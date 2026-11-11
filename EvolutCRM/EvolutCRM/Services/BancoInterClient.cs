using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

// Um cliente por operação. Não cria arquivo PFX nem grava PDFs em disco.
internal sealed class BancoInterClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly X509Certificate2? _cert;
    private readonly BoletoInterConfig _config;
    private string? _token;
    private readonly string _base;

    // Permite testar o transporte HTTP sem certificado ou acesso ao banco real.
    internal BancoInterClient(BoletoInterConfig config, HttpClient http, string baseUrl)
    {
        _config = config; _http = http; _base = baseUrl;
    }

    public BancoInterClient(BoletoInterConfig config, IConfiguration settings)
    {
        _config = config;
        _base = settings.GetValue<bool>("BoletoInter:Sandbox")
            ? "https://cdpj-sandbox.partners.uatinter.co"
            : "https://cdpj.partners.bancointer.com.br";
        if (!File.Exists(config.Crt) || !File.Exists(config.Key))
            throw new InvalidOperationException("O CRT e a chave KEY precisam estar acessíveis no servidor do HELP.");
        using var pem = X509Certificate2.CreateFromPemFile(config.Crt, config.Key);
        // Schannel no Windows pode recusar chaves efêmeras no mTLS.
        // Sem PersistKeySet, a chave importada é liberada ao descartar o certificado.
        var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.EphemeralKeySet;
        _cert = new X509Certificate2(pem.Export(X509ContentType.Pkcs12), (string?)null, flags);
        if (!_cert.HasPrivateKey || DateTime.UtcNow < _cert.NotBefore.ToUniversalTime()
            || DateTime.UtcNow >= _cert.NotAfter.ToUniversalTime())
        {
            _cert.Dispose();
            throw new InvalidOperationException("O certificado do Banco Inter está inválido ou vencido.");
        }
        var handler = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
        handler.ClientCertificates.Add(_cert);
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
    }

    public async Task AutenticarAsync()
    {
        if (_token is not null) return;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _config.ClientId, ["client_secret"] = _config.ClientSecret,
            ["grant_type"] = "client_credentials", ["scope"] = _config.Scope
        });
        using var response = await _http.PostAsync(_base + "/oauth/v2/token", content);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha na autenticação do Inter (HTTP {(int)response.StatusCode}). Verifique credenciais, certificado e escopos.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        _token = Texto(json.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("O Inter não retornou o token de acesso.");
    }

    // NUNCA repetir automaticamente este POST: timeout/5xx não garantem que o banco recusou a emissão.
    public async Task<string> EmitirAsync(string json)
    {
        using var request = Requisicao(HttpMethod.Post, "/cobranca/v3/cobrancas");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Emissão sem confirmação (HTTP {(int)response.StatusCode}). Confira no Inter antes de qualquer nova emissão.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codigo = Texto(body.RootElement, "codigoSolicitacao");
        if (!Guid.TryParse(codigo, out _))
            throw new InvalidOperationException("O Inter respondeu sem um código de solicitação válido. Confira a cobrança no banco.");
        return codigo;
    }

    public async Task<JsonDocument> ConsultarAsync(string codigo)
    {
        using var response = await GetAsync(codigo, "");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task<byte[]> BaixarPdfAsync(string codigo)
    {
        using var response = await GetAsync(codigo, "/pdf");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var base64 = Texto(json.RootElement, "pdf");
        if (base64.Length == 0) base64 = Texto(json.RootElement, "arquivo");
        if (base64.Length > 28_000_000) throw new InvalidOperationException("PDF maior que o limite de 20 MB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new InvalidOperationException("O Inter retornou um PDF inválido."); }
        ValidarPdf(bytes);
        return bytes;
    }

    // Lista as cobranças (v3) de um pagador por vencimento. Usado para achar o código da solicitação de boletos antigos.
    public async Task<JsonDocument> ListarCobrancasAsync(DateTime inicio, DateTime fim, string cpfCnpj, int pagina)
    {
        var path = "/cobranca/v3/cobrancas?dataInicial=" + inicio.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            + "&dataFinal=" + fim.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            + "&filtrarDataPor=VENCIMENTO&cpfCnpjPessoaPagadora=" + Uri.EscapeDataString(cpfCnpj)
            + "&paginacao.itensPorPagina=100&paginacao.paginaAtual=" + pagina;
        for (int i = 0; ; i++)
        {
            using var request = Requisicao(HttpMethod.Get, path);
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = response.StatusCode;
            bool temporario = status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!temporario || i >= 2)
                throw new InvalidOperationException($"Não foi possível pesquisar as cobranças no Inter (HTTP {(int)status}). Tente novamente.");
            await Task.Delay(TimeSpan.FromSeconds(2 * (i + 1)));
        }
    }

    // Retentativas limitadas apenas nas leituras; o botão Recuperar PDF permite consultar novamente.
    private async Task<HttpResponseMessage> GetAsync(string codigo, string suffix)
    {
        if (!Guid.TryParse(codigo, out _)) throw new InvalidOperationException("Código da solicitação do Inter inválido.");
        for (int i = 0; ; i++)
        {
            using var request = Requisicao(HttpMethod.Get,
                "/cobranca/v3/cobrancas/" + Uri.EscapeDataString(codigo) + suffix);
            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            bool temporario = status is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!temporario || i >= 2)
                throw new InvalidOperationException($"Consulta ao Inter indisponível (HTTP {(int)status}). A cobrança não será reemitida. Tente recuperar o PDF novamente.");
            await Task.Delay(TimeSpan.FromSeconds(2 * (i + 1)));
        }
    }

    private HttpRequestMessage Requisicao(HttpMethod method, string path)
    {
        if (_token is null) throw new InvalidOperationException("Cliente Inter não autenticado.");
        var request = new HttpRequestMessage(method, _base + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_config.ContaCorrente))
            request.Headers.Add("x-conta-corrente", _config.ContaCorrente);
        return request;
    }

    internal static string Texto(JsonElement json, string campo) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(campo, out var v)
            && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? v.ToString() : "";

    internal static void ValidarPdf(byte[] bytes)
    {
        if (bytes.Length < 5 || bytes.Length > 20 * 1024 * 1024
            || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidOperationException("O documento recebido não é um PDF válido de até 20 MB.");
    }

    public void Dispose() { _http.Dispose(); _cert?.Dispose(); }
}
