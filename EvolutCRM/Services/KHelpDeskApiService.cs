using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EvolutCRM.Services;

public class KHelpDeskApiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    private string _token = "";
    private DateTime _tokenExpiraEm = DateTime.MinValue;

    public KHelpDeskApiService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<Dictionary<string, bool>> BuscarStatusDispositivosAsync(IEnumerable<string> codigosAcesso)
    {
        var codigos = codigosAcesso
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .ToList();

        var status = codigos.ToDictionary(x => x, _ => false);

        if (!codigos.Any())
            return status;

        var token = await AutenticarAsync();

        var baseUrl = _cfg["KHelpDeskApi:BaseUrl"]?.TrimEnd('/');
        var endpoint = _cfg["KHelpDeskApi:DevicesEndpoint"];

        foreach (var codigo in codigos)
        {
            var body = new
            {
                page = 0,
                size = 100,
                search = codigo
            };

            var jsonBody = JsonSerializer.Serialize(body);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{endpoint}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Erro ao buscar dispositivo {codigo}: {resp.StatusCode} - {json}");

            using var doc = JsonDocument.Parse(json);

            var dispositivos = ExtrairArrayDispositivos(doc.RootElement);

            foreach (var disp in dispositivos)
            {
                var id = LerString(disp, "id_virtual")
                         ?? LerString(disp, "idVirtual")
                         ?? LerString(disp, "id")
                         ?? LerString(disp, "Id")
                         ?? LerString(disp, "codigo")
                         ?? LerString(disp, "Codigo");

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                id = id.Trim();

                if (!id.Equals(codigo, StringComparison.OrdinalIgnoreCase))
                    continue;

                var onlineValor = LerString(disp, "online")
                                  ?? LerString(disp, "Online")
                                  ?? "0";

                status[codigo] = onlineValor == "1"
                                 || onlineValor.Equals("true", StringComparison.OrdinalIgnoreCase)
                                 || onlineValor.Equals("S", StringComparison.OrdinalIgnoreCase);

                break;
            }
        }

        return status;
    }

    private static string MontarUrlDispositivosPaginada(string? baseUrl, string? endpoint, int pagina)
    {
        baseUrl = (baseUrl ?? "").TrimEnd('/');
        endpoint = endpoint ?? "";

        var separador = endpoint.Contains("?") ? "&" : "?";

        return $"{baseUrl}{endpoint}{separador}page={pagina}";
    }

    private static int? LerInt(JsonElement root, string caminho)
    {
        var valor = LerString(root, caminho);

        if (int.TryParse(valor, out var numero))
            return numero;

        return null;
    }

    private async Task<string> AutenticarAsync()
    {
        if (!string.IsNullOrWhiteSpace(_token) && DateTime.Now < _tokenExpiraEm)
            return _token;

        var baseUrl = _cfg["KHelpDeskApi:BaseUrl"]?.TrimEnd('/');
        var endpoint = _cfg["KHelpDeskApi:AuthEndpoint"];
        var usuario = _cfg["KHelpDeskApi:Usuario"];
        var senha = _cfg["KHelpDeskApi:Senha"];

        var body = new
        {
            email = usuario,            
            password = senha
            
        };

        var jsonBody = JsonSerializer.Serialize(body);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Erro ao autenticar na API KHelpDesk: {resp.StatusCode} - {json}");

        using var doc = JsonDocument.Parse(json);

        var token = LerString(doc.RootElement, "token")
                    ?? LerString(doc.RootElement, "response.accessToken")
                    ?? LerString(doc.RootElement, "bearer")
                    ?? LerString(doc.RootElement, "jwt")
                    ?? LerString(doc.RootElement, "data.token")
                    ?? LerString(doc.RootElement, "data.access_token");

        if (string.IsNullOrWhiteSpace(token))
            throw new Exception("Token não encontrado no retorno da autenticação da API KHelpDesk.");

        _token = token;
        _tokenExpiraEm = DateTime.Now.AddMinutes(50);

        return _token;
    }

    private static IEnumerable<JsonElement> ExtrairArrayDispositivos(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
            return records.EnumerateArray();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            return data.EnumerateArray();

        if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            return devices.EnumerateArray();

        if (root.TryGetProperty("dispositivos", out var dispositivos) && dispositivos.ValueKind == JsonValueKind.Array)
            return dispositivos.EnumerateArray();

        return Enumerable.Empty<JsonElement>();
    }

    private static string? LerString(JsonElement root, string caminho)
    {
        var partes = caminho.Split('.');

        var atual = root;

        foreach (var parte in partes)
        {
            if (atual.ValueKind != JsonValueKind.Object)
                return null;

            if (!atual.TryGetProperty(parte, out atual))
                return null;
        }

        return atual.ValueKind switch
        {
            JsonValueKind.String => atual.GetString(),
            JsonValueKind.Number => atual.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}