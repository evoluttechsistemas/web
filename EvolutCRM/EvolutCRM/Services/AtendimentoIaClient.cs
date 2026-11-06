using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvolutCRM.Helpers;

namespace EvolutCRM.Services;

/// <summary>
/// Cliente da API do AtendimentoIA (resposta da IA para chamados).
/// O AtendimentoIA concentra a leitura de docs, insights aprovados, parâmetros e código-fonte do ERP;
/// o HELP só pergunta e mostra a resposta para o atendente revisar.
/// O token fica só no servidor (appsettings: AtendimentoIA:Url e AtendimentoIA:Token) e nunca vai para o navegador.
/// </summary>
public sealed class AtendimentoIaClient
{
    public const string NomeHttpClient = "AtendimentoIA";
    private static readonly TimeSpan ValidadeContinuacao = TimeSpan.FromHours(7.5);   // a API aceita por 8h

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    // Continuação da conversa por ticket + atendente (a API devolve uma nova a cada resposta)
    private readonly ConcurrentDictionary<string, (string Valor, DateTime Expira)> _continuacoes = new();

    public AtendimentoIaClient(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    private string Url => (_config["AtendimentoIA:Url"] ?? "").Trim().TrimEnd('/');
    private string Token => (_config["AtendimentoIA:Token"] ?? "").Trim();

    /// <summary>Ligado quando Url e Token estão no appsettings. Desligado = o painel usa a IA antiga do HELP.</summary>
    public bool Configurado =>
        Token.Length >= 32 &&
        Uri.TryCreate(Url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttps || u.IsLoopback);

    private static string Chave(int codTicket, string usuario) => $"{codTicket}|{(usuario ?? "").Trim().ToLowerInvariant()}";

    public bool TemConversa(int codTicket, string usuario) =>
        _continuacoes.TryGetValue(Chave(codTicket, usuario), out var c) && c.Expira > DateTime.UtcNow;

    public void EsquecerConversa(int codTicket, string usuario) => _continuacoes.TryRemove(Chave(codTicket, usuario), out _);

    public async Task<AtendimentoIaResposta> PerguntarAsync(int codTicket, string usuario, string pergunta, CancellationToken ct)
    {
        if (!Configurado)
            return AtendimentoIaResposta.Falha("O AtendimentoIA não está configurado (AtendimentoIA:Url e AtendimentoIA:Token).");

        pergunta = (pergunta ?? "").Trim();
        if (pergunta.Length == 0) return AtendimentoIaResposta.Falha("Digite uma pergunta.");
        if (pergunta.Length > 2000) pergunta = pergunta[..2000];

        var chave = Chave(codTicket, usuario);
        string? continuacao = _continuacoes.TryGetValue(chave, out var c) && c.Expira > DateTime.UtcNow ? c.Valor : null;

        var r = await EnviarAsync(codTicket, pergunta, continuacao, ct);

        // Continuação expirada ou inválida: começa uma conversa nova com a mesma pergunta
        if (r.Status == HttpStatusCode.BadRequest && continuacao != null)
        {
            _continuacoes.TryRemove(chave, out _);
            r = await EnviarAsync(codTicket, pergunta, null, ct);
        }

        if (r.Resposta != null)
        {
            if (!string.IsNullOrWhiteSpace(r.Resposta.Continuacao))
                _continuacoes[chave] = (r.Resposta.Continuacao!, DateTime.UtcNow + ValidadeContinuacao);
            LimparExpiradas();
            return r.Resposta;
        }
        return AtendimentoIaResposta.Falha(r.Erro ?? "Não foi possível falar com o AtendimentoIA.");
    }

    private async Task<(HttpStatusCode? Status, AtendimentoIaResposta? Resposta, string? Erro)> EnviarAsync(
        int codTicket, string pergunta, string? continuacao, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(NomeHttpClient);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/api/v1/help/chamados/{codTicket}/respostas")
            {
                Content = JsonContent.Create(new { pergunta, continuacao })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            using var resp = await client.SendAsync(req, ct);
            // Página HTML no lugar da API: a versão publicada não tem a API ou mandou para o login
            var tipo = resp.Content.Headers.ContentType?.MediaType ?? "";
            if ((int)resp.StatusCode is >= 300 and < 400 || (resp.IsSuccessStatusCode && !tipo.Contains("json", StringComparison.OrdinalIgnoreCase)))
            {
                var destino = resp.Headers.Location?.ToString() ?? "";
                LogHelper.Log($"[ATENDIMENTO-IA] Ticket #{codTicket} | HTTP {(int)resp.StatusCode} | tipo={tipo} | destino={destino}");
                return (resp.StatusCode, null,
                    "O AtendimentoIA devolveu uma página em vez da API. A versão publicada pode não ter a API do HELP, " +
                    "ou ele está mandando para a tela de login.");
            }

            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<AtendimentoIaResposta>(Json, ct);
                if (dto == null || string.IsNullOrWhiteSpace(dto.Resposta))
                    return (resp.StatusCode, null, "O AtendimentoIA não devolveu resposta. Tente perguntar de outro jeito.");
                dto.Ok = true;
                return (resp.StatusCode, dto, null);
            }

            var detalhe = await LerErroAsync(resp, ct);
            LogHelper.Log($"[ATENDIMENTO-IA] Ticket #{codTicket} | HTTP {(int)resp.StatusCode} | {detalhe}");
            var erro = resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "O AtendimentoIA recusou a credencial do HELP. Confira o AtendimentoIA:Token.",
                HttpStatusCode.NotFound => "O AtendimentoIA não encontrou este chamado.",
                HttpStatusCode.TooManyRequests => "O AtendimentoIA está atendendo outras perguntas agora. Tente de novo em alguns segundos.",
                HttpStatusCode.BadRequest => string.IsNullOrWhiteSpace(detalhe) ? "Pergunta recusada pelo AtendimentoIA." : detalhe,
                _ => "O AtendimentoIA não conseguiu responder agora. Tente novamente em instantes."
            };
            return (resp.StatusCode, null, erro);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            LogHelper.Log($"[ATENDIMENTO-IA] Ticket #{codTicket} | tempo esgotado");
            return (null, null, "O AtendimentoIA demorou demais para responder. Tente novamente.");
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[ATENDIMENTO-IA] Ticket #{codTicket} | {ex.GetType().Name}: {ex.Message}");
            return (null, null, "Não foi possível conectar ao AtendimentoIA. Confira se ele está no ar e o AtendimentoIA:Url.");
        }
    }

    private static async Task<string> LerErroAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var txt = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(txt)) return "";
            using var doc = JsonDocument.Parse(txt);
            foreach (var campo in new[] { "erro", "detail", "title" })
                if (doc.RootElement.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "";
            return "";
        }
        catch { return ""; }
    }

    private void LimparExpiradas()
    {
        if (_continuacoes.Count < 200) return;
        var agora = DateTime.UtcNow;
        foreach (var kv in _continuacoes.Where(k => k.Value.Expira <= agora).ToList())
            _continuacoes.TryRemove(kv.Key, out _);
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

public sealed class AtendimentoIaResposta
{
    [JsonIgnore] public bool Ok { get; set; }
    [JsonIgnore] public string Erro { get; set; } = "";

    public string? Resposta { get; set; }
    public string? PerguntaDeAcompanhamento { get; set; }
    public bool EvidenciasSuficientes { get; set; }
    public string? Segmento { get; set; }
    public string? VersaoErp { get; set; }
    public string? Formato { get; set; }                 // texto | fluxo | imagem
    public List<string>? Passos { get; set; }
    public string? Imagem { get; set; }                  // imagem da tela (data URI), quando houver
    public string? AvisoDaImagem { get; set; }
    public List<AtendimentoIaVideo>? Videos { get; set; }
    public string? Continuacao { get; set; }

    public static AtendimentoIaResposta Falha(string erro) => new() { Ok = false, Erro = erro };
}

public sealed class AtendimentoIaVideo
{
    public string? Titulo { get; set; }
    public string? Url { get; set; }
}
