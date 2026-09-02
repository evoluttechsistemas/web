using EvolutCRM.Data; // ✅ repo
using EvolutCRM.Services;
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

        public async Task<string> AskAsync(string userMessage, string? conversationContext = null, CancellationToken ct = default)
        {
            try
            {

                var effectiveUserQuery = BuildEffectiveUserQuery(userMessage, conversationContext);

                LogHelper.Log("===== NOVA REQUISIÇÃO IA =====");
                LogHelper.Log("MENSAGEM ORIGINAL DO CLIENTE:");
                LogHelper.Log(userMessage ?? "NULL");
                LogHelper.Log("HISTÓRICO RECEBIDO:");
                LogHelper.Log(conversationContext ?? "NULL");
                LogHelper.Log("PERGUNTA EFETIVA:");
                LogHelper.Log(effectiveUserQuery);
                LogHelper.Log("================================");

                System.Diagnostics.Debug.WriteLine("===== PERGUNTA EFETIVA =====");
                System.Diagnostics.Debug.WriteLine(effectiveUserQuery);
                System.Diagnostics.Debug.WriteLine("============================");

                System.Diagnostics.Debug.WriteLine("===== HISTÓRICO RECEBIDO =====");
                System.Diagnostics.Debug.WriteLine(conversationContext ?? "NULL");
                System.Diagnostics.Debug.WriteLine("================================");

                System.Diagnostics.Debug.WriteLine("===== MENSAGEM ORIGINAL DO CLIENTE =====");
                System.Diagnostics.Debug.WriteLine(userMessage);
                System.Diagnostics.Debug.WriteLine("=========================================");

                var apiKey = _config["OpenAI:ApiKey"];
                var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";
                LogHelper.Log("MODELO USADO: " + model);

                if (string.IsNullOrWhiteSpace(apiKey))
                    return "API KEY não configurada.";

                if (string.IsNullOrWhiteSpace(userMessage))
                    return "Pergunta vazia.";

                // 1) Buscar por embeddings
                float[] qVec;
                try { qVec = await _openAiClient.GetEmbeddingAsync(effectiveUserQuery); }
                catch { qVec = Array.Empty<float>(); }

                var docHits = qVec.Length > 0 ? _index.SearchDocs(qVec, effectiveUserQuery, 6) : new();
                var insHits = qVec.Length > 0 ? _index.SearchInsights(qVec, effectiveUserQuery, 8) : new();

                LogHelper.Log($"[BUSCA] Docs encontrados: {docHits.Count} | Insights encontrados: {insHits.Count}");
                foreach (var d in docHits.Take(3))
                    LogHelper.Log($"[DOCS] {d.Score:F2} {d.Item.Titulo}");
                foreach (var i in insHits.Take(3))
                    LogHelper.Log($"[INSIGHTS] {i.Score:F2} {i.Item.Resumo}");

                // 2) Filtrar por relevância
                const float MIN_RELEVANTE = 0.30f;
                const float NIVEL_ALTO = 0.38f;

                var docsBons = docHits.Where(h => h.Score >= MIN_RELEVANTE).ToList();
                var insBons = insHits.Where(h => h.Score >= MIN_RELEVANTE).ToList();

                float melhor = Math.Max(
                    docsBons.Count > 0 ? docsBons[0].Score : 0,
                    insBons.Count > 0 ? insBons[0].Score : 0);

                string nivel = melhor >= NIVEL_ALTO ? "ALTO" : "BAIXO";

                // 3) Montar contexto
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
                        foreach (var d in docsBons.Take(5))
                        {
                            ctx.AppendLine("-----");
                            ctx.AppendLine($"TÍTULO: {d.Item.Titulo}");
                            ctx.AppendLine($"CONTEÚDO: {Cut(d.Item.Texto, 1200)}");
                        }
                        ctx.AppendLine();
                    }
                    if (insBons.Count > 0)
                    {
                        ctx.AppendLine("CASOS ANTERIORES (referência de solução):");
                        foreach (var it in insBons.Take(6))
                        {
                            ctx.AppendLine("-----");
                            ctx.AppendLine($"PERGUNTA: {Cut(it.Item.Resumo ?? "", 300)}");
                            ctx.AppendLine($"SOLUÇÃO: {Cut(it.Item.Solucao ?? "", 600)}");
                        }
                    }
                }

                LogHelper.Log($"[CONTEXTO] Nível={nivel} | DocsBons={docsBons.Count} | InsBons={insBons.Count}");
                LogHelper.Log(ctx.ToString());


                // 4) Chamar OpenAI
                var http = _httpFactory.CreateClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var systemPrompt = @"
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

                // NOVO
                var body = new
                {
                    model = model,
                    messages = new object[]
                    {
        new { role = "system", content = systemPrompt },
        new { role = "system", content = "CONTEXTO RECUPERADO (use só o que está aqui):\n\n" + ctx.ToString() },
        new { role = "user", content =
            (string.IsNullOrWhiteSpace(conversationContext) ? "" : "Conversa até agora:\n" + conversationContext + "\n\n")
            + "Mensagem atual do cliente:\n" + userMessage }
                    },
                    temperature = 0.2
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
                    return $"Erro OpenAI HTTP {(int)resp.StatusCode}: {respText}";
                }

                using var doc = JsonDocument.Parse(respText);

                var reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                reply = (reply ?? "").Trim();
                LogHelper.Log("[OPENAI] RESPOSTA FINAL:");
                LogHelper.Log(reply);

                if (string.IsNullOrWhiteSpace(reply))
                    return "Não consegui gerar uma resposta. Informe o módulo/tela e a mensagem de erro (se houver).";

                var debugInfo = $"[DEBUG] DocsBons={docsBons.Count} | InsBons={insBons.Count} | Nivel={nivel}";
                System.Diagnostics.Debug.WriteLine(debugInfo);
                LogHelper.Log(debugInfo);
                LogHelper.Log("===== FIM DA REQUISIÇÃO IA =====");
                return reply;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AI] Erro COMPLETO: " + ex);
                LogHelper.Log("[AI] ERRO COMPLETO: " + ex);
                return "ERRO IA: " + ex.Message;
            }
        }

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

            var lines = conversationContext
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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