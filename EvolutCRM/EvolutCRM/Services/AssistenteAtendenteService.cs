using EvolutCRM.Data;
using EvolutCRM.Helpers;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvolutCRM.Services;

/// <summary>
/// Assistente IA do ATENDENTE (chat dentro do ticket).
/// Diferente da IA do cliente (AiService.SugerirAsync), aqui a IA é "liberada":
///   1) procura primeiro nas documentações e nos casos resolvidos (insights);
///   2) se não achar, avisa e ajuda com conhecimento geral + pesquisa na web;
///   3) lê a conversa do ticket e os prints colados.
/// Usa a Responses API da OpenAI (streaming, ferramenta web_search e função de busca interna).
///
/// appsettings (todos opcionais):
///   OpenAI:ModeloAssistente   modelo padrão do chat do atendente (padrão: gpt-4.1-mini)
///   IA:AssistenteModelos      níveis que o atendente escolhe no painel (Baixo / Médio / Alto), cada um com seu modelo:
///       [ { "Nome": "Baixo", "Id": "gpt-4.1-mini", "Descricao": "..." },
///         { "Nome": "Alto",  "Id": "gpt-5", "Raciocinio": true, "Esforco": "medium", "Descricao": "..." } ]
///       A ordem da lista é a ordem na tela; o primeiro é o padrão. Sem a lista, usa ModelosPadrao.
///   IA:AssistenteWeb          "false" desliga a pesquisa na web
///   IA:AssistenteMinScore     corte da busca interna (padrão 0.30)
/// </summary>
public sealed class AssistenteAtendenteService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiClient _openAi;
    private readonly EmbeddingIndex _index;
    private readonly AiTicketInsightsRepository _insights;
    private readonly EmbeddingIndexerService _indexer;
    private readonly TreinamentoClienteService _treinos;
    private readonly AtendimentoIaClient _atendimentoIa;

    // Ferramenta de busca na web que a conta aceita: "web_search" (atual) ou "web_search_preview" (antiga).
    // Descoberta na primeira chamada e guardada para as próximas. "" = conta/modelo sem pesquisa na web.
    // Guardado por modelo: cada modelo pode aceitar uma ferramenta diferente.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _ferramentaWeb = new();

    private static readonly AssistenteModeloOpcao[] ModelosPadrao =
    {
        new() { Nome = "Baixo", Id = "gpt-4.1-mini", Descricao = "Rápido e barato. Bom para o dia a dia." },
        new() { Nome = "Médio", Id = "gpt-4.1",      Descricao = "Mais preciso em problemas técnicos." },
        new() { Nome = "Alto",  Id = "gpt-5",        Descricao = "Pensa antes de responder. Para erro novo ou difícil.", Raciocinio = true, Esforco = "medium" },
    };

    private const int MaxRodadasFerramenta = 3;
    private const int MaxMensagensChatNoContexto = 12;

    // Mesmo pipeline de busca da sugestão ao cliente (sinônimos + reordenação + versão/data)
    private readonly AiService _ai;
    private readonly DocumentacaoService _docs;

    public AssistenteAtendenteService(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        OpenAiClient openAi,
        EmbeddingIndex index,
        AiTicketInsightsRepository insights,
        EmbeddingIndexerService indexer,
        TreinamentoClienteService treinos,
        AiService ai,
        DocumentacaoService docs,
        AtendimentoIaClient atendimentoIa)
    {
        _atendimentoIa = atendimentoIa;
        _ai = ai;
        _docs = docs;
        _treinos = treinos;
        _config = config;
        _httpFactory = httpFactory;
        _openAi = openAi;
        _index = index;
        _insights = insights;
        _indexer = indexer;
    }

    private string ConnStr => _config.GetConnectionString("Connection")!;
    private string Modelo => string.IsNullOrWhiteSpace(_config["OpenAI:ModeloAssistente"])
        ? "gpt-4.1-mini" : _config["OpenAI:ModeloAssistente"]!;

    /// <summary>Níveis que aparecem no seletor do painel, na ordem da configuração. O primeiro é o padrão.</summary>
    public IReadOnlyList<AssistenteModeloOpcao> ListarModelos()
    {
        var lista = _config.GetSection("IA:AssistenteModelos").GetChildren()
            .Select(s => new AssistenteModeloOpcao
            {
                Id = (s["Id"] ?? "").Trim(),
                Nome = string.IsNullOrWhiteSpace(s["Nome"]) ? (s["Id"] ?? "").Trim() : s["Nome"]!.Trim(),
                Descricao = s["Descricao"] ?? "",
                Raciocinio = s["Raciocinio"] != null
                    ? string.Equals(s["Raciocinio"], "true", StringComparison.OrdinalIgnoreCase)
                    : PareceRaciocinio(s["Id"]),
                Esforco = string.IsNullOrWhiteSpace(s["Esforco"]) ? null : s["Esforco"]!.Trim()
            })
            .Where(m => m.Id.Length > 0)
            // o nível é a chave (dois níveis podem usar o mesmo modelo com esforço diferente)
            .GroupBy(m => m.Nome, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .ToList();

        if (lista.Count == 0)
        {
            lista = ModelosPadrao.Select(m => new AssistenteModeloOpcao
            {
                Id = m.Id, Nome = m.Nome, Descricao = m.Descricao, Raciocinio = m.Raciocinio, Esforco = m.Esforco
            }).ToList();
            // OpenAI:ModeloAssistente troca o modelo do nível Baixo
            if (!string.IsNullOrWhiteSpace(_config["OpenAI:ModeloAssistente"])) lista[0].Id = Modelo;
        }
        return lista;
    }

    private static bool PareceRaciocinio(string? id)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        return id.StartsWith("gpt-5") || Regex.IsMatch(id, @"^o\d");
    }

    /// <summary>Só aceita nível da lista (a tela não escolhe qualquer modelo). Aceita o nome do nível ou o id do modelo.</summary>
    private AssistenteModeloOpcao ResolverModelo(string? chave)
    {
        var lista = ListarModelos();
        return lista.FirstOrDefault(m => string.Equals(m.Nome, chave, StringComparison.OrdinalIgnoreCase))
            ?? lista.FirstOrDefault(m => string.Equals(m.Id, chave, StringComparison.OrdinalIgnoreCase))
            ?? lista[0];
    }

    private static string? NormalizarEsforco(string? e) => (e ?? "").Trim().ToLowerInvariant() switch
    {
        "low" or "baixo" => "low",
        "high" or "alto" => "high",
        "medium" or "medio" or "médio" => "medium",
        _ => "medium"
    };
    private bool WebLigada => !string.Equals(_config["IA:AssistenteWeb"], "false", StringComparison.OrdinalIgnoreCase);

    // ═════════════════════════════════════════════════════════════════════
    // 1) Pergunta (streaming)
    // ═════════════════════════════════════════════════════════════════════

    /// <param name="onTexto">Chamado a cada pedaço da resposta com o texto acumulado.</param>
    /// <param name="onStatus">"Pesquisando na web...", "Consultando documentações..." etc.</param>
    public async Task<AssistenteResultado> PerguntarAsync(
        AssistenteContextoTicket ticket,
        AssistentePergunta pergunta,
        IReadOnlyList<AssistenteMensagem> chatAnterior,
        Func<string, Task> onTexto,
        Func<string, Task> onStatus,
        CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();
        var opcao = ResolverModelo(pergunta.Modelo);
        var esforco = opcao.Raciocinio
            ? NormalizarEsforco(string.IsNullOrWhiteSpace(opcao.Esforco) ? pergunta.Esforco : opcao.Esforco)
            : null;
        var res = new AssistenteResultado { Modelo = esforco == null ? opcao.Id : $"{opcao.Id} ({esforco})" };

        // Ticket com o AtendimentoIA configurado: a resposta vem de lá (docs, insights aprovados, parâmetros e
        // código-fonte do ERP ficam centralizados nele), inclusive quando a pergunta tem print.
        if (UsaAtendimentoIa(ticket))
            return await PerguntarAtendimentoIaAsync(ticket, pergunta, onTexto, onStatus, ct);

        if (string.IsNullOrWhiteSpace(_config["OpenAI:ApiKey"]))
        {
            res.Erro = "A chave da OpenAI não está configurada (OpenAI:ApiKey).";
            return res;
        }

        var texto = (pergunta.Texto ?? "").Trim();
        if (texto.Length == 0 && pergunta.Imagens.Count == 0)
        {
            res.Erro = "Digite uma pergunta ou cole um print.";
            return res;
        }
        if (texto.Length == 0) texto = "O que aparece nesta imagem e como resolvo?";

        try
        {
            // ── Imagens: as do ticket vêm do banco; as coladas já chegam reduzidas
            await onStatus("Preparando...");
            var imagens = new List<(AssistenteImagem Img, string DataUrl)>();
            foreach (var img in pergunta.Imagens.Take(4))
            {
                string? url = img.Origem == "ticket"
                    ? await CarregarImagemDoTicketAsync(ticket.CodTicket, img.CodTicketChamadoD, 1600, ct)
                    : img.DataUrl;
                if (!string.IsNullOrWhiteSpace(url)) imagens.Add((img, url));
            }

            res.CodMensagemAtendente = await GravarMensagemAsync(ticket, "atendente", texto, null, false, null,
                imagens.Select(i => (i.Img.Origem, i.Img.CodTicketChamadoD, i.DataUrl)).ToList());

            // ── Busca interna automática (documentações + casos resolvidos)
            await onStatus("Procurando nas documentações e nos casos anteriores...");
            var fontes = new Dictionary<string, AssistenteFonte>(StringComparer.OrdinalIgnoreCase);
            // "e isso aqui?", "como resolvo?": vira uma consulta completa usando o assunto e o fim da conversa
            await onStatus("Entendendo a pergunta...");
            var consultaInicial = await _ai.ReescreverParaBuscaAsync(texto, MontarContextoReescrita(ticket, chatAnterior), ct);
            if (string.Equals(consultaInicial, texto, StringComparison.Ordinal))
                consultaInicial = MontarConsultaBusca(texto, ticket);   // não reescreveu: usa a regra antiga
            LogHelper.Log($"[ASSISTENTE] Busca: {Cortar(consultaInicial, 300)}");
            var baseInterna = await BuscarBaseInternaAsync(consultaInicial, ticket, fontes, 4, 4, ct);
            res.DocsEncontrados = fontes.Values.Count(f => f.Tipo == "doc");
            res.CasosEncontrados = fontes.Values.Count(f => f.Tipo == "caso");

            // ── Monta a entrada
            // O que o cliente já aprendeu: a IA avisa quando a dúvida é sobre algo já ensinado
            if (ticket.Origem == "ticket" && ticket.CodCliente > 0)
            {
                var treinos = await _treinos.ResumoParaIAAsync(ticket.CodEmp, ticket.CodCliente);
                if (treinos.Length > 0)
                    ticket.Detalhes = (string.IsNullOrWhiteSpace(ticket.Detalhes) ? "" : ticket.Detalhes + "\n") +
                        "TREINAMENTOS QUE O CLIENTE JÁ RECEBEU (se a dúvida for sobre um destes, avise o atendente com a data " +
                        "e sugira reenviar o tutorial pelo botão Treinamentos do cliente):\n" + treinos;
            }

            var instrucoes = SystemPrompt + AdendoModo(ticket);
            var entrada = new List<object>
            {
                new { role = "developer", content = MontarTextoContexto(ticket, baseInterna) }
            };

            foreach (var m in chatAnterior
                         .Where(m => !m.Gerando && !m.Erro && !string.IsNullOrWhiteSpace(m.Texto))
                         .TakeLast(MaxMensagensChatNoContexto))
            {
                var t = Cortar(m.Texto, 4000);
                if (!m.EhIA && m.Miniaturas.Count > 0) t += $"\n[o atendente anexou {m.Miniaturas.Count} imagem(ns) nesta pergunta]";
                entrada.Add(new { role = m.EhIA ? "assistant" : "user", content = t });
            }

            var partes = new List<object> { new { type = "input_text", text = texto } };
            foreach (var (_, url) in imagens)
                partes.Add(new { type = "input_image", image_url = url, detail = "high" });
            entrada.Add(new { role = "user", content = partes });

            // ── Conversa com a OpenAI (a IA pode chamar a busca interna e a web)
            var acumulado = new StringBuilder();
            string? idResposta = null;
            List<object> proximaEntrada = entrada;

            for (int rodada = 0; rodada <= MaxRodadasFerramenta; rodada++)
            {
                var ultimaRodada = rodada == MaxRodadasFerramenta;
                var r = await ChamarStreamingAsync(proximaEntrada, idResposta, ultimaRodada, acumulado,
                    onTexto, onStatus, res, opcao.Id, esforco, instrucoes, ct);

                if (!string.IsNullOrEmpty(r.Erro))
                {
                    res.Erro = r.Erro;
                    break;
                }

                idResposta = r.IdResposta;
                foreach (var w in r.LinksWeb) AdicionarFonteWeb(fontes, w.Url, w.Titulo);

                if (r.Chamadas.Count == 0 || ultimaRodada) break;

                // Executa as buscas internas que a IA pediu
                await onStatus("Consultando documentações e insights...");
                var saidas = new List<object>();
                foreach (var c in r.Chamadas)
                {
                    string saida;
                    if (c.Nome == "buscar_base_interna")
                    {
                        var consulta = LerArgumento(c.Argumentos, "consulta");
                        saida = string.IsNullOrWhiteSpace(consulta)
                            ? "Consulta vazia."
                            : await BuscarBaseInternaAsync(consulta, ticket, fontes, 4, 4, ct);
                    }
                    else saida = "Ferramenta desconhecida.";

                    saidas.Add(new { type = "function_call_output", call_id = c.CallId, output = saida });
                }
                proximaEntrada = saidas;
                res.DocsEncontrados = fontes.Values.Count(f => f.Tipo == "doc");
                res.CasosEncontrados = fontes.Values.Count(f => f.Tipo == "caso");
            }

            var final = acumulado.ToString().Trim();
            if (final.Length == 0 && string.IsNullOrEmpty(res.Erro))
                res.Erro = "A IA não devolveu resposta. Tente perguntar de outro jeito.";

            // Marca as fontes internas que a IA citou ([D1], [C2]) e as da web
            foreach (Match m in Regex.Matches(final, @"\[(?<id>[DC]\d{1,2})\]"))
                if (fontes.TryGetValue(m.Groups["id"].Value, out var f)) f.Citada = true;
            foreach (var f in fontes.Values.Where(f => f.Tipo == "web")) f.Citada = true;

            res.Texto = final;
            res.Ok = final.Length > 0;
            res.Fontes = fontes.Values
                .OrderByDescending(f => f.Citada)
                .ThenBy(f => f.Tipo == "web" ? 2 : f.Tipo == "caso" ? 1 : 0)
                .ToList();
            relogio.Stop();
            res.DuracaoMs = (int)relogio.ElapsedMilliseconds;

            if (res.Ok)
                res.CodMensagemIA = await GravarMensagemAsync(ticket, "ia", final, res.Fontes, res.PesquisouWeb, res, null);

            LogHelper.Log($"[ASSISTENTE] Ticket #{ticket.CodTicket} | {ticket.Usuario} | {res.DuracaoMs}ms | web={res.PesquisouWeb} " +
                          $"| docs={res.DocsEncontrados} casos={res.CasosEncontrados} | tokens {res.TokensEntrada}/{res.TokensSaida}");
            return res;
        }
        catch (OperationCanceledException)
        {
            res.Erro = "Resposta interrompida.";
            res.Texto = "";
            return res;
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] Erro: " + ex);
            res.Erro = "Erro ao falar com a IA: " + ex.Message;
            return res;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1b) Pergunta pelo AtendimentoIA (API externa)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>O painel do ticket usa o AtendimentoIA quando ele está configurado no appsettings.</summary>
    public bool UsaAtendimentoIa(AssistenteContextoTicket ticket) =>
        ticket.Origem == "ticket" && ticket.CodTicket > 0 && _atendimentoIa.Configurado;

    private async Task<AssistenteResultado> PerguntarAtendimentoIaAsync(
        AssistenteContextoTicket ticket, AssistentePergunta pergunta,
        Func<string, Task> onTexto, Func<string, Task> onStatus, CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();
        var res = new AssistenteResultado { Modelo = "AtendimentoIA" };
        var texto = (pergunta.Texto ?? "").Trim();
        if (texto.Length == 0 && pergunta.Imagens.Count == 0)
        {
            res.Erro = "Digite uma pergunta ou cole um print.";
            return res;
        }
        if (texto.Length == 0) texto = "O que aparece nesta imagem e como resolvo?";

        try
        {
            // Prints: os do ticket vêm do banco; os colados já chegam reduzidos. A API aceita até 2 por pergunta.
            var imagens = new List<(AssistenteImagem Img, string DataUrl)>();
            if (pergunta.Imagens.Count > 0) await onStatus("Preparando o print...");
            foreach (var img in pergunta.Imagens.Take(AtendimentoIaClient.MaxImagens))
            {
                string? url = img.Origem == "ticket"
                    ? await CarregarImagemDoTicketAsync(ticket.CodTicket, img.CodTicketChamadoD, 1600, ct)
                    : img.DataUrl;
                if (!string.IsNullOrWhiteSpace(url)) imagens.Add((img, url));
            }

            res.CodMensagemAtendente = await GravarMensagemAsync(ticket, "atendente", texto, null, false, null,
                imagens.Select(i => (i.Img.Origem, i.Img.CodTicketChamadoD, i.DataUrl)).ToList());

            // A API não lê a conversa do ticket: no começo da conversa o HELP manda o assunto e
            // o que o cliente escreveu por último, para "não funciona" virar uma pergunta completa.
            var novaConversa = !_atendimentoIa.TemConversa(ticket.CodTicket, ticket.Usuario);
            var enviada = novaConversa ? MontarPerguntaAtendimentoIa(texto, ticket) : texto;

            await onStatus(imagens.Count > 0
                ? "Lendo o print e consultando o AtendimentoIA (docs, casos, parâmetros e código do ERP)..."
                : "Consultando o AtendimentoIA (docs, casos, parâmetros e código do ERP)...");
            var r = await _atendimentoIa.PerguntarAsync(ticket.CodTicket, ticket.Usuario, enviada, ct,
                imagens.Select(i => i.DataUrl).ToList());
            relogio.Stop();
            res.DuracaoMs = (int)relogio.ElapsedMilliseconds;

            if (!r.Ok)
            {
                res.Erro = r.Erro;
                LogHelper.Log($"[ASSISTENTE] Ticket #{ticket.CodTicket} | {ticket.Usuario} | AtendimentoIA falhou: {r.Erro}");
                return res;
            }

            var final = FormatarRespostaAtendimentoIa(r);
            await onTexto(final);
            res.Texto = final;
            res.Ok = true;
            res.CodMensagemIA = await GravarMensagemAsync(ticket, "ia", final, res.Fontes, false, res, null);

            LogHelper.Log($"[ASSISTENTE] Ticket #{ticket.CodTicket} | {ticket.Usuario} | AtendimentoIA | {res.DuracaoMs}ms " +
                          $"| evidencias={(r.EvidenciasSuficientes ? "ok" : "insuficientes")} | formato={r.Formato}");
            return res;
        }
        catch (OperationCanceledException)
        {
            res.Erro = "Resposta interrompida.";
            res.Texto = "";
            return res;
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] AtendimentoIA erro: " + ex);
            res.Erro = "Erro ao falar com o AtendimentoIA: " + ex.Message;
            return res;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Botão "Gerar resposta IA" (ticket e card) pelo AtendimentoIA
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Quando ligado, os botões "Gerar resposta IA" do ticket e do card usam o AtendimentoIA.</summary>
    public bool AtendimentoIaConfigurado => _atendimentoIa.Configurado;

    /// <summary>Resposta para o cliente do ticket a partir das mensagens pendentes dele (e dos prints que mandou).</summary>
    public async Task<AtendimentoIaResposta> GerarRespostaClienteTicketAsync(int codTicket, string mensagensCliente,
        string? assunto, IReadOnlyList<int> codigosImagens, CancellationToken ct = default)
    {
        var imagens = new List<string>();
        foreach (var codD in codigosImagens.TakeLast(AtendimentoIaClient.MaxImagens))
        {
            var url = await CarregarImagemDoTicketAsync(codTicket, codD, 1600, ct);
            if (!string.IsNullOrWhiteSpace(url)) imagens.Add(url);
        }
        return await _atendimentoIa.GerarRespostaTicketAsync(codTicket, MontarPerguntaBotao(mensagensCliente, assunto), imagens, ct);
    }

    /// <summary>Resposta para o lead do card (modo comercial do AtendimentoIA).</summary>
    public async Task<AtendimentoIaResposta> GerarRespostaLeadCardAsync(int codCard, string mensagensLead,
        IReadOnlyList<byte[]> imagens, CancellationToken ct = default)
    {
        var urls = new List<string>();
        foreach (var bytes in imagens.TakeLast(AtendimentoIaClient.MaxImagens))
        {
            var url = await ImagemIA.ParaDataUrlAsync(bytes, 1600, ct);
            if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
        }
        return await _atendimentoIa.GerarRespostaCardAsync(codCard, MontarPerguntaBotao(mensagensLead, null), urls, ct);
    }

    /// <summary>
    /// Texto que vai para o campo de mensagem. Sem evidência suficiente, usa a pergunta de acompanhamento
    /// (pede o detalhe que falta ao cliente) em vez de mandar "não encontrei" para ele.
    /// </summary>
    public static string TextoParaCliente(AtendimentoIaResposta r)
    {
        var resposta = (r.Resposta ?? "").Trim();
        var acompanhamento = (r.PerguntaDeAcompanhamento ?? "").Trim();
        if (!r.EvidenciasSuficientes && acompanhamento.Length > 0) return acompanhamento;
        if (r.Formato == "fluxo" && r.Passos is { Count: > 1 } passos)
            resposta += "\n\n" + string.Join("\n", passos.Select((p, i) => $"{i + 1}. {p.Trim()}"));
        if (acompanhamento.Length > 0 && !resposta.Contains(acompanhamento, StringComparison.OrdinalIgnoreCase))
            resposta += "\n\n" + acompanhamento;
        return resposta.Trim();
    }

    private static string MontarPerguntaBotao(string mensagens, string? assunto)
    {
        var texto = Cortar((mensagens ?? "").Trim(), 1700);
        if (!string.IsNullOrWhiteSpace(assunto)) texto += "\n(Assunto do chamado: " + Cortar(assunto.Trim(), 200) + ")";
        return Cortar(texto, 2000);
    }

    /// <summary>Pergunta do atendente + assunto e últimas mensagens do cliente (máx. 2.000 caracteres, limite da API).</summary>
    private static string MontarPerguntaAtendimentoIa(string texto, AssistenteContextoTicket t)
    {
        var pergunta = Cortar(texto, 1200);
        var extra = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(t.Assunto)) extra.Append("Assunto do chamado: ").Append(t.Assunto!.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(t.UltimasDoCliente)) extra.Append("Últimas mensagens do cliente: ").Append(t.UltimasDoCliente.Trim());
        if (extra.Length == 0) return pergunta;

        var cabeca = pergunta + "\n\n";
        var sobra = 2000 - cabeca.Length;
        return sobra < 80 ? pergunta : cabeca + Cortar(extra.ToString(), sobra);
    }

    /// <summary>Monta o texto do painel. O que vai para o cliente fica em &lt;resposta_cliente&gt; (botão "Usar na resposta").</summary>
    private static string FormatarRespostaAtendimentoIa(AtendimentoIaResposta r)
    {
        var sb = new StringBuilder();
        if (!r.EvidenciasSuficientes)
            sb.Append("⚠️ **O AtendimentoIA não encontrou evidências suficientes.** Confira antes de enviar ao cliente.\n\n");
        else if (r.BaseadoEmCasoAnterior)
            sb.Append("📌 **Baseado em atendimento anterior resolvido.** Confira se vale para a versão deste cliente.\n\n");

        sb.Append("<resposta_cliente>\n");
        sb.Append((r.Resposta ?? "").Trim());
        var passos = (r.Passos ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (passos.Count > 0)
        {
            sb.Append("\n\n");
            for (int i = 0; i < passos.Count; i++) sb.Append(i + 1).Append(". ").Append(passos[i].Trim()).Append('\n');
        }
        var videos = (r.Videos ?? new()).Where(v => !string.IsNullOrWhiteSpace(v.Url)).Take(3).ToList();
        if (videos.Count > 0)
        {
            sb.Append(videos.Count > 1 ? "\n\nVídeos que podem ajudar:\n" : "\n\nVídeo que pode ajudar:\n");
            foreach (var v in videos) sb.Append("- ").Append((v.Titulo ?? "Vídeo").Trim()).Append(": ").Append(v.Url!.Trim()).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(r.PerguntaDeAcompanhamento))
            sb.Append("\n\n").Append(r.PerguntaDeAcompanhamento!.Trim());
        sb.Append("\n</resposta_cliente>");

        var rodape = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.AvisoDaImagem)) rodape.Add(r.AvisoDaImagem!.Trim());
        if (!string.IsNullOrWhiteSpace(r.VersaoErp)) rodape.Add($"Versão do ERP do cliente: {r.VersaoErp}");
        if (!string.IsNullOrWhiteSpace(r.Segmento)) rodape.Add($"Segmento: {r.Segmento}");
        if (rodape.Count > 0) sb.Append("\n\n_").Append(string.Join(" · ", rodape)).Append('_');
        return sb.ToString();
    }

    private sealed class RodadaResultado
    {
        public string? IdResposta;
        public string? Erro;
        public List<(string CallId, string Nome, string Argumentos)> Chamadas = new();
        public List<(string Url, string Titulo)> LinksWeb = new();
    }

    private async Task<RodadaResultado> ChamarStreamingAsync(
        List<object> entrada, string? idAnterior, bool semFerramentas, StringBuilder acumulado,
        Func<string, Task> onTexto, Func<string, Task> onStatus, AssistenteResultado res,
        string modelo, string? esforco, string instrucoes, CancellationToken ct)
    {
        var rodada = new RodadaResultado();

        // Tenta a ferramenta web atual; se a conta/modelo não aceitar, cai para a antiga e depois para sem web
        var candidatas = !WebLigada || semFerramentas
            ? new[] { "" }
            : _ferramentaWeb.TryGetValue(modelo, out var conhecida)
                ? new[] { conhecida }
                : new[] { "web_search", "web_search_preview", "" };

        HttpResponseMessage? resp = null;
        string ferramentaUsada = "";
        int iWeb = 0;
        while (iWeb < candidatas.Length)
        {
            var web = candidatas[iWeb];
            var body = new Dictionary<string, object?>
            {
                ["model"] = modelo,
                ["instructions"] = instrucoes,
                ["input"] = entrada,
                ["stream"] = true,
                // modelo de raciocínio gasta tokens pensando: precisa de mais espaço
                ["max_output_tokens"] = esforco == null ? 3000 : 16000,
            };
            if (esforco != null) body["reasoning"] = new { effort = esforco };
            if (idAnterior != null) body["previous_response_id"] = idAnterior;

            var tools = new List<object>();
            if (!semFerramentas)
            {
                tools.Add(FerramentaBuscaInterna);
                if (web.Length > 0)
                    tools.Add(new Dictionary<string, object?>
                    {
                        ["type"] = web,
                        ["search_context_size"] = "medium",
                        ["user_location"] = new { type = "approximate", country = "BR" }
                    });
            }
            if (tools.Count > 0) body["tools"] = tools;

            var http = _httpFactory.CreateClient("openai");
            http.Timeout = TimeSpan.FromMinutes(4);
            var req = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (resp.IsSuccessStatusCode)
            {
                ferramentaUsada = web;
                break;
            }

            var erro = await resp.Content.ReadAsStringAsync(ct);
            var status = (int)resp.StatusCode;

            // Modelo não aceita "esforço": tenta de novo sem ele
            if (resp.StatusCode == HttpStatusCode.BadRequest && esforco != null &&
                erro.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
            {
                LogHelper.Log($"[ASSISTENTE] '{modelo}' não aceita reasoning.effort, tentando sem. {Cortar(erro, 300)}");
                resp.Dispose();
                resp = null;
                esforco = null;
                body.Remove("reasoning");
                continue;
            }

            var problemaFerramenta = resp.StatusCode == HttpStatusCode.BadRequest &&
                                     web.Length > 0 &&
                                     (erro.Contains("web_search", StringComparison.OrdinalIgnoreCase) ||
                                      erro.Contains("tool", StringComparison.OrdinalIgnoreCase));
            resp.Dispose();
            resp = null;

            if (!problemaFerramenta)
            {
                LogHelper.Log($"[ASSISTENTE] OpenAI HTTP {status}: {Cortar(erro, 800)}");
                rodada.Erro = "A OpenAI recusou o pedido: " + LerMensagemErro(erro);
                return rodada;
            }
            LogHelper.Log($"[ASSISTENTE] Ferramenta '{web}' não aceita por '{modelo}', tentando a próxima. {Cortar(erro, 300)}");
            iWeb++;
        }

        if (resp == null)
        {
            rodada.Erro = "Não foi possível falar com a OpenAI.";
            return rodada;
        }
        if (!semFerramentas && WebLigada) _ferramentaWeb.TryAdd(modelo, ferramentaUsada);

        using (resp)
        await using (var stream = await resp.Content.ReadAsStreamAsync(ct))
        using (var leitor = new StreamReader(stream, Encoding.UTF8))
        {
            var ultimoEnvio = DateTime.MinValue;
            string? linha;
            while ((linha = await leitor.ReadLineAsync(ct)) != null)
            {
                if (!linha.StartsWith("data:", StringComparison.Ordinal)) continue;
                var dados = linha.AsSpan(5).Trim().ToString();
                if (dados.Length == 0 || dados == "[DONE]") continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(dados); }
                catch { continue; }

                using (doc)
                {
                    var ev = doc.RootElement;
                    var tipo = Str(ev, "type");

                    switch (tipo)
                    {
                        case "response.created":
                            if (ev.TryGetProperty("response", out var rc)) rodada.IdResposta = Str(rc, "id");
                            break;

                        case "response.output_text.delta":
                            acumulado.Append(Str(ev, "delta"));
                            // não manda para a tela a cada letra: no máximo ~12 vezes por segundo
                            if ((DateTime.UtcNow - ultimoEnvio).TotalMilliseconds > 80)
                            {
                                ultimoEnvio = DateTime.UtcNow;
                                await onTexto(acumulado.ToString());
                            }
                            break;

                        case "response.web_search_call.in_progress":
                        case "response.web_search_call.searching":
                            res.PesquisouWeb = true;
                            await onStatus("Pesquisando na web...");
                            break;

                        case "response.web_search_call.completed":
                            await onStatus("Lendo os resultados da pesquisa...");
                            break;

                        case "response.output_item.added":
                            if (ev.TryGetProperty("item", out var ia))
                            {
                                var t = Str(ia, "type");
                                if (t == "function_call") await onStatus("Consultando documentações e insights...");
                                else if (t is "web_search_call") { res.PesquisouWeb = true; await onStatus("Pesquisando na web..."); }
                                else if (t == "message") await onStatus("Escrevendo...");
                                else if (t == "reasoning") await onStatus("Raciocinando...");
                            }
                            break;

                        case "response.output_item.done":
                            if (ev.TryGetProperty("item", out var it))
                            {
                                var t = Str(it, "type");
                                if (t == "function_call")
                                    rodada.Chamadas.Add((Str(it, "call_id"), Str(it, "name"), Str(it, "arguments")));
                                else if (t == "message")
                                    LerLinksWeb(it, rodada.LinksWeb);
                                else if (t == "web_search_call")
                                    res.PesquisouWeb = true;
                            }
                            break;

                        case "response.completed":
                        case "response.incomplete":
                            if (ev.TryGetProperty("response", out var rf))
                            {
                                rodada.IdResposta = Str(rf, "id") is { Length: > 0 } idf ? idf : rodada.IdResposta;
                                if (rf.TryGetProperty("usage", out var u))
                                {
                                    res.TokensEntrada += Int(u, "input_tokens");
                                    res.TokensSaida += Int(u, "output_tokens");
                                }
                                if (tipo == "response.incomplete" && acumulado.Length > 0)
                                    acumulado.Append("\n\n_(resposta cortada por tamanho)_");
                            }
                            break;

                        case "response.failed":
                            rodada.Erro = ev.TryGetProperty("response", out var rx) && rx.TryGetProperty("error", out var ex)
                                ? Str(ex, "message") : "A OpenAI não conseguiu gerar a resposta.";
                            break;

                        case "error":
                            rodada.Erro = Str(ev, "message") is { Length: > 0 } m ? m : "Erro da OpenAI.";
                            break;
                    }
                }
            }
        }

        await onTexto(acumulado.ToString());
        return rodada;
    }

    private static readonly object FerramentaBuscaInterna = new Dictionary<string, object?>
    {
        ["type"] = "function",
        ["name"] = "buscar_base_interna",
        ["description"] = "Busca nas documentações da EvolutTech e nos casos de suporte já resolvidos (insights). " +
                          "Use com termos específicos: código/mensagem de erro, nome da tela, rotina, módulo. " +
                          "Use ANTES de pesquisar na web quando o assunto for do sistema Evolut.",
        ["parameters"] = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["consulta"] = new { type = "string", description = "O que procurar, em português, com os termos técnicos." }
            },
            ["required"] = new[] { "consulta" },
            ["additionalProperties"] = false
        },
        ["strict"] = true
    };

    // ═════════════════════════════════════════════════════════════════════
    // 2) Busca interna (documentações + casos)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Contexto curto para a reescrita da pergunta: assunto, últimas do cliente e o fim do chat com a IA.</summary>
    private static string MontarContextoReescrita(AssistenteContextoTicket t, IReadOnlyList<AssistenteMensagem> chatAnterior)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(t.Assunto)) sb.AppendLine("Assunto: " + Cortar(t.Assunto, 300));
        if (!string.IsNullOrWhiteSpace(t.Versao)) sb.AppendLine("Versão do cliente: " + t.Versao);
        if (!string.IsNullOrWhiteSpace(t.UltimasDoCliente)) sb.AppendLine("Últimas mensagens do cliente:\n" + Cortar(t.UltimasDoCliente, 1200));
        var fim = chatAnterior.Where(m => !m.Gerando && !m.Erro && !string.IsNullOrWhiteSpace(m.Texto)).TakeLast(2).ToList();
        if (fim.Count > 0)
        {
            sb.AppendLine("Conversa recente com a IA:");
            foreach (var m in fim) sb.AppendLine((m.EhIA ? "IA: " : "Atendente: ") + Cortar(m.Texto, 600));
        }
        return sb.ToString();
    }

    private static string MontarConsultaBusca(string pergunta, AssistenteContextoTicket t)
    {
        var sb = new StringBuilder(pergunta);
        if (!string.IsNullOrWhiteSpace(t.Assunto)) sb.Append('\n').Append(t.Assunto);
        if (!string.IsNullOrWhiteSpace(t.UltimasDoCliente)) sb.Append('\n').Append(Cortar(t.UltimasDoCliente, 600));
        return sb.ToString();
    }

    /// <summary>
    /// Busca por embeddings e devolve o texto para a IA, com ids D1/C1 continuando a numeração já usada.
    /// </summary>
    private async Task<string> BuscarBaseInternaAsync(string consulta, AssistenteContextoTicket t,
        Dictionary<string, AssistenteFonte> fontes, int maxDocs, int maxCasos, CancellationToken ct)
    {
        // Mesmo pipeline da sugestão ao cliente: sinônimos, busca híbrida com versão/data,
        // reordenação pela IA (nota 0-3) e corte por relevância. Tira os casos do próprio ticket.
        AiSelecaoBase sel;
        try
        {
            sel = await _ai.SelecionarBaseAsync(consulta, t.CodEmp, t.CodTicket, t.Versao, maxDocs, maxCasos, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] Busca interna falhou: " + ex.Message);
            return "Busca interna indisponível no momento.";
        }
        ct.ThrowIfCancellationRequested();

        var docs = sel.Docs;
        var casos = sel.Casos;

        var sb = new StringBuilder();
        int nD = fontes.Values.Count(f => f.Tipo == "doc");
        int nC = fontes.Values.Count(f => f.Tipo == "caso");

        foreach (var d in docs)
        {
            var ja = fontes.Values.FirstOrDefault(f => f.Tipo == "doc" && f.CodDoc == d.Item.CodDoc && f.Texto == Cortar(d.Item.Texto, 3000));
            var id = ja?.Id ?? "D" + (++nD);
            if (ja == null)
                fontes[id] = new AssistenteFonte
                {
                    Id = id, Tipo = "doc", Titulo = d.Item.Titulo, CodDoc = d.Item.CodDoc, Slug = d.Item.Slug,
                    // pré-visualização para a equipe: abre públicas e internas (o link público não mostra as internas)
                    Url = d.Item.CodDoc > 0 ? _docs.UrlParaEquipe(d.Item.CodDoc, t.CodEmp, d.Item.Slug) : "",
                    Score = d.Score, Texto = Cortar(d.Item.Texto, 3000)
                };
            sb.AppendLine("-----");
            sb.AppendLine($"[{id}] DOCUMENTAÇÃO: {d.Item.Titulo}");
            sb.AppendLine(Cortar(d.Item.Texto, 1800));
        }

        foreach (var c in casos)
        {
            var ja = fontes.Values.FirstOrDefault(f => f.Tipo == "caso" && f.CodTicket == c.Item.TicketId && f.Titulo == Cortar(c.Item.Resumo ?? "", 140));
            var id = ja?.Id ?? "C" + (++nC);
            if (ja == null)
                fontes[id] = new AssistenteFonte
                {
                    Id = id, Tipo = "caso", Titulo = Cortar(c.Item.Resumo ?? "", 140), CodTicket = c.Item.TicketId,
                    Url = "/ticket/" + c.Item.TicketId, Score = c.Score,
                    Texto = "Caso: " + (c.Item.Resumo ?? "") + "\n\nSolução: " + (c.Item.Solucao ?? "")
                };
            sb.AppendLine("-----");
            sb.AppendLine($"[{id}] CASO RESOLVIDO (ticket #{c.Item.TicketId}, {AiService.DescreverOrigemCaso(c.Item)}): {Cortar(c.Item.Resumo ?? "", 400)}");
            sb.AppendLine("SOLUÇÃO APLICADA: " + Cortar(c.Item.Solucao ?? "", 900));
        }

        return sb.Length == 0
            ? $"Nada relevante nas documentações nem nos casos anteriores para: \"{Cortar(consulta, 200)}\"."
            : sb.ToString();
    }

    private static void AdicionarFonteWeb(Dictionary<string, AssistenteFonte> fontes, string url, string titulo)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        // tira o rastreio que a OpenAI acrescenta
        url = Regex.Replace(url, @"[?&]utm_source=openai$", "");
        if (fontes.Values.Any(f => f.Tipo == "web" && f.Url == url)) return;
        var n = fontes.Values.Count(f => f.Tipo == "web") + 1;
        string host = "";
        try { host = new Uri(url).Host.Replace("www.", ""); } catch { }
        fontes["W" + n] = new AssistenteFonte
        {
            Id = "W" + n, Tipo = "web", Url = url,
            Titulo = string.IsNullOrWhiteSpace(titulo) ? host : titulo.Trim(),
            Texto = host
        };
    }

    private static void LerLinksWeb(JsonElement item, List<(string Url, string Titulo)> destino)
    {
        if (!item.TryGetProperty("content", out var conteudo) || conteudo.ValueKind != JsonValueKind.Array) return;
        foreach (var parte in conteudo.EnumerateArray())
        {
            if (!parte.TryGetProperty("annotations", out var anot) || anot.ValueKind != JsonValueKind.Array) continue;
            foreach (var a in anot.EnumerateArray())
                if (Str(a, "type") == "url_citation")
                    destino.Add((Str(a, "url"), Str(a, "title")));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3) Contexto do ticket
    // ═════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> UsuariosAutomaticos = new(StringComparer.OrdinalIgnoreCase)
        { "WHATSAPP", "CLIENTE", "IA", "EVOLUTTECH", "BOT", "SISTEMA", "NOVO" };

    /// <summary>
    /// Monta o contexto a partir do histórico que a tela do ticket já tem carregado.
    /// Tickets longos: as 3 primeiras mensagens (onde o problema costuma ser descrito) + as 45 últimas.
    /// </summary>
    public static AssistenteContextoTicket MontarContexto(int codTicket, int codEmp, string usuario,
        TicketChamadoCModel? ticket, IEnumerable<TicketChamadoDModel>? historico, string? situacao = null)
    {
        var validas = (historico ?? Enumerable.Empty<TicketChamadoDModel>())
            .Where(x => x.MensagemExcluida != "S" &&
                        x.StatusEnvioWhatsApp is not ("ia_processing" or "saudacao_auto" or "aguarde_auto"))
            .OrderBy(x => x.DataHora).ThenBy(x => x.Codigo)
            .ToList();

        List<TicketChamadoDModel> escolhidas;
        int omitidas = 0;
        if (validas.Count > 48)
        {
            escolhidas = validas.Take(3).Concat(validas.TakeLast(45)).ToList();
            omitidas = validas.Count - 48;
        }
        else escolhidas = validas;

        var sb = new StringBuilder();
        for (int i = 0; i < escolhidas.Count; i++)
        {
            if (omitidas > 0 && i == 3)
                sb.AppendLine($"[... {omitidas} mensagens do meio da conversa omitidas ...]");

            var x = escolhidas[i];
            var usuarioMsg = (x.Usuario ?? "").Trim();
            var autor = x.EnvioCliente == "S" ? "Cliente"
                      : x.Interno == "S" ? $"Nota interna ({usuarioMsg})"
                      : UsuariosAutomaticos.Contains(usuarioMsg) ? "Mensagem automática"
                      : $"Atendente ({usuarioMsg})";

            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(x.Anotacao)) partes.Add(Cortar(x.Anotacao.Trim(), 900));
            if (!string.IsNullOrWhiteSpace(x.TranscricaoAudio)) partes.Add("[áudio transcrito] " + Cortar(x.TranscricaoAudio.Trim(), 900));
            else if (!string.IsNullOrWhiteSpace(x.AudioUrl)) partes.Add("[áudio sem transcrição]");
            if (EhImagemDoTicket(x)) partes.Add($"[imagem anexada, mensagem {x.Codigo}]");
            else if (!string.IsNullOrWhiteSpace(x.CaminhoImagem)) partes.Add($"[arquivo anexado: {x.NomeImagem}]");
            if (!string.IsNullOrWhiteSpace(x.VideoUrl)) partes.Add("[vídeo anexado]");
            if (partes.Count == 0) continue;

            sb.AppendLine($"[{x.DataHora:dd/MM HH:mm}] {autor}: {string.Join(" ", partes)}");
        }

        var ultimasCliente = string.Join("\n", validas
            .Where(x => x.EnvioCliente == "S")
            .Select(x => !string.IsNullOrWhiteSpace(x.Anotacao) ? x.Anotacao.Trim() : (x.TranscricaoAudio ?? "").Trim())
            .Where(s => s.Length > 0)
            .TakeLast(4));

        return new AssistenteContextoTicket
        {
            Origem = "ticket",
            Modo = "suporte",
            CodTicket = codTicket,
            CodCliente = ticket?.CodCliente ?? 0,
            CodEmp = codEmp,
            Usuario = usuario ?? "",
            Assunto = ticket?.Assunto,
            Cliente = !string.IsNullOrWhiteSpace(ticket?.ApelidoCliente) ? ticket!.ApelidoCliente : ticket?.NomeCliente,
            Versao = ticket?.Versao,
            Situacao = situacao,
            Conversa = sb.ToString(),
            UltimasDoCliente = ultimasCliente,
            TotalMensagens = validas.Count
        };
    }

    /// <summary>Imagem do ticket que a IA consegue ler (servida por /api/tickets/anexo/, com extensão de imagem).</summary>
    public static bool EhImagemDoTicket(TicketChamadoDModel x)
    {
        if (string.IsNullOrWhiteSpace(x.CaminhoImagem) ||
            !x.CaminhoImagem.StartsWith("/api/tickets/anexo/", StringComparison.OrdinalIgnoreCase))
            return false;
        return ImagemIA.PareceImagem(x.NomeImagem);
    }

    private static readonly Dictionary<string, string> NomesFunil = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RA"] = "Novo Lead", ["EP"] = "Contato Feito", ["FU"] = "Qualificado", ["NO"] = "Nutrição",
        ["PF"] = "Pendente Fechamento", ["IM"] = "Implantação", ["CONCLUIDO"] = "Concluído", ["PERDIDO"] = "Perdido"
    };

    /// <summary>Contexto do card do comercial (Card.razor): dados do lead, produtos e a conversa.</summary>
    public static AssistenteContextoTicket MontarContextoCard(CardModels? card, int codEmp, string usuario)
    {
        var anot = (card?.Anotacoes ?? new List<AnotacaoModel>())
            .Where(x => x.MensagemExcluida != "S")
            .OrderBy(x => x.DataHora).ThenBy(x => x.Codigo)
            .ToList();

        List<AnotacaoModel> escolhidas;
        int omitidas = 0;
        if (anot.Count > 48)
        {
            escolhidas = anot.Take(3).Concat(anot.TakeLast(45)).ToList();
            omitidas = anot.Count - 48;
        }
        else escolhidas = anot;

        var sb = new StringBuilder();
        for (int i = 0; i < escolhidas.Count; i++)
        {
            if (omitidas > 0 && i == 3)
                sb.AppendLine($"[... {omitidas} mensagens do meio da conversa omitidas ...]");
            var x = escolhidas[i];
            var interna = string.Equals((x.StatusWhatsApp ?? "").Trim(), "interno", StringComparison.OrdinalIgnoreCase);
            var autor = x.EnvioCliente == "S" ? "Lead/cliente"
                      : interna ? $"Nota interna ({x.Usuario})"
                      : $"Comercial ({x.Usuario})";

            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(x.Texto)) partes.Add(Cortar(x.Texto.Trim(), 900));
            if (!string.IsNullOrWhiteSpace(x.TranscricaoAudio)) partes.Add("[áudio transcrito] " + Cortar(x.TranscricaoAudio.Trim(), 900));
            else if (!string.IsNullOrWhiteSpace(x.Audio) || x.AudioBytes?.Length > 0) partes.Add("[áudio sem transcrição]");
            if (EhImagemDoCard(x)) partes.Add($"[imagem anexada, mensagem {x.Codigo}]");
            else if (!string.IsNullOrWhiteSpace(x.NomeImagem)) partes.Add($"[arquivo anexado: {x.NomeImagem}]");
            if (partes.Count == 0) continue;
            sb.AppendLine($"[{x.DataHora:dd/MM HH:mm}] {autor}: {string.Join(" ", partes)}");
        }

        var det = new StringBuilder();
        if (card != null)
        {
            if (!string.IsNullOrWhiteSpace(card.Funil))
                det.AppendLine("Etapa do funil: " + (NomesFunil.TryGetValue(card.Funil.Trim(), out var nf) ? nf : card.Funil));
            if (!string.IsNullOrWhiteSpace(card.FaixaLead)) det.AppendLine("Temperatura do lead: " + card.FaixaLead);
            if (!string.IsNullOrWhiteSpace(card.Status)) det.AppendLine("Status do card: " + card.Status);
            if (card.PrevisaoFechamento.HasValue) det.AppendLine($"Previsão de fechamento: {card.PrevisaoFechamento:dd/MM/yyyy}");
            if (!string.IsNullOrWhiteSpace(card.ObservacaoCliente)) det.AppendLine("Observação do cliente: " + Cortar(card.ObservacaoCliente, 500));
            var prods = (card.Produtos ?? new()).Where(p => !string.IsNullOrWhiteSpace(p.Nome)).Select(p => $"{p.Nome} ({p.Quantidade:0.##} {p.Unidade})").ToList();
            var servs = (card.Servicos ?? new()).Where(s => !string.IsNullOrWhiteSpace(s.Nome)).Select(s => $"{s.Nome} ({s.Quantidade:0.##})").ToList();
            if (prods.Count > 0) det.AppendLine("Produtos na proposta: " + string.Join("; ", prods));
            if (servs.Count > 0) det.AppendLine("Serviços na proposta: " + string.Join("; ", servs));
        }

        return new AssistenteContextoTicket
        {
            Origem = "card",
            Modo = "comercial",
            CodCard = card?.Codigo ?? 0,
            CodEmp = codEmp,
            Usuario = usuario ?? "",
            Assunto = card?.Descricao,
            Cliente = card?.NomeCliente,
            Detalhes = det.ToString().Trim(),
            Conversa = sb.ToString(),
            UltimasDoCliente = string.Join("\n", anot.Where(x => x.EnvioCliente == "S")
                .Select(x => !string.IsNullOrWhiteSpace(x.Texto) ? x.Texto.Trim() : (x.TranscricaoAudio ?? "").Trim())
                .Where(s => s.Length > 0).TakeLast(4)),
            TotalMensagens = anot.Count
        };
    }

    /// <summary>Sem ticket/card aberto (lista de tickets, funil comercial): perguntas sobre o sistema.</summary>
    public static AssistenteContextoTicket MontarContextoGeral(int codEmp, string usuario, string modo)
        => new()
        {
            Origem = "geral",
            Modo = modo == "comercial" ? "comercial" : "suporte",
            CodEmp = codEmp,
            Usuario = usuario ?? ""
        };

    /// <summary>Imagem de mensagem do card que a IA consegue ler (os bytes já vêm carregados no card).</summary>
    public static bool EhImagemDoCard(AnotacaoModel x)
    {
        if (x.ImagemBytes == null || x.ImagemBytes.Length == 0) return false;
        var nome = !string.IsNullOrWhiteSpace(x.NomeImagem) ? x.NomeImagem : "imagem.jpg";
        return ImagemIA.PareceImagem(nome);
    }

    private static string MontarTextoContexto(AssistenteContextoTicket t, string baseInterna)
    {
        var sb = new StringBuilder();
        var quem = t.Modo == "comercial" ? "Vendedor (comercial)" : "Atendente";
        sb.AppendLine($"Hoje: {DateTime.Now:dd/MM/yyyy HH:mm}. {quem} que está perguntando: {t.Usuario}.");
        sb.AppendLine();

        if (t.Origem == "geral")
        {
            sb.AppendLine("Não há ticket nem card aberto: a pergunta é sobre o sistema Evolut ou um assunto técnico/comercial geral. Não existe conversa com cliente.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(t.Origem == "card" ? $"DADOS DO CARD #{t.CodCard} (negociação comercial)" : $"DADOS DO TICKET #{t.CodTicket}");
            if (!string.IsNullOrWhiteSpace(t.Cliente)) sb.AppendLine("Cliente: " + t.Cliente);
            if (!string.IsNullOrWhiteSpace(t.Assunto)) sb.AppendLine((t.Origem == "card" ? "Descrição: " : "Assunto: ") + t.Assunto);
            if (!string.IsNullOrWhiteSpace(t.Versao)) sb.AppendLine("Versão do sistema do cliente: " + t.Versao);
            if (!string.IsNullOrWhiteSpace(t.Situacao)) sb.AppendLine("Situação: " + t.Situacao);
            if (!string.IsNullOrWhiteSpace(t.Detalhes)) sb.AppendLine(t.Detalhes);
            sb.AppendLine();
            sb.AppendLine(t.Origem == "card" ? "CONVERSA COM O LEAD (mais antiga primeiro):" : "CONVERSA DO TICKET (mais antiga primeiro):");
            sb.AppendLine(string.IsNullOrWhiteSpace(t.Conversa) ? "(sem mensagens)" : t.Conversa);
            sb.AppendLine();
        }
        sb.AppendLine("BASE INTERNA — resultado da busca automática para a pergunta atual:");
        sb.AppendLine(baseInterna);
        return sb.ToString();
    }

    private const string SystemPrompt = @"
Você é o copiloto técnico do time de suporte da EvolutTech. Quem conversa com você é o ATENDENTE (analista de suporte), nunca o cliente final.

O sistema Evolut é um ERP para varejo (supermercados, padarias, açougues, hortifrutis, depósitos de bebidas): PDV/frente de caixa, fiscal (NF-e, NFC-e, SEFAZ, certificado digital), financeiro (contas a pagar/receber, boletos, PIX, TEF/cartão), estoque, cadastro de produtos, etiquetas, balança, impressoras, relatórios, banco de dados SQL Server.

O QUE VOCÊ RECEBE
- Os dados e a conversa do ticket (cliente, atendentes, notas internas, áudios transcritos). Você já leu tudo: não peça para o atendente repetir o que está na conversa.
- BASE INTERNA: trechos de documentações da EvolutTech [D1], [D2]... e casos já resolvidos por outros atendentes [C1], [C2]...
- Prints, quando o atendente colar uma imagem ou mandar uma imagem do ticket.

COMO RESPONDER (nesta ordem)
1. Base interna primeiro. Se ela cobre o problema, responda com base nela e cite o id logo depois da informação, ex.: ""Ajuste a natureza de operação [D2]"". Se os trechos recebidos não bastarem, use a ferramenta buscar_base_interna com termos mais específicos (código de erro, nome da tela, mensagem do print) antes de ir para fora.
2. Se a base interna não tiver a resposta, diga isso em uma linha no começo (ex.: ""Não encontrei isso nas documentações nem nos casos anteriores."") e então ajude com seu conhecimento técnico e, quando for útil, pesquise na web.
3. Deixe claro o que é procedimento da EvolutTech (base interna) e o que é conhecimento geral/web. Hipótese é hipótese: diga como confirmar.
4. Seja prático: causa provável → como confirmar → como resolver. Passos numerados quando houver procedimento.
5. Pergunta se o Evolut TEM ou FAZ algo (""o cliente consegue fazer receita?"", ""controla estoque por caixa, fardo e unidade?""):
   - Antes de concluir, faça mais de uma busca com buscar_base_interna usando sinônimos do varejo
     (ex.: receita = ficha técnica, composição, produção, insumo; caixa/fardo = embalagem, unidade de compra, fator de conversão, multiplicador, fracionado).
   - Comece com a conclusão em uma linha: **Sim, tem** [D1] / **Tem em parte** / **Não encontrei na base**. Depois explique como funciona e onde fica, só com o que está na base.
   - Só diga que o Evolut tem um recurso se a base interna mostrar. Sem base, diga que não dá para confirmar e sugira verificar com o desenvolvimento. Não use a web para dizer o que o Evolut faz.

PESQUISA NA WEB
- Use quando o atendente pedir, ou quando a base interna não resolver e o assunto depender de informação externa ou atual (rejeições e regras da SEFAZ, notas técnicas, legislação fiscal, mensagens de erro do Windows/SQL Server/.NET, drivers, impressoras, balanças, TEF, certificados).
- Prefira fontes oficiais: Portal Nacional da NF-e, SEFAZ dos estados, Receita Federal, documentação dos fabricantes, Microsoft.
- NUNCA coloque na pesquisa dados do cliente: nome, CNPJ/CPF, telefone, e-mail, endereço, chave de acesso completa ou número de nota. Pesquise só o termo técnico (ex.: ""rejeição 539 duplicidade NF-e diferença na chave de acesso"").

IMAGENS
- Leia o print com atenção: transcreva o código e a mensagem de erro exatamente como aparecem e identifique a tela. Use isso na busca interna e na web.

MENSAGEM PARA O CLIENTE
- Quando o atendente pedir uma resposta/mensagem para o cliente, escreva o texto pronto entre as marcas <resposta_cliente> e </resposta_cliente>.
- Dentro das marcas: tom de atendente humano da EvolutTech no WhatsApp, educado e direto, 2 a 6 linhas, sem markdown, sem citar ids, fontes, IA ou documentação, e sem dizer que vai pesquisar.
- Fora das marcas você pode explicar ao atendente o porquê, em poucas linhas.

FORMATO
- Português do Brasil, direto ao ponto, sem ""Claro!"", ""Ótima pergunta"" ou despedidas.
- Markdown leve: **negrito**, listas, títulos curtos com ###. Sem tabelas largas.
- Não invente menus, telas, teclas ou campos do sistema Evolut que não estejam na base interna. Se não souber o caminho exato no Evolut, diga isso e descreva o conceito.
- Pergunta simples, resposta curta. Detalhe só quando o problema pedir.";

    private static string AdendoModo(AssistenteContextoTicket t)
    {
        if (t.Modo == "comercial")
            return @"

MODO COMERCIAL (vale acima das regras de suporte quando houver conflito)
- Quem pergunta é do time COMERCIAL (vendas), atendendo um lead ou negociando com um cliente.
- O foco costuma ser: o Evolut tem ou não tem tal recurso, como funciona em alto nível, se atende o segmento do cliente (supermercado, padaria, açougue, hortifruti, depósito de bebidas), o que o cliente ganha com isso.
- Siga sempre a regra 5: conclusão primeiro (Sim / Em parte / Não encontrei), e nunca prometa recurso que não esteja na base interna. Se não encontrou, oriente o vendedor a confirmar com o suporte/desenvolvimento antes de responder o lead.
- Linguagem simples, sem detalhe técnico de banco de dados ou configuração interna, a não ser que o vendedor peça.
- Mensagem para o cliente (<resposta_cliente>): tom comercial, cordial e consultivo, mostrando o benefício; sem prometer o que não está confirmado e sem falar de preço se o preço não estiver na conversa." + (t.Origem == "geral" ? AdendoGeral : "");

        return t.Origem == "geral" ? AdendoGeral : "";
    }

    private const string AdendoGeral = @"

SEM ATENDIMENTO ABERTO
- Não há ticket nem card: não existe conversa com cliente para ler. Responda a pergunta sobre o sistema ou o assunto técnico com base na BASE INTERNA (e na web quando for assunto externo).
- Só escreva <resposta_cliente> se pedirem um texto para mandar a alguém.";

    // ═════════════════════════════════════════════════════════════════════
    // 4) Histórico (tabelas TicketAssistenteIA / TicketAssistenteIAAnexo)
    //    Origem: ticket (por ticket) | card (por card) | g-suporte / g-comerc (por usuário, sem atendimento)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Chave gravada na coluna Origem.</summary>
    private static string OrigemDb(string origem, string modo) => origem switch
    {
        "card" => "card",
        "geral" => modo == "comercial" ? "g-comerc" : "g-suporte",
        _ => "ticket"
    };

    private static bool _temColunaOrigem;

    /// <summary>O script _v2 (colunas Origem/CodCard) já rodou? Guarda só o "sim" (passa a valer sem reiniciar).</summary>
    private static async Task<bool> TemColunaOrigemAsync(SqlConnection conn, CancellationToken ct = default)
    {
        if (_temColunaOrigem) return true;
        await using var cmd = new SqlCommand("SELECT COL_LENGTH('dbo.TicketAssistenteIA', 'Origem')", conn);
        var r = await cmd.ExecuteScalarAsync(ct);
        _temColunaOrigem = r != null && r != DBNull.Value;
        if (!_temColunaOrigem)
            LogHelper.Log("[ASSISTENTE] Coluna Origem não existe — rode Scripts/2026-10-24_IA_AssistenteAtendente_v2.sql (card e modo geral ficam sem histórico).");
        return _temColunaOrigem;
    }

    /// <summary>Filtro da conversa no banco + parâmetros. Null = não dá para guardar (script v2 não rodou).</summary>
    private static (string Where, Action<SqlCommand> Params)? FiltroConversa(bool temOrigem, string origem, string modo, int cod, string usuario)
    {
        var od = OrigemDb(origem, modo);
        if (!temOrigem)
            return od == "ticket" && cod > 0
                ? ("CodTicketChamadoC = @c", c => c.Parameters.AddWithValue("@c", cod))
                : null;

        return od switch
        {
            "ticket" => ("Origem = 'ticket' AND CodTicketChamadoC = @c", c => c.Parameters.AddWithValue("@c", cod)),
            "card" => ("Origem = 'card' AND CodCard = @c", c => c.Parameters.AddWithValue("@c", cod)),
            _ => ("Origem = @o AND Usuario = @u", c =>
            {
                c.Parameters.AddWithValue("@o", od);
                c.Parameters.AddWithValue("@u", Cortar(usuario, 100));
            })
        };
    }

    /// <param name="origem">"ticket", "card" ou "geral".</param>
    /// <param name="cod">Código do ticket ou do card (0 no modo geral).</param>
    public async Task<List<AssistenteMensagem>> CarregarHistoricoAsync(string origem, string modo, int cod, string usuario,
        CancellationToken ct = default)
    {
        var lista = new List<AssistenteMensagem>();
        if (origem != "geral" && cod <= 0) return lista;
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);

            var filtro = FiltroConversa(await TemColunaOrigemAsync(conn, ct), origem, modo, cod, usuario);
            if (filtro == null) return lista;

            await using (var cmd = new SqlCommand($@"
SELECT TOP 60 CodTicketAssistenteIA, Papel, Usuario, Mensagem, Fontes, PesquisouWeb, DataHora, Modelo
  FROM dbo.TicketAssistenteIA
 WHERE {filtro.Value.Where} AND Arquivado = 'N'
 ORDER BY CodTicketAssistenteIA DESC", conn))
            {
                filtro.Value.Params(cmd);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var m = new AssistenteMensagem
                    {
                        Codigo = rd.GetInt64(0),
                        Papel = rd.GetString(1),
                        Usuario = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        Texto = rd.IsDBNull(3) ? "" : rd.GetString(3),
                        PesquisouWeb = !rd.IsDBNull(5) && rd.GetBoolean(5),
                        DataHora = rd.GetDateTime(6),
                        Modelo = rd.IsDBNull(7) ? "" : rd.GetString(7)
                    };
                    if (!rd.IsDBNull(4))
                    {
                        try { m.Fontes = JsonSerializer.Deserialize<List<AssistenteFonte>>(rd.GetString(4)) ?? new(); }
                        catch { }
                    }
                    m.DocsEncontrados = m.Fontes.Count(f => f.Tipo == "doc");
                    m.CasosEncontrados = m.Fontes.Count(f => f.Tipo == "caso");
                    lista.Add(m);
                }
            }
            lista.Reverse();

            // Miniaturas das imagens das perguntas
            var codigos = lista.Where(m => !m.EhIA).Select(m => m.Codigo).ToList();
            if (codigos.Count > 0)
            {
                await using var cmdA = new SqlCommand($@"
SELECT CodTicketAssistenteIA, Imagem FROM dbo.TicketAssistenteIAAnexo
 WHERE CodTicketAssistenteIA IN ({string.Join(",", codigos)}) AND Imagem IS NOT NULL
 ORDER BY CodAnexo", conn);
                await using var rd = await cmdA.ExecuteReaderAsync(ct);
                var porMsg = lista.ToDictionary(m => m.Codigo);
                while (await rd.ReadAsync(ct))
                {
                    var codMsg = rd.GetInt64(0);
                    var bytes = (byte[])rd[1];
                    var mini = await ImagemIA.ParaDataUrlAsync(bytes, 360, ct);
                    if (mini != null && porMsg.TryGetValue(codMsg, out var msg)) msg.Miniaturas.Add(mini);
                }
            }
        }
        catch (SqlException ex) when (ex.Number == 208) // tabela não existe (script não rodado)
        {
            LogHelper.Log("[ASSISTENTE] Tabelas TicketAssistenteIA não existem — rode Scripts/2026-10-24_IA_AssistenteAtendente.sql");
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] Erro ao carregar histórico: " + ex.Message);
        }
        return lista;
    }

    /// <summary>"Nova conversa": arquiva o chat atual (continua no banco).</summary>
    public async Task NovaConversaAsync(string origem, string modo, int cod, string usuario)
    {
        if (origem == "ticket") _atendimentoIa.EsquecerConversa(cod, usuario);
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var filtro = FiltroConversa(await TemColunaOrigemAsync(conn), origem, modo, cod, usuario);
            if (filtro == null) return;
            await using var cmd = new SqlCommand(
                $"UPDATE dbo.TicketAssistenteIA SET Arquivado = 'S' WHERE {filtro.Value.Where} AND Arquivado = 'N'", conn);
            filtro.Value.Params(cmd);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] NovaConversa: " + ex.Message);
        }
    }

    private async Task<long> GravarMensagemAsync(AssistenteContextoTicket t, string papel, string texto,
        List<AssistenteFonte>? fontes, bool web, AssistenteResultado? res,
        List<(string Origem, int CodD, string DataUrl)>? imagens)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var temOrigem = await TemColunaOrigemAsync(conn);
            var od = OrigemDb(t.Origem, t.Modo);
            if (!temOrigem && od != "ticket") return 0;   // sem o script v2, só o ticket guarda histórico

            long cod;
            await using (var cmd = new SqlCommand(temOrigem
                ? @"
INSERT INTO dbo.TicketAssistenteIA
    (CodTicketChamadoC, CodEmp, Usuario, Papel, Mensagem, Fontes, PesquisouWeb, Modelo, DuracaoMs, TokensEntrada, TokensSaida, Origem, CodCard)
OUTPUT INSERTED.CodTicketAssistenteIA
VALUES (@t, @emp, @u, @p, @m, @f, @w, @mod, @dur, @te, @ts, @o, @card);"
                : @"
INSERT INTO dbo.TicketAssistenteIA
    (CodTicketChamadoC, CodEmp, Usuario, Papel, Mensagem, Fontes, PesquisouWeb, Modelo, DuracaoMs, TokensEntrada, TokensSaida)
OUTPUT INSERTED.CodTicketAssistenteIA
VALUES (@t, @emp, @u, @p, @m, @f, @w, @mod, @dur, @te, @ts);", conn))
            {
                if (temOrigem)
                {
                    cmd.Parameters.AddWithValue("@o", od);
                    cmd.Parameters.AddWithValue("@card", t.CodCard > 0 ? t.CodCard : DBNull.Value);
                }
                cmd.Parameters.AddWithValue("@t", od == "ticket" ? t.CodTicket : 0);
                cmd.Parameters.AddWithValue("@emp", t.CodEmp > 0 ? t.CodEmp : DBNull.Value);
                cmd.Parameters.AddWithValue("@u", Cortar(t.Usuario, 100));
                cmd.Parameters.AddWithValue("@p", papel);
                cmd.Parameters.AddWithValue("@m", texto ?? "");
                cmd.Parameters.AddWithValue("@f", fontes == null || fontes.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(fontes));
                cmd.Parameters.AddWithValue("@w", web);
                cmd.Parameters.AddWithValue("@mod", res == null ? DBNull.Value : Cortar(res.Modelo, 60));
                cmd.Parameters.AddWithValue("@dur", res == null ? DBNull.Value : res.DuracaoMs);
                cmd.Parameters.AddWithValue("@te", res == null ? DBNull.Value : res.TokensEntrada);
                cmd.Parameters.AddWithValue("@ts", res == null ? DBNull.Value : res.TokensSaida);
                cod = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            foreach (var (origem, codD, dataUrl) in imagens ?? new())
            {
                var bytes = BytesDeDataUrl(dataUrl);
                if (bytes == null) continue;
                await using var cmdA = new SqlCommand(@"
INSERT INTO dbo.TicketAssistenteIAAnexo (CodTicketAssistenteIA, Origem, CodTicketChamadoD, Imagem)
VALUES (@c, @o, @d, @i);", conn);
                cmdA.Parameters.AddWithValue("@c", cod);
                cmdA.Parameters.AddWithValue("@o", origem);
                cmdA.Parameters.AddWithValue("@d", codD > 0 ? codD : DBNull.Value);
                cmdA.Parameters.Add("@i", System.Data.SqlDbType.VarBinary, -1).Value = bytes;
                await cmdA.ExecuteNonQueryAsync();
            }
            return cod;
        }
        catch (Exception ex)
        {
            // Sem as tabelas o assistente funciona, só não guarda histórico
            LogHelper.Log("[ASSISTENTE] Não gravou histórico: " + ex.Message);
            return 0;
        }
    }

    /// <summary>Imagem de uma mensagem do ticket (TicketChamadoD.Imagem) como data URL JPEG.</summary>
    public async Task<string?> CarregarImagemDoTicketAsync(int codTicket, int codD, int max, CancellationToken ct = default)
    {
        if (codTicket <= 0 || codD <= 0) return null;
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SELECT Imagem FROM TicketChamadoD
 WHERE Codigo = @c AND CodTicketChamadoC = @t AND Imagem IS NOT NULL", conn);
            cmd.Parameters.AddWithValue("@c", codD);
            cmd.Parameters.AddWithValue("@t", codTicket);
            if (await cmd.ExecuteScalarAsync(ct) is not byte[] bytes || bytes.Length == 0) return null;
            return await ImagemIA.ParaDataUrlAsync(bytes, max, ct);
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] Imagem do ticket: " + ex.Message);
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5) Salvar como insight (o que foi resolvido vira conhecimento)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>A IA resume problema + solução da conversa para o atendente revisar.</summary>
    public async Task<AssistenteInsightRascunho?> GerarRascunhoInsightAsync(AssistenteContextoTicket t,
        IReadOnlyList<AssistenteMensagem> chat, string? respostaEscolhida, CancellationToken ct = default)
    {
        var modelo = _config["OpenAI:Model"] ?? "gpt-4.1-mini";
        var conversaAssistente = string.Join("\n\n", chat
            .Where(m => !m.Gerando && !m.Erro && !string.IsNullOrWhiteSpace(m.Texto))
            .TakeLast(10)
            .Select(m => (m.EhIA ? "IA: " : "Atendente: ") + Cortar(m.Texto, 2500)));

        var user = new StringBuilder();
        user.AppendLine($"TICKET #{t.CodTicket} — Assunto: {t.Assunto}");
        user.AppendLine();
        user.AppendLine("CONVERSA DO TICKET:");
        user.AppendLine(Cortar(t.Conversa, 12000));
        user.AppendLine();
        user.AppendLine("CONVERSA DO ATENDENTE COM A IA:");
        user.AppendLine(conversaAssistente);
        if (!string.IsNullOrWhiteSpace(respostaEscolhida))
        {
            user.AppendLine();
            user.AppendLine("RESPOSTA DA IA QUE O ATENDENTE MARCOU COMO A SOLUÇÃO:");
            user.AppendLine(Cortar(respostaEscolhida, 4000));
        }

        var body = new
        {
            model = modelo,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = @"Você transforma um atendimento de suporte do sistema Evolut (ERP) em um caso reutilizável para a base de conhecimento.
Responda SOMENTE com JSON:
{ ""topicoTitulo"": """", ""perguntaCliente"": """", ""resumo"": """", ""categoria"": """", ""causaProvavel"": """", ""solucao"": """", ""tags"": ["""",""""] }
- topicoTitulo: até 80 caracteres.
- perguntaCliente: a dúvida do cliente em uma frase, sem nome nem dados do cliente.
- resumo: o problema em até 250 caracteres, com o código/mensagem de erro exato quando houver.
- causaProvavel: a causa, em até 400 caracteres.
- solucao: o passo a passo concreto que resolve (telas, ações, configurações), em até 1000 caracteres. Sem nomes, CNPJ ou dados do cliente. Sem ids como [D1].
- categoria: exatamente uma de: pdv, fiscal_nfe, fiscal_nfce, financeiro_pagar, financeiro_receber, estoque, cadastro_produto, cadastro_cliente_fornecedor, balanca, impressao, etiqueta, relatorio, acesso_remoto, hardware, configuracao, integracao, outro
- tags: 3 a 8, minúsculas, sem acento, sem espaço (use _), técnicas e úteis para busca. Nada genérico como erro, sistema, problema." },
                new { role = "user", content = user.ToString() }
            }
        };

        try
        {
            var http = _httpFactory.CreateClient("openai");
            using var resp = await http.PostAsync("v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                LogHelper.Log("[ASSISTENTE] Rascunho insight HTTP " + (int)resp.StatusCode + ": " + Cortar(raw, 400));
                return null;
            }
            using var doc = JsonDocument.Parse(raw);
            var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            using var j = JsonDocument.Parse(conteudo);
            var r = j.RootElement;
            var tags = r.TryGetProperty("tags", out var pt) && pt.ValueKind == JsonValueKind.Array
                ? string.Join(", ", pt.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : "";
            return new AssistenteInsightRascunho
            {
                TopicoTitulo = Str(r, "topicoTitulo"),
                PerguntaCliente = Str(r, "perguntaCliente"),
                Resumo = Str(r, "resumo"),
                Categoria = Str(r, "categoria") is { Length: > 0 } c ? c : "outro",
                CausaProvavel = Str(r, "causaProvavel"),
                Solucao = Str(r, "solucao"),
                Tags = tags
            };
        }
        catch (Exception ex)
        {
            LogHelper.Log("[ASSISTENTE] Rascunho insight: " + ex.Message);
            return null;
        }
    }

    /// <summary>Grava o insight (confirmado pelo atendente) e já coloca no índice da IA.</summary>
    public async Task SalvarInsightAsync(int codTicket, AssistenteInsightRascunho r)
    {
        var tags = (r.Tags ?? "")
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .Take(10)
            .ToList();

        await _insights.InserirManualAsync(codTicket, new TicketInsight
        {
            topicoTitulo = r.TopicoTitulo,
            perguntaCliente = r.PerguntaCliente,
            resumo = r.Resumo,
            categoria = string.IsNullOrWhiteSpace(r.Categoria) ? "outro" : r.Categoria.Trim(),
            intencao = "suporte",
            gravidade = "media",
            tags = tags,
            causaProvavel = r.CausaProvavel,
            solucao = r.Solucao,
            solucaoConfirmada = true,   // o atendente revisou e confirmou
            confianca = 90
        });

        try
        {
            await _indexer.IndexarInsightsDoTicketAsync(codTicket);
            await _index.AtualizarInsightsDoTicketAsync(codTicket);
        }
        catch (Exception ex)
        {
            // Já está salvo; a rotina da madrugada coloca no índice
            LogHelper.Log($"[ASSISTENTE] Insight salvo, mas não entrou no índice agora: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Utilitários
    // ═════════════════════════════════════════════════════════════════════

    private static string Cortar(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

    private static string Str(JsonElement e, string nome)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "") : "";

    private static int Int(JsonElement e, string nome)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

    private static string LerArgumento(string json, string nome)
    {
        try
        {
            using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return Str(d.RootElement, nome);
        }
        catch { return ""; }
    }

    private static string LerMensagemErro(string corpo)
    {
        try
        {
            using var d = JsonDocument.Parse(corpo);
            if (d.RootElement.TryGetProperty("error", out var e)) return Str(e, "message");
        }
        catch { }
        return Cortar(corpo, 200);
    }

    private static byte[]? BytesDeDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        var i = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
        if (i < 0) return null;
        try { return Convert.FromBase64String(dataUrl[(i + 7)..]); }
        catch { return null; }
    }
}
