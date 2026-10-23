using EvolutCRM.Data; // ✅ repo
using EvolutCRM.Services;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using OpenAI;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvolutCRM.Services
{
    public class AiService
    {
        // NOVO
        private readonly IConfiguration _config;
        private readonly DocService _docs;
        private readonly AiTicketInsightsRepository _insights;
        private readonly IHttpClientFactory _httpFactory;
        private readonly OpenAiClient _openAiClient;
        private readonly EmbeddingIndex _index;

        public AiService(
            IConfiguration config,
            DocService docs,
            AiTicketInsightsRepository insights,
            IHttpClientFactory httpFactory,
            OpenAiClient openAiClient,
            EmbeddingIndex index)
        {
            _config = config;
            _docs = docs;
            _insights = insights;
            _httpFactory = httpFactory;
            _openAiClient = openAiClient;
            _index = index;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            var text = Regex.Replace(html, "<.*?>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static string Cut(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

        private static string ExpandQuery(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return "";
            var x = q.Trim();

            var lower = x.ToLowerInvariant();

            if (lower.Contains("pdv"))
                x += " \"frente de caixa\" \"ponto de venda\" caixa abrir caixa fechar caixa";

            if (lower.Contains("contas a pagar") || lower.Contains("conta a pagar") || lower.Contains("pagar"))
                x += " financeiro \"contas a pagar\" fornecedor lançamento baixa vencimento título";

            if (lower.Contains("contas a receber") || lower.Contains("conta a receber") || lower.Contains("receber"))
                x += " financeiro \"contas a receber\" cliente lançamento baixa vencimento título";

            if (lower.Contains("boleto"))
                x += " financeiro cobrança cobrança bancária remessa retorno";

            if (lower.Contains("nota") || lower.Contains("nfe") || lower.Contains("nf-e"))
                x += " fiscal xml danfe cfop emissão certificado";

            return x;
        }

        // ✅ pega uma "tag" simples baseada na pergunta (para FindSimilarAsync)
        private static string? ExtractTagCandidate(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return null;

            var msg = userMessage.ToLowerInvariant();

            var map = new Dictionary<string, string[]>
            {
                ["entrada_mercadoria_xml"] = new[]
    {
        "entrada de mercadoria por xml", "entrada mercadoria xml",
        "entrada de mercadorias por xml", "entrada mercadorias xml",
        "entrada por xml", "xml de entrada", "importar xml",
        "importação de xml", "importacao de xml", "buscar xml",
        "consultar chave", "chave da nota", "vincular produto xml",
        "cadastrar produto xml", "fornecedor xml"
    },

                ["entrada_mercadoria_manual"] = new[]
    {
        "entrada de mercadoria manual", "entrada mercadoria manual",
        "entrada de mercadorias manual", "entrada manual",
        "dar entrada manual", "lançar mercadoria manual",
        "lancar mercadoria manual", "cabeçalho da nota",
        "cabecalho da nota", "numero nota", "número nota",
        "frete entrada", "validade produto", "lote produto"
    },

                ["cadastro_produto"] = new[]
    {
        "cadastro de produtos", "cadastro produto", "cadastrar produto",
        "produto", "produtos", "código de barras", "codigo de barras",
        "descrição do produto", "descricao do produto", "seção",
        "secao", "unidade", "preço de custo", "preco de custo",
        "markup", "margem", "margem real", "produto balança",
        "produto balanca", "produto gelado", "estoque minimo",
        "estoque mínimo", "ncm", "tributação", "tributacao"
    },

                ["cadastro_fornecedor"] = new[]
    {
        "cadastro de fornecedor", "cadastro fornecedor", "cadastrar fornecedor",
        "fornecedor", "fornecedores", "consultar cnpj fornecedor",
        "cnpj fornecedor", "dados do fornecedor", "buscar fornecedor",
        "juridico fornecedor", "jurídico fornecedor", "razao social fornecedor",
        "razão social fornecedor", "fornecedor inativo", "plano de contas fornecedor",
        "centro de custo fornecedor"
    },

                ["cadastro_cliente"] = new[]
    {
        "cadastro de cliente", "cadastro cliente", "cadastrar cliente",
        "cliente", "clientes", "consultar cpf", "consultar cnpj cliente",
        "cnpj cliente", "dados do cliente", "buscar cliente",
        "cliente inativo", "nome razão", "nome razao",
        "apelido fantasia", "bloqueia venda crediario",
        "bloqueia venda crediário", "limite cliente", "consultar limite",
        "contribuinte icms", "tipo contribuinte"
    },

                
                ["formas_pagamento_pdv"] = new[]
    {
        "formas de pagamento", "forma de pagamento", "pagamento pdv",
        "dinheiro", "cheque à vista", "cheque a vista",
        "cheque à prazo", "cheque a prazo", "cartão",
        "cartao", "crédito", "credito", "débito",
        "debito", "crediário", "crediario", "fiado",
        "pix", "qr code pix", "f5", "f6", "f7", "f8", "f9", "f10"
    },

                ["cadastro_cartao"] = new[]
    {
        "cadastro de cartão", "cadastro de cartao", "cadastrar cartão",
        "cadastrar cartao", "cartão", "cartao", "bandeira",
        "operadora", "taxa cartão", "taxa cartao",
        "dias para vencimento", "crédito", "credito", "débito", "debito"
    },

                ["nfe"] = new[]
    {
        "nfe", "nf-e", "nota fiscal", "emissão de nota fiscal",
        "emissao de nota fiscal", "emitir nota fiscal",
        "danfe", "cfop", "certificado", "nota fiscal de entrada",
        "nota fiscal de saída", "nota fiscal de saida",
        "destinatário", "destinatario", "finalidade da nota",
        "cst", "ipi", "pis", "cofins", "dados adicionais"
    },

                ["nfce"] = new[]
    {
        "nfce", "nfc-e", "cupom fiscal", "nota consumidor",
        "nota fiscal consumidor", "emitir nfce", "emitir nfc-e",
        "cancelar nfce", "cancelar nfc-e"
    },

                ["contas_pagar"] = new[]
    {
        "contas a pagar", "conta a pagar", "título a pagar",
        "titulo a pagar", "duplicata a pagar", "pagamento fornecedor",
        "baixar titulo pagar", "baixar título pagar",
        "baixa contas a pagar", "bx pagar", "boleto fornecedor",
        "vencimento fornecedor", "plano de contas pagar"
    },

                ["contas_receber"] = new[]
    {
        "contas a receber", "conta a receber", "título a receber",
        "titulo a receber", "duplicata a receber", "recebimento cliente",
        "baixa contas a receber", "baixar titulo receber",
        "baixar título receber", "bx receber", "crediario",
        "crediário", "cobrança", "cobranca", "vencimento cliente"
    },

                ["bancos"] = new[]
    {
        "banco", "bancos", "conta bancária", "conta bancaria",
        "cadastro de banco", "cadastro conta bancaria",
        "movimentação bancária", "movimentacao bancaria",
        "movimentações das contas bancarias", "movimentações das contas bancárias",
        "saldo bancario", "saldo bancário", "agência", "agencia",
        "conta", "chave pix banco"
    },

                ["estoque"] = new[]
    {
        "estoque", "inventario", "inventário", "acertar estoque",
        "acerto de estoque", "ruptura", "estoque minimo",
        "estoque mínimo", "estoque maximo", "estoque máximo",
        "validade", "lote", "extrato produto", "entrada por produto"
    },

                ["etiqueta"] = new[]
    {
        "etiqueta", "etiquetas", "emissão de etiqueta",
        "emissao de etiqueta", "etiqueta gondola", "etiqueta gôndola",
        "balança", "balanca", "produto balança", "produto balanca",
        "imprimir etiqueta"
    },

                ["relatorio"] = new[]
    {
        "relatório", "relatorio", "relatórios", "relatorios",
        "imprimir relatório", "imprimir relatorio", "vendas por período",
        "vendas por periodo", "relatório de vendas", "relatorio de vendas",
        "relatório entrada de mercadoria", "relatorio entrada de mercadoria",
        "sugestão de compras", "sugestao de compras"
    },

                ["dashboard"] = new[]
    {
        "dashboard", "painel", "aba geral", "vendas do dia",
        "vendas do mês", "vendas do mes", "titulos em aberto",
        "títulos em aberto", "nfce mensal", "nfe mensal",
        "pagos", "recebidos", "caixa", "custo", "lucro",
        "compras", "serviços", "servicos"
    },

                ["financeiro"] = new[]
    {
        "financeiro", "baixa", "lançamento", "lancamento",
        "parcelamento", "juros", "multa", "vencimento",
        "boleto", "cobrança", "cobranca", "remessa",
        "retorno", "estorno", "estornar baixa"
    },
                ["baixa_contas_pagar"] = new[]
{
    "baixa contas a pagar", "baixar contas a pagar", "bx pagar",
    "baixa pagar", "baixar boleto fornecedor", "pagar fornecedor",
    "estornar baixa pagar", "estorno baixa pagar", "boleto a pagar",
    "situação do boleto", "situacao do boleto", "período de vencimento",
    "periodo de vencimento"
},

                ["lancamento_rapido_pagar"] = new[]
{
    "lançamento rápido pagar", "lancamento rapido pagar",
    "lançamento rápido de contas a pagar", "lancamento rapido de contas a pagar",
    "lanc rapido pagar", "lançar conta rápido", "lancar conta rapido",
    "lançar despesa", "lancar despesa", "lançar boleto",
    "lancar boleto"
},

                ["acerto_estoque"] = new[]
{
    "acerto de estoque", "acertar estoque", "ajustar estoque",
    "corrigir estoque", "alterar estoque", "estoque atual",
    "tipo de movimentação", "tipo de movimentacao",
    "movimentação entrada", "movimentacao entrada",
    "movimentação saída", "movimentacao saida",
    "quantidade estoque", "gravar acerto"
},

                ["compras_sugestao"] = new[]
{
    "compras sugestão", "compras sugestao", "sugestão de compras",
    "sugestao de compras", "comprar produto", "produtos parados",
    "produto parado", "ruptura", "produto em falta",
    "evitar ruptura", "media de vendas", "média de vendas",
    "dicas de compras", "eli compras"
},

                ["movimentacao_bancaria"] = new[]
{
    "movimentação bancária", "movimentacao bancaria",
    "movimentações bancárias", "movimentacoes bancarias",
    "movimentações das contas bancarias",
    "movimentações das contas bancárias",
    "conta bancária", "conta bancaria",
    "saldo banco", "saldo bancário", "saldo bancario",
    "entrada banco", "saída banco", "saida banco"
},

                ["plano_contas"] = new[]
{
    "plano de contas", "cadastro plano de contas",
    "cadastrar plano de contas", "conta financeira",
    "categoria financeira", "tipo de despesa",
    "tipo de receita"
},

                ["centro_custo"] = new[]
{
    "centro de custo", "centro de custos",
    "cadastro centro de custo", "cadastrar centro de custo",
    "setor financeiro", "setor da despesa"
},

                ["cadastro_secoes"] = new[]
{
    "cadastro de seção", "cadastro de secao",
    "cadastrar seção", "cadastrar secao",
    "seção do produto", "secao do produto",
    "grupo de produto", "categoria de produto"
},

                ["consulta_vendas"] = new[]
{
    "consulta vendas", "consultar vendas", "vendas realizadas",
    "vendas por período", "vendas por periodo",
    "venda em espera", "venda finalizada",
    "venda cancelada", "cancelar venda",
    "reimprimir venda", "detalhe da venda"
},

                ["fechamento_caixa"] = new[]
{
    "fechamento de caixa", "fechar caixa", "movimento do caixa",
    "caixa detalhado", "imprimir caixa", "troco inicial",
    "valor real em caixa", "recebimentos do caixa",
    "retirada do caixa", "sangria", "acréscimo no caixa",
    "acrescimo no caixa", "transferência caixa", "transferencia caixa"
},

                ["emissao_etiqueta"] = new[]
{
    "emissão de etiqueta", "emissao de etiqueta",
    "etiqueta gôndola", "etiqueta gondola",
    "imprimir etiqueta", "produto etiqueta",
    "quantidade etiqueta", "etiqueta produto",
    "etiqueta balança", "etiqueta balanca"
},
                ["pdv_venda"] = new[]
{
    "pdv", "ponto de venda", "frente de caixa",
    "passar venda", "lançar produto pdv", "lancar produto pdv",
    "código de barras pdv", "codigo de barras pdv",
    "fechar venda", "forma de pagamento pdv",
    "desconto pdv", "produto gelado pdv",
    "delivery pdv"
},

                ["pdv_caixa"] = new[]
{
    "abrir caixa", "abertura de caixa", "fechar caixa",
    "fechamento de caixa", "troco inicial", "sangria",
    "retirada do caixa", "acréscimo no caixa", "acrescimo no caixa",
    "transferência caixa", "transferencia caixa"
}
            };

            foreach (var item in map)
            {
                if (item.Value.Any(term => msg.Contains(term)))
                    return item.Key;
            }

            var words = Regex.Matches(msg, @"\b[\p{L}0-9\-]{4,}\b")
                .Select(m => m.Value)
                .Where(w => w is not ("para" or "como" or "isso" or "aqui" or "nao" or "não" or "com" or "onde" or "quando"))
                .ToList();

            return words.OrderByDescending(w => w.Length).FirstOrDefault();
        }

        // Compatibilidade: endpoint /api/help/suggest (recebe só texto)
        public Task<string> AskAsync(string userMessage, string? conversationContext = null, CancellationToken ct = default)
            => AskAsync(new AiPedidoSugestao
            {
                MensagensCliente = userMessage ?? "",
                Conversa = conversationContext
            }, ct);

        public async Task<string> AskAsync(AiPedidoSugestao pedido, CancellationToken ct = default)
            => (await SugerirAsync(pedido, ct)).Resposta;

        /// <summary>
        /// Gera a sugestão de resposta com fontes e confiança, e grava em AiSugestaoLog
        /// (para medir quanto o atendente aproveita).
        /// </summary>
        public async Task<AiSugestaoResultado> SugerirAsync(AiPedidoSugestao pedido, CancellationToken ct = default)
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            static AiSugestaoResultado Falha(string msg) => new() { Ok = false, Resposta = msg };

            try
            {
                var apiKey = _config["OpenAI:ApiKey"];
                var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";
                // Modelo da resposta final (opcional): permite usar um modelo maior só aqui
                var modeloResposta = _config["OpenAI:ModeloResposta"];
                if (string.IsNullOrWhiteSpace(modeloResposta)) modeloResposta = model;

                if (string.IsNullOrWhiteSpace(apiKey))
                    return Falha("API KEY não configurada.");

                var mensagens = (pedido.MensagensCliente ?? "").Trim();
                var imagens = await CarregarImagensAsync(pedido.CodTicket, pedido.CodigosImagens, ct);

                if (mensagens.Length == 0 && imagens.Count == 0)
                    return Falha("Pergunta vazia.");

                LogHelper.Log("===== NOVA REQUISIÇÃO IA =====");
                LogHelper.Log($"TICKET: {pedido.CodTicket} | ASSUNTO: {pedido.Assunto} | ORIGEM: {pedido.Origem}");
                LogHelper.Log("MENSAGENS DO CLIENTE:");
                LogHelper.Log(mensagens.Length == 0 ? "(só imagem)" : mensagens);
                LogHelper.Log($"IMAGENS ENVIADAS PARA A IA: {imagens.Count}");
                LogHelper.Log("MODELO: " + model + " | RESPOSTA: " + modeloResposta);

                // 1) Entender a dúvida: reescreve como pergunta completa e lê os prints
                var (pergunta, textoImagens) = await EntenderPerguntaAsync(pedido, mensagens, imagens, model, ct);

                // Se a etapa de entendimento falhar, usa a regra antiga (agora com o histórico lido corretamente)
                if (string.IsNullOrWhiteSpace(pergunta))
                    pergunta = BuildEffectiveUserQuery(mensagens, pedido.Conversa);
                if (string.IsNullOrWhiteSpace(pergunta))
                    pergunta = string.IsNullOrWhiteSpace(textoImagens) ? (pedido.Assunto ?? "") : textoImagens;

                var textoBusca = string.IsNullOrWhiteSpace(textoImagens) ? pergunta : pergunta + "\n" + textoImagens;
                // Sinônimos (PDV = frente de caixa etc.) entram na busca por palavras com peso menor
                var sinonimos = Sinonimos(textoBusca);

                LogHelper.Log("PERGUNTA ENTENDIDA: " + pergunta);
                if (!string.IsNullOrWhiteSpace(textoImagens))
                    LogHelper.Log("TEXTO DOS PRINTS: " + textoImagens);

                // 2) Buscar por embeddings (candidatos: até 2 pedaços por documentação e 2 casos por ticket)
                float[] qVec;
                try { qVec = await _openAiClient.GetEmbeddingAsync(textoBusca); }
                catch { qVec = Array.Empty<float>(); }

                var docHits = qVec.Length > 0 ? _index.SearchDocs(qVec, textoBusca, 12, pedido.CodEmp, sinonimos) : new();
                var insHits = qVec.Length > 0 ? _index.SearchInsights(qVec, textoBusca, 14, pedido.CodEmp, sinonimos) : new();

                // Avaliação automática: tira os casos do próprio ticket testado
                if (pedido.IgnorarTicketNaBusca > 0)
                    insHits = insHits.Where(h => h.Item.TicketId != pedido.IgnorarTicketNaBusca).ToList();
                insHits = insHits.Take(12).ToList();

                LogHelper.Log($"[BUSCA] Docs encontrados: {docHits.Count} | Insights encontrados: {insHits.Count}");
                foreach (var d in docHits.Take(3))
                    LogHelper.Log($"[DOCS] {d.Score:F2} {d.Item.Titulo}");
                foreach (var i in insHits.Take(3))
                    LogHelper.Log($"[INSIGHTS] {i.Score:F2} {i.Item.Resumo}");

                // 3) Filtrar por relevância
                //    Com reordenação (padrão): a IA dá nota 0-3 para os 20 melhores candidatos e só fica o que ajuda.
                //    Sem ela (ou se falhar): corte pela pontuação da busca.
                //    Ajustáveis no appsettings: IA:Rerank, IA:MinCandidato, IA:MinRelevante, IA:NivelAlto
                float minCandidato = LerFloat("IA:MinCandidato", 0.22f);
                float minRelevante = LerFloat("IA:MinRelevante", 0.30f);
                float nivelAlto = LerFloat("IA:NivelAlto", 0.38f);
                bool usarRerank = !string.Equals(_config["IA:Rerank"], "false", StringComparison.OrdinalIgnoreCase);

                List<(EmbeddingIndex.DocVec Item, float Score)> docsBons;
                List<(EmbeddingIndex.InsightVec Item, float Score)> insBons;
                string nivel;

                var candDocs = docHits.Where(h => h.Score >= minCandidato).Take(10).ToList();
                var candIns = insHits.Where(h => h.Score >= minCandidato).Take(10).ToList();

                Dictionary<string, int>? notas = null;
                if (usarRerank && candDocs.Count + candIns.Count > 0)
                    notas = await ReordenarAsync(textoBusca, candDocs, candIns, model, ct);

                if (notas != null)
                {
                    int NotaD(int i) => notas.TryGetValue("D" + (i + 1), out var n) ? n : 0;
                    int NotaC(int i) => notas.TryGetValue("C" + (i + 1), out var n) ? n : 0;

                    var dOrd = candDocs.Select((h, i) => (h, nota: NotaD(i)))
                        .Where(x => x.nota >= 2).OrderByDescending(x => x.nota).ThenByDescending(x => x.h.Score).ToList();
                    var cOrd = candIns.Select((h, i) => (h, nota: NotaC(i)))
                        .Where(x => x.nota >= 2).OrderByDescending(x => x.nota).ThenByDescending(x => x.h.Score).ToList();

                    docsBons = dOrd.Take(4).Select(x => x.h).ToList();
                    insBons = cOrd.Take(5).Select(x => x.h).ToList();

                    bool temNota3 = dOrd.Any(x => x.nota == 3) || cOrd.Any(x => x.nota == 3);
                    nivel = temNota3 ? "ALTO" : (docsBons.Count + insBons.Count > 0 ? "MEDIO" : "BAIXO");

                    LogHelper.Log("[RERANK] " + string.Join(" ", notas.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
                }
                else
                {
                    docsBons = docHits.Where(h => h.Score >= minRelevante).Take(5).ToList();
                    insBons = insHits.Where(h => h.Score >= minRelevante).Take(6).ToList();
                    float melhorBusca = Math.Max(
                        docsBons.Count > 0 ? docsBons[0].Score : 0,
                        insBons.Count > 0 ? insBons[0].Score : 0);
                    nivel = melhorBusca >= nivelAlto ? "ALTO" : "BAIXO";
                }

                float melhor = Math.Max(
                    docsBons.Count > 0 ? docsBons.Max(h => h.Score) : 0,
                    insBons.Count > 0 ? insBons.Max(h => h.Score) : 0);

                // 4) Montar contexto — cada trecho com um id (D1, C1...) para a IA dizer o que usou
                var fontesDisponiveis = new Dictionary<string, AiFonte>(StringComparer.OrdinalIgnoreCase);
                var ctx = new StringBuilder();
                ctx.AppendLine($"NÍVEL: {nivel}");
                ctx.AppendLine();

                if (docsBons.Count == 0 && insBons.Count == 0)
                {
                    ctx.AppendLine("Nenhuma base relevante encontrada para esta pergunta.");
                }
                else
                {
                    if (docsBons.Count > 0)
                    {
                        ctx.AppendLine("DOCUMENTAÇÃO RELEVANTE:");
                        int n = 0;
                        foreach (var d in docsBons)
                        {
                            var idF = "D" + (++n);
                            fontesDisponiveis[idF] = new AiFonte
                            {
                                Id = idF, Tipo = "doc", Titulo = d.Item.Titulo,
                                CodDoc = d.Item.CodDoc, Score = d.Score
                            };
                            ctx.AppendLine("-----");
                            ctx.AppendLine($"[{idF}] TÍTULO: {d.Item.Titulo}");
                            ctx.AppendLine($"CONTEÚDO: {Cut(d.Item.Texto, 1600)}");
                        }
                        ctx.AppendLine();
                    }
                    if (insBons.Count > 0)
                    {
                        ctx.AppendLine("CASOS ANTERIORES (referência de solução):");
                        int n = 0;
                        foreach (var it in insBons)
                        {
                            var idF = "C" + (++n);
                            fontesDisponiveis[idF] = new AiFonte
                            {
                                Id = idF, Tipo = "caso", Titulo = Cut(it.Item.Resumo ?? "", 140),
                                CodTicket = it.Item.TicketId, Score = it.Score
                            };
                            ctx.AppendLine("-----");
                            ctx.AppendLine($"[{idF}] PERGUNTA: {Cut(it.Item.Resumo ?? "", 300)}");
                            ctx.AppendLine($"SOLUÇÃO: {Cut(it.Item.Solucao ?? "", 600)}");
                        }
                    }
                }

                LogHelper.Log($"[CONTEXTO] Nível={nivel} | DocsBons={docsBons.Count} | InsBons={insBons.Count}");
                LogHelper.Log(ctx.ToString());

                // 5) Mensagem para a IA: conversa + o que o cliente mandou + o que tinha no print + a dúvida entendida
                var conteudoUsuario = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(pedido.Assunto))
                {
                    conteudoUsuario.AppendLine("Assunto do ticket: " + pedido.Assunto.Trim());
                    conteudoUsuario.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(pedido.Versao))
                {
                    conteudoUsuario.AppendLine("Versão do sistema do cliente: " + pedido.Versao.Trim());
                    conteudoUsuario.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(pedido.Conversa))
                {
                    conteudoUsuario.AppendLine("Conversa até agora:");
                    conteudoUsuario.AppendLine(pedido.Conversa);
                    conteudoUsuario.AppendLine();
                }
                conteudoUsuario.AppendLine("Mensagem(ns) atual(is) do cliente, ainda sem resposta:");
                conteudoUsuario.AppendLine(mensagens.Length == 0 ? "(o cliente enviou só imagem)" : mensagens);
                if (!string.IsNullOrWhiteSpace(textoImagens))
                {
                    conteudoUsuario.AppendLine();
                    conteudoUsuario.AppendLine("O cliente também enviou print(s). O que aparece neles:");
                    conteudoUsuario.AppendLine(textoImagens);
                }
                conteudoUsuario.AppendLine();
                conteudoUsuario.AppendLine("Em resumo, o cliente quer saber: " + pergunta);

                // 6) Chamar OpenAI (saída em JSON: resposta + fontes usadas + confiança)
                var http = _httpFactory.CreateClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var body = new
                {
                    model = modeloResposta,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPromptResposta + SystemPromptFormatoSaida },
                        new { role = "system", content = "CONTEXTO RECUPERADO (use só o que está aqui):\n\n" + ctx.ToString() },
                        new { role = "user", content = conteudoUsuario.ToString() }
                    },
                    temperature = 0.2,
                    response_format = new { type = "json_object" }
                };

                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                LogHelper.Log("[OPENAI] ENVIANDO REQUISIÇÃO...");
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
                var respText = await resp.Content.ReadAsStringAsync(ct);
                LogHelper.Log("[OPENAI] STATUS HTTP: " + (int)resp.StatusCode);

                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log("[OPENAI] ERRO HTTP: " + respText);
                    return Falha($"Erro OpenAI HTTP {(int)resp.StatusCode}: {respText}");
                }

                string bruto;
                using (var doc = JsonDocument.Parse(respText))
                {
                    bruto = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";
                }

                var (reply, idsUsados, confiancaIa) = LerSaidaResposta(bruto);
                LogHelper.Log("[OPENAI] RESPOSTA FINAL:");
                LogHelper.Log(reply);
                LogHelper.Log($"[OPENAI] FONTES: {string.Join(",", idsUsados)} | CONFIANÇA IA: {confiancaIa}");

                if (string.IsNullOrWhiteSpace(reply))
                    return Falha("Não consegui gerar uma resposta. Informe o módulo/tela e a mensagem de erro (se houver).");

                var fontes = idsUsados
                    .Where(fontesDisponiveis.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(x => fontesDisponiveis[x])
                    .ToList();

                var confianca = CalcularConfianca(confiancaIa, nivel, fontes.Count);
                relogio.Stop();

                long codLog = await GravarLogAsync(pedido, pergunta, reply, confianca, nivel, melhor,
                    fontes, modeloResposta, (int)relogio.ElapsedMilliseconds);

                LogHelper.Log($"[DEBUG] DocsBons={docsBons.Count} | InsBons={insBons.Count} | Nivel={nivel} | Confiança={confianca} | Log={codLog}");
                LogHelper.Log("===== FIM DA REQUISIÇÃO IA =====");

                return new AiSugestaoResultado
                {
                    Ok = true,
                    Resposta = reply,
                    Confianca = confianca,
                    Fontes = fontes,
                    CodLog = codLog,
                    PerguntaEntendida = pergunta
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AI] Erro COMPLETO: " + ex);
                LogHelper.Log("[AI] ERRO COMPLETO: " + ex);
                return Falha("ERRO IA: " + ex.Message);
            }
        }

        private float LerFloat(string chave, float padrao)
            => float.TryParse(_config[chave], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : padrao;

        /// <summary>
        /// Sinônimos do vocabulário do suporte (só os termos extras, sem repetir a pergunta).
        /// Usa palavra inteira e sem acento, para "anotação" não puxar "nota fiscal".
        /// </summary>
        private static string Sinonimos(string q)
        {
            var t = " " + Regex.Replace(EmbeddingIndex.Normalizar(q), @"[^a-z0-9]+", " ") + " ";
            bool Tem(params string[] termos) => termos.Any(x => t.Contains(" " + x + " "));
            var sb = new StringBuilder();

            if (Tem("pdv", "caixa", "frente"))
                sb.Append(" frente caixa ponto venda pdv abertura fechamento sangria suprimento cupom");
            if (Tem("pagar", "fornecedor", "fornecedores"))
                sb.Append(" financeiro contas pagar fornecedor lancamento baixa vencimento titulo");
            if (Tem("receber", "recebimento", "recebimentos"))
                sb.Append(" financeiro contas receber lancamento baixa vencimento titulo duplicata");
            if (Tem("boleto", "boletos"))
                sb.Append(" financeiro cobranca bancaria remessa retorno banco");
            if (Tem("nota", "notas", "nfe", "nf", "nfce", "danfe", "xml"))
                sb.Append(" fiscal nfe nfce xml danfe cfop emissao sefaz certificado");
            if (Tem("rejeicao", "rejeitada", "rejeitou", "sefaz"))
                sb.Append(" rejeicao sefaz nfe nfce fiscal transmissao");
            if (Tem("estoque", "saldo", "inventario"))
                sb.Append(" estoque produto saldo movimentacao inventario entrada saida");
            if (Tem("impressora", "imprimir", "impressao", "imprime"))
                sb.Append(" impressora impressao imprimir driver configuracao");
            if (Tem("certificado"))
                sb.Append(" certificado digital a1 a3 validade senha");
            if (Tem("tef", "cartao", "maquininha", "pix"))
                sb.Append(" tef cartao pagamento pinpad pix recebimento");

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Reordenação: a IA lê os candidatos e dá nota 0-3 de quanto cada um ajuda a responder.
        /// Retorna null se falhar (aí vale o corte pela pontuação da busca).
        /// </summary>
        private async Task<Dictionary<string, int>?> ReordenarAsync(string pergunta,
            List<(EmbeddingIndex.DocVec Item, float Score)> docs,
            List<(EmbeddingIndex.InsightVec Item, float Score)> casos,
            string model, CancellationToken ct)
        {
            try
            {
                var apiKey = _config["OpenAI:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey)) return null;

                var lista = new StringBuilder();
                for (int i = 0; i < docs.Count; i++)
                {
                    lista.AppendLine($"[D{i + 1}] {docs[i].Item.Titulo}");
                    lista.AppendLine(Cut(docs[i].Item.Texto ?? "", 550).Replace("\n", " "));
                    lista.AppendLine();
                }
                for (int i = 0; i < casos.Count; i++)
                {
                    lista.AppendLine($"[C{i + 1}] Caso: {Cut(casos[i].Item.Resumo ?? "", 250)}");
                    lista.AppendLine("Solução: " + Cut(casos[i].Item.Solucao ?? "", 350));
                    lista.AppendLine();
                }

                var http = _httpFactory.CreateClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                http.Timeout = TimeSpan.FromSeconds(25);

                var body = new
                {
                    model,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = @"Você seleciona material de apoio para o suporte do sistema Evolut (ERP).
Para CADA trecho, dê uma nota de quanto ele ajuda a responder a dúvida do cliente:
3 = responde diretamente a dúvida (mesmo assunto, mesma tela/ação);
2 = relacionado e útil (parte da resposta, caso parecido com solução aplicável);
1 = mesmo módulo/assunto geral, mas não responde;
0 = não tem relação.
Seja rigoroso: palavra em comum não basta, precisa ser o mesmo problema.
Responda só JSON: { ""notas"": { ""D1"": 0, ""C1"": 3 } } com todos os ids." },
                        new { role = "user", content = "DÚVIDA DO CLIENTE:\n" + pergunta + "\n\nTRECHOS:\n" + lista }
                    }
                };

                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions",
                    new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
                var txt = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log($"[RERANK] HTTP {(int)resp.StatusCode}: {Cut(txt, 300)}");
                    return null;
                }

                using var doc = JsonDocument.Parse(txt);
                var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                using var j = JsonDocument.Parse(conteudo);
                var raiz = j.RootElement.TryGetProperty("notas", out var pn) ? pn : j.RootElement;
                if (raiz.ValueKind != JsonValueKind.Object) return null;

                var notas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in raiz.EnumerateObject())
                {
                    int v = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetInt32()
                          : int.TryParse(p.Value.GetString(), out var x) ? x : 0;
                    notas[p.Name.Trim('[', ']', ' ')] = Math.Clamp(v, 0, 3);
                }
                return notas.Count == 0 ? null : notas;
            }
            catch (Exception ex)
            {
                LogHelper.Log("[RERANK] Falhou, usando corte da busca: " + ex.Message);
                return null;
            }
        }

        private const string SystemPromptFormatoSaida = @"

FORMATO DA SAÍDA (obrigatório)
Responda SOMENTE com um JSON neste formato:
{ ""resposta"": ""texto que vai para o cliente"", ""fontes"": [""D1"", ""C2""], ""confianca"": ""alta"" }
- ""resposta"": a mensagem para o cliente, seguindo todas as regras acima. Nunca cite os ids (D1, C1...) nem fale em fontes, documentação ou casos.
- ""fontes"": ids dos trechos do CONTEXTO que você realmente usou para responder. Lista vazia se não usou nenhum.
- ""confianca"":
  ""alta"" = o CONTEXTO responde exatamente o que o cliente perguntou;
  ""media"" = o CONTEXTO responde em parte, ou você precisou adaptar de um caso parecido;
  ""baixa"" = o CONTEXTO não cobre a dúvida; a resposta é só orientação, pergunta ou direção provável.
";

        /// <summary>Lê o JSON da resposta. Se vier texto puro, usa o texto como resposta.</summary>
        private static (string Resposta, List<string> Fontes, string Confianca) LerSaidaResposta(string bruto)
        {
            bruto = (bruto ?? "").Trim();
            var fontes = new List<string>();
            if (bruto.Length == 0) return ("", fontes, "baixa");

            try
            {
                using var doc = JsonDocument.Parse(bruto);
                var r = doc.RootElement;
                var resposta = r.TryGetProperty("resposta", out var pr) && pr.ValueKind == JsonValueKind.String
                    ? (pr.GetString() ?? "").Trim() : "";
                if (r.TryGetProperty("fontes", out var pf) && pf.ValueKind == JsonValueKind.Array)
                    foreach (var f in pf.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(f.GetString()))
                            fontes.Add(f.GetString()!.Trim().Trim('[', ']'));
                var conf = r.TryGetProperty("confianca", out var pc) && pc.ValueKind == JsonValueKind.String
                    ? (pc.GetString() ?? "").Trim().ToLowerInvariant() : "baixa";
                return (resposta, fontes, conf);
            }
            catch
            {
                // Modelo devolveu texto fora do JSON: aproveita o texto
                return (bruto, fontes, "baixa");
            }
        }

        /// <summary>
        /// Junta a opinião da IA com a força da busca: "alta" só quando a busca achou base forte
        /// e a IA usou alguma fonte. Sem fonte usada, é sempre "baixa".
        /// </summary>
        private static string CalcularConfianca(string confiancaIa, string nivel, int qtdFontes)
        {
            var c = (confiancaIa ?? "").Replace("é", "e").ToLowerInvariant();
            if (qtdFontes == 0 || c.StartsWith("baix")) return "baixa";
            if (c.StartsWith("alt") && nivel == "ALTO") return "alta";
            return "media";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Log de sugestões: mede quanto o atendente aproveita a resposta da IA
        // (tabela AiSugestaoLog — script Scripts/2026-10-23_IA_SugestaoLog.sql)

        private async Task<long> GravarLogAsync(AiPedidoSugestao pedido, string pergunta, string resposta,
            string confianca, string nivel, float melhorScore, List<AiFonte> fontes, string modelo, int duracaoMs)
        {
            try
            {
                await using var conn = new SqlConnection(_config.GetConnectionString("Connection"));
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(@"
INSERT INTO AiSugestaoLog
    (CodTicketChamadoC, CodEmp, Usuario, Origem, Pergunta, RespostaIA, Confianca, Nivel,
     MelhorScore, Fontes, Modelo, DuracaoMs)
OUTPUT INSERTED.CodAiSugestaoLog
VALUES
    (@ticket, @emp, @usuario, @origem, @pergunta, @resposta, @confianca, @nivel,
     @score, @fontes, @modelo, @duracao);", conn);

                cmd.Parameters.AddWithValue("@ticket", pedido.CodTicket > 0 ? pedido.CodTicket : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@emp", pedido.CodEmp > 0 ? pedido.CodEmp : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@usuario", string.IsNullOrWhiteSpace(pedido.Usuario) ? DBNull.Value : (object)Cut(pedido.Usuario, 100));
                cmd.Parameters.AddWithValue("@origem", string.IsNullOrWhiteSpace(pedido.Origem) ? "ticket" : Cut(pedido.Origem, 20));
                cmd.Parameters.AddWithValue("@pergunta", pergunta ?? "");
                cmd.Parameters.AddWithValue("@resposta", resposta ?? "");
                cmd.Parameters.AddWithValue("@confianca", confianca);
                cmd.Parameters.AddWithValue("@nivel", nivel);
                cmd.Parameters.AddWithValue("@score", Math.Round((decimal)melhorScore, 3));
                cmd.Parameters.AddWithValue("@fontes", JsonSerializer.Serialize(fontes.Select(f => new
                {
                    f.Id, f.Tipo, f.Titulo, f.CodDoc, f.CodTicket, Score = Math.Round(f.Score, 3)
                })));
                cmd.Parameters.AddWithValue("@modelo", Cut(modelo ?? "", 60));
                cmd.Parameters.AddWithValue("@duracao", duracaoMs);

                var r = await cmd.ExecuteScalarAsync();
                return r == null || r == DBNull.Value ? 0 : Convert.ToInt64(r);
            }
            catch (Exception ex)
            {
                // Sem a tabela (script não rodado) a sugestão continua funcionando
                LogHelper.Log("[AI LOG] Não gravou AiSugestaoLog: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Grava o que o atendente realmente mandou e o quanto parece com a sugestão (0 a 100).
        /// </summary>
        public async Task RegistrarEnvioAsync(long codLog, string? textoEnviado)
        {
            if (codLog <= 0) return;
            try
            {
                var sugestao = "";
                await using var conn = new SqlConnection(_config.GetConnectionString("Connection"));
                await conn.OpenAsync();

                await using (var sel = new SqlCommand(
                    "SELECT RespostaIA FROM AiSugestaoLog WHERE CodAiSugestaoLog = @c AND DataHoraEnvio IS NULL", conn))
                {
                    sel.Parameters.AddWithValue("@c", codLog);
                    var r = await sel.ExecuteScalarAsync();
                    if (r == null || r == DBNull.Value) return; // já registrado ou não existe
                    sugestao = r.ToString() ?? "";
                }

                var enviado = (textoEnviado ?? "").Trim();
                var sim = Similaridade(sugestao, enviado);

                await using var upd = new SqlCommand(@"
UPDATE AiSugestaoLog
   SET RespostaEnviada = @texto, DataHoraEnvio = GETDATE(), Similaridade = @sim
 WHERE CodAiSugestaoLog = @c AND DataHoraEnvio IS NULL;", conn);
                upd.Parameters.AddWithValue("@texto", enviado);
                upd.Parameters.AddWithValue("@sim", sim);
                upd.Parameters.AddWithValue("@c", codLog);
                await upd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogHelper.Log("[AI LOG] RegistrarEnvio: " + ex.Message);
            }
        }

        /// <summary>👍 = 1, 👎 = -1.</summary>
        public async Task RegistrarFeedbackAsync(long codLog, int valor)
        {
            if (codLog <= 0) return;
            try
            {
                await using var conn = new SqlConnection(_config.GetConnectionString("Connection"));
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "UPDATE AiSugestaoLog SET Feedback = @v, DataHoraFeedback = GETDATE() WHERE CodAiSugestaoLog = @c", conn);
                cmd.Parameters.AddWithValue("@v", valor > 0 ? 1 : -1);
                cmd.Parameters.AddWithValue("@c", codLog);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogHelper.Log("[AI LOG] RegistrarFeedback: " + ex.Message);
            }
        }

        /// <summary>
        /// Parecença entre dois textos (0 a 100), por palavras, ignorando acento, pontuação e maiúsculas.
        /// 100 = mandou igual; acima de 80 = só retocou; abaixo de 40 = praticamente reescreveu.
        /// </summary>
        public static decimal Similaridade(string? a, string? b)
        {
            static List<string> Palavras(string? t)
            {
                if (string.IsNullOrWhiteSpace(t)) return new();
                var semAcento = new string(t.ToLowerInvariant().Normalize(NormalizationForm.FormD)
                    .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                                 != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray());
                return Regex.Split(semAcento, @"[^a-z0-9]+").Where(w => w.Length > 0).ToList();
            }

            var pa = Palavras(a);
            var pb = Palavras(b);
            if (pa.Count == 0 && pb.Count == 0) return 100;
            if (pa.Count == 0 || pb.Count == 0) return 0;

            var cont = pa.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
            int comuns = 0;
            foreach (var w in pb)
                if (cont.TryGetValue(w, out var n) && n > 0) { comuns++; cont[w] = n - 1; }

            return Math.Round(200m * comuns / (pa.Count + pb.Count), 2);
        }

        /// <summary>Números para saber se a IA está acertando (últimos N dias).</summary>
        public async Task<object> RelatorioAsync(int dias = 30, int codEmp = 0)
        {
            await using var conn = new SqlConnection(_config.GetConnectionString("Connection"));
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
SELECT
    ISNULL(L.Confianca, '-')                                         AS Confianca,
    COUNT(*)                                                         AS Geradas,
    SUM(CASE WHEN L.DataHoraEnvio IS NOT NULL THEN 1 ELSE 0 END)     AS Enviadas,
    SUM(CASE WHEN L.Similaridade >= 80 THEN 1 ELSE 0 END)            AS UsouQuaseIgual,
    SUM(CASE WHEN L.Similaridade >= 40 AND L.Similaridade < 80 THEN 1 ELSE 0 END) AS Editou,
    SUM(CASE WHEN L.Similaridade < 40 THEN 1 ELSE 0 END)             AS Reescreveu,
    AVG(CAST(L.Similaridade AS FLOAT))                               AS SimilaridadeMedia,
    SUM(CASE WHEN L.Feedback = 1 THEN 1 ELSE 0 END)                  AS Positivos,
    SUM(CASE WHEN L.Feedback = -1 THEN 1 ELSE 0 END)                 AS Negativos,
    AVG(CAST(L.DuracaoMs AS FLOAT))                                  AS TempoMedioMs
FROM AiSugestaoLog L
WHERE L.Origem = 'ticket'
  AND L.DataHora >= DATEADD(DAY, -@dias, GETDATE())
  AND (@emp = 0 OR L.CodEmp = @emp)
GROUP BY ISNULL(L.Confianca, '-')", conn);
            cmd.Parameters.AddWithValue("@dias", Math.Max(1, dias));
            cmd.Parameters.AddWithValue("@emp", codEmp);

            var linhas = new List<Dictionary<string, object?>>();
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var d = new Dictionary<string, object?>();
                    for (int i = 0; i < rd.FieldCount; i++)
                        d[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                    linhas.Add(d);
                }
            }

            int Soma(string k) => linhas.Sum(l => l[k] == null ? 0 : Convert.ToInt32(l[k]));
            int geradas = Soma("Geradas"), enviadas = Soma("Enviadas");
            int quaseIgual = Soma("UsouQuaseIgual"), editou = Soma("Editou"), reescreveu = Soma("Reescreveu");
            int pos = Soma("Positivos"), neg = Soma("Negativos");
            static double Pct(int a, int b) => b == 0 ? 0 : Math.Round(100.0 * a / b, 1);

            return new
            {
                dias,
                geradas,
                enviadas,
                // "aproveitamento": das sugestões que viraram mensagem, quantas foram usadas quase iguais ou só ajustadas
                aproveitamentoPct = Pct(quaseIgual + editou, enviadas),
                usouQuaseIgualPct = Pct(quaseIgual, enviadas),
                editouPct = Pct(editou, enviadas),
                reescreveuPct = Pct(reescreveu, enviadas),
                feedbackPositivoPct = Pct(pos, pos + neg),
                feedbacks = pos + neg,
                porConfianca = linhas
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Juiz da avaliação automática: compara a resposta gerada com a solução confirmada

        public async Task<(int Nota, string Motivo)> JulgarRespostaAsync(string pergunta, string solucaoEsperada,
            string respostaGerada, CancellationToken ct = default)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";
            if (string.IsNullOrWhiteSpace(apiKey)) return (-1, "API KEY não configurada.");

            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var body = new
            {
                model,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = @"Você avalia respostas de suporte do sistema Evolut (ERP).
Compare a RESPOSTA GERADA com a SOLUÇÃO CONFIRMADA (o que de fato resolveu o caso).
Nota:
2 = a resposta leva à mesma solução (mesmo caminho/ação), mesmo com outras palavras;
1 = parcialmente certa: direção correta mas incompleta, ou pede informação razoável antes de resolver;
0 = errada, inventada, ou leva a outro caminho.
Responda só JSON: { ""nota"": 0, ""motivo"": ""uma frase curta"" }" },
                    new { role = "user", content =
                        "PERGUNTA DO CLIENTE:\n" + pergunta +
                        "\n\nSOLUÇÃO CONFIRMADA:\n" + solucaoEsperada +
                        "\n\nRESPOSTA GERADA:\n" + respostaGerada }
                }
            };

            using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
            var txt = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return (-1, $"HTTP {(int)resp.StatusCode}");

            try
            {
                using var doc = JsonDocument.Parse(txt);
                var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                using var j = JsonDocument.Parse(conteudo);
                int nota = j.RootElement.TryGetProperty("nota", out var pn) && pn.ValueKind == JsonValueKind.Number ? pn.GetInt32() : -1;
                string motivo = j.RootElement.TryGetProperty("motivo", out var pm) ? (pm.GetString() ?? "") : "";
                return (Math.Clamp(nota, -1, 2), motivo);
            }
            catch
            {
                return (-1, "Resposta do juiz inválida.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Etapa 1: entender a dúvida (reescreve a pergunta + lê os prints)
        // ─────────────────────────────────────────────────────────────────────
        private const string SystemPromptEntender = @"
Você ajuda o suporte da EvolutTech, que atende clientes do sistema Evolut (comercial/ERP, PDV/frente de caixa, fiscal NF-e/NFC-e, financeiro, estoque, etiquetas, balança, impressoras).
Leia a conversa, as mensagens atuais do cliente e as imagens (prints), se houver. NÃO responda o cliente.
Devolva SOMENTE um JSON com dois campos:
{ ""pergunta"": ""..."", ""textoImagens"": ""..."" }

pergunta:
- A dúvida ou o problema ATUAL do cliente, reescrito como UMA pergunta completa e autocontida, em português.
- Inclua o módulo/tela, a ação e a mensagem de erro quando der para identificar pela conversa ou pelo print.
- Se a mensagem atual for continuação (""sim"", ""e no pix?"", ""no caixa"", ""não, na nota de entrada""), junte com o assunto anterior da conversa.
- Ignore saudações e agradecimentos. Se o cliente só cumprimentou, use o assunto do ticket.
- Não invente detalhes que não aparecem na conversa nem nas imagens.

textoImagens:
- O que é relevante nos prints: mensagem de erro exata, código de rejeição, nome da tela/janela, campos ou botões destacados.
- Escreva o texto do erro exatamente como aparece.
- Deixe vazio ("""") se não houver imagem, se ela não for legível ou se não tiver relação com o sistema.";

        private async Task<(string Pergunta, string TextoImagens)> EntenderPerguntaAsync(
            AiPedidoSugestao pedido, string mensagens, List<string> imagens, string model, CancellationToken ct)
        {
            try
            {
                var texto = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(pedido.Assunto))
                    texto.AppendLine("ASSUNTO DO TICKET: " + pedido.Assunto.Trim()).AppendLine();

                texto.AppendLine("CONVERSA (mais antiga primeiro):");
                texto.AppendLine(string.IsNullOrWhiteSpace(pedido.Conversa) ? "(sem histórico)" : pedido.Conversa);
                texto.AppendLine();
                texto.AppendLine("MENSAGENS ATUAIS DO CLIENTE (ainda sem resposta):");
                texto.AppendLine(mensagens.Length == 0 ? "(o cliente enviou só imagem)" : mensagens);
                if (imagens.Count > 0)
                    texto.AppendLine().AppendLine($"O cliente enviou {imagens.Count} imagem(ns), em anexo.");

                var partes = new List<object> { new { type = "text", text = texto.ToString() } };
                foreach (var img in imagens)
                    partes.Add(new { type = "image_url", image_url = new { url = img, detail = "high" } });

                var body = new
                {
                    model,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPromptEntender },
                        new { role = "user", content = partes }
                    }
                };

                var http = _httpFactory.CreateClient("openai");
                using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
                };

                using var resp = await http.SendAsync(req, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log($"[ENTENDER] HTTP {(int)resp.StatusCode}: {Cut(raw, 500)}");
                    return ("", "");
                }

                using var doc = JsonDocument.Parse(raw);
                var conteudo = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                using var j = JsonDocument.Parse(conteudo);
                string Ler(string nome) =>
                    j.RootElement.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
                        ? (v.GetString() ?? "").Trim()
                        : "";

                return (Ler("pergunta"), Ler("textoImagens"));
            }
            catch (Exception ex)
            {
                LogHelper.Log("[ENTENDER] Erro: " + ex.Message);
                return ("", "");
            }
        }

        /// <summary>
        /// Lê do banco as imagens que o cliente mandou (TicketChamadoD.Imagem), no máximo 2,
        /// reduz para até 1600px e devolve como data URL para a IA. PDF/documento é ignorado.
        /// </summary>
        private async Task<List<string>> CarregarImagensAsync(int codTicket, List<int>? codigos, CancellationToken ct)
        {
            var lista = new List<string>();
            if (codTicket <= 0 || codigos == null || codigos.Count == 0) return lista;

            try
            {
                await using var conn = new SqlConnection(_config.GetConnectionString("Connection"));
                await conn.OpenAsync(ct);

                foreach (var cod in codigos.Distinct().TakeLast(2))
                {
                    await using var cmd = new SqlCommand(@"
                        SELECT Imagem FROM TicketChamadoD
                        WHERE Codigo = @Cod AND CodTicketChamadoC = @Ticket AND Imagem IS NOT NULL", conn);
                    cmd.Parameters.AddWithValue("@Cod", cod);
                    cmd.Parameters.AddWithValue("@Ticket", codTicket);

                    if (await cmd.ExecuteScalarAsync(ct) is not byte[] bytes || bytes.Length == 0)
                        continue;

                    // não é imagem (PDF, planilha...) → null, segue sem ela
                    var dataUrl = await EvolutCRM.Helpers.ImagemIA.ParaDataUrlAsync(bytes, 1600, ct);
                    if (dataUrl != null) lista.Add(dataUrl);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log("[IMAGENS] Erro ao carregar: " + ex.Message);
            }

            return lista;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Prompt da resposta final (o mesmo de antes, agora numa constante)
        // ─────────────────────────────────────────────────────────────────────
        private const string SystemPromptResposta = @"
Você é um atendente humano da EvolutTech, dando suporte no chat para clientes do sistema Evolut (ERP/CRM). Fale como uma pessoa real digitando no WhatsApp: educado, calmo, direto. Nunca diga que é IA, robô, assistente, nem que usa documentação, base, contexto ou insights.

COMO RESPONDER (regra principal, sempre nesta ordem)
1. RESOLVA primeiro: já na primeira frase dê o caminho/área prático, com base no CONTEXTO.
2. Se o assunto tiver mais de um caminho possível, liste de 2 a 4 opções que aparecem no CONTEXTO e faça UMA pergunta curta no fim, de forma leve.
3. Se a pergunta for específica e o CONTEXTO tiver a resposta, responda direto, sem pergunta.
Nunca comece com pergunta. Sempre entregue algo útil antes.

PERGUNTAS (não encher o cliente)
- No máximo UMA pergunta por resposta, e sempre depois de já ter dado a solução.
- Se na sua mensagem anterior você já perguntou algo, NÃO pergunte de novo: assuma o que o cliente respondeu e dê a resposta específica.
- Prefira cobrir as variações dentro da própria resposta a perguntar. Ex.: ""é assim; se for o caso X, então Y"".
- Só faça pergunta de orientação quando o cliente não deu nada pra trabalhar (ex.: ""não funciona""). Nesse caso, pergunte dando direções: ""você tá tentando emitir nota, ver um relatório, mexer no caixa?"".

NÍVEL DE CONTEXTO (vem marcado no início do contexto)
- NÍVEL: ALTO -> você TEM a informação. Responda com segurança e seja específico (caminho de tela, tecla, código). Não use ""acho que"", ""normalmente"", ""geralmente"".
- NÍVEL: MEDIO -> há base relacionada, mas pode não cobrir tudo. Use o que se aplica ao caso, seja específico só no que está no contexto e não complete o resto com suposição.
- NÍVEL: BAIXO -> não há base suficiente. Não invente. Se houver uma direção provável, dê ela e diga de forma humana que vai confirmar certinho; ou faça uma pergunta de orientação.

NÃO INVENTAR
- Só cite menus, telas, botões, opções, caminhos, teclas ou códigos que aparecem no CONTEXTO.
- Nunca afirme que algo ""não existe"" no sistema.
- Se a informação não está no contexto, não complete com suposição.

TOM E TAMANHO
- Curto: 2 a 5 linhas na maioria das vezes. Passo a passo só se o cliente pedir ou o caso exigir, e com poucos passos.
- Sem markdown pesado, sem títulos, sem listas numeradas longas. Use quebra de linha.
- Vá direto, sem ""Claro!"", ""Com certeza!"", ""Segue abaixo"", ""Aqui está"".
- NÃO termine a resposta com pergunta quando a resposta já está completa. Se você deu o caminho e a explicação, pare ali.
- Nunca use ""Quer que eu te explique..."", ""Quer que eu te ajude..."", ""Precisa de mais alguma coisa?"" ou variações. O cliente pergunta se precisar.
- Pergunta no final só quando o assunto realmente tem variações e você precisa saber qual caminho seguir (ex.: ""qual desses relatórios você precisa?""). Fora isso, encerre com a informação.

Prioridade final: 1) correto  2) humano  3) curto  4) útil.
";

        private static bool IsContinuationMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var msg = message.Trim().ToLowerInvariant();

            if (msg.StartsWith("e "))
                return true;
            if (msg.StartsWith("mas "))
                return true;
            if (msg.StartsWith("agora "))
                return true;
            if (msg.StartsWith("certo"))
                return true;
            if (msg.StartsWith("então"))
                return true;
            if (msg.StartsWith("ta "))
                return true;
            if (msg.StartsWith("eu quis dizer"))
                return true;
            if (msg.StartsWith("quis dizer"))
                return true;
            if (msg.StartsWith("na verdade"))
                return true;
            if (msg.StartsWith("não,"))
                return true;
            if (msg.StartsWith("não "))
                return true;
            if (msg.StartsWith("no pdv"))
                return true;
            if (msg.StartsWith("no caixa"))
                return true;

            string[] shortContinuations =
            {
        "sim", "quero", "pode", "pode mostrar", "mostra", "me mostra",
        "isso", "ok", "okay", "entendi", "não entendi",
        "onde", "onde fica", "como", "como faço", "qual caminho",
        "e no pix", "e no cartão", "e no dinheiro",
        "gostaria", "sim, gostaria", "no pdv", "no pdv mesmo", "no caixa"
    };

            if (shortContinuations.Contains(msg))
                return true;

            return msg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
        }

        private static string BuildEffectiveUserQuery(string userMessage, string? conversationContext)
        {
            var current = userMessage?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(current))
                return "";

            if (string.IsNullOrWhiteSpace(conversationContext))
                return current;

            // As linhas vêm como "[22/10 10:15] Cliente: ..." — tira a data da frente para reconhecer quem falou
            var lines = conversationContext
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => Regex.Replace(l, @"^\[[^\]]*\]\s*", ""))
                .ToList();

            var lastClientMessages = lines
                .Where(l => l.StartsWith("Cliente:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Replace("Cliente:", "", StringComparison.OrdinalIgnoreCase).Trim())
                .ToList();

            var lastAssistantMessages = lines
                .Where(l => l.StartsWith("Atendente:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Replace("Atendente:", "", StringComparison.OrdinalIgnoreCase).Trim())
                .ToList();

            var previousClient = lastClientMessages.Count >= 2 ? lastClientMessages[^2] : "";
            var lastAssistant = lastAssistantMessages.LastOrDefault() ?? "";

            if (!IsContinuationMessage(current))
                return current;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(previousClient))
                parts.Add("Assunto anterior do cliente: " + previousClient);

            if (!string.IsNullOrWhiteSpace(lastAssistant))
                parts.Add("Última resposta do atendente: " + lastAssistant);

            parts.Add("Correção ou continuação atual do cliente: " + current);

            return string.Join(" | ", parts);
        }

        private static void LogDocs(string origem, IEnumerable<(int Id, int CodEmp, string Slug, string Title, string Content)> docs)
        {
            var lista = docs?.ToList() ?? new();

            LogHelper.Log($"[DOCS][{origem}] QUANTIDADE: {lista.Count}");
            System.Diagnostics.Debug.WriteLine($"[DOCS][{origem}] QUANTIDADE: {lista.Count}");

            var i = 1;

            foreach (var d in lista)
            {
                var clean = StripHtml(d.Content);
                clean = Cut(clean, 500);

                var linha =
                    $"[DOCS][{origem}] #{i} | SLUG={d.Slug} | TÍTULO={d.Title} | CONTEÚDO={clean}";

                LogHelper.Log(linha);
                System.Diagnostics.Debug.WriteLine(linha);

                i++;
            }
        }
    }
}