using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>Servidor monitorado (configurável em "MonitorServidores:Servidores").</summary>
public class ServidorMonitoradoConfig
{
    public string Nome { get; set; } = "";
    public string Host { get; set; } = "";
    public int Porta { get; set; }
}

/// <summary>Situação atual de um servidor monitorado.</summary>
public class ServidorStatus
{
    public string Nome { get; init; } = "";
    public string Host { get; init; } = "";
    public int Porta { get; init; }

    /// <summary>null = ainda não verificado; true = respondendo; false = fora do ar (confirmado).</summary>
    public bool? Online { get; set; }
    public int FalhasSeguidas { get; set; }
    public DateTime? UltimaVerificacao { get; set; }
    public DateTime? UltimoSucesso { get; set; }
    public long? LatenciaMs { get; set; }
    public DateTime? ForaDesde { get; set; }
    public string? UltimoErro { get; set; }
    public List<ServidorQueda> UltimasQuedas { get; set; } = new();

    public string Chave => $"{Host}:{Porta}";
    public bool ForaDoAr => Online == false;
}

public record ServidorQueda(DateTime Inicio, DateTime? Fim, string? Erro)
{
    public TimeSpan Duracao => (Fim ?? DateTime.Now) - Inicio;
}

/// <summary>
/// Verifica a cada minuto se os servidores configurados respondem (conexão TCP na porta,
/// sem usuário e senha). Depois de 3 falhas seguidas considera FORA DO AR:
///   • avisa todas as telas do HELP (painel de notificações, igual tickets) — evento OnMudanca
///   • manda WhatsApp para os números configurados (só no servidor de produção)
///   • registra a queda na tabela MonitorServidorQueda (histórico)
/// Quando volta, avisa de novo ("voltou, ficou X fora").
/// </summary>
public class MonitorServidorService : BackgroundService
{
    private const int FalhasParaAlerta = 3;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TempoLimiteConexao = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LembreteWhatsApp = TimeSpan.FromHours(2);
    private const string BaileysBase = "http://localhost:3333";

    private static readonly ServidorMonitoradoConfig[] ServidoresPadrao =
    {
        new() { Nome = "Servidor de backup (FTP)", Host = "131.221.148.129", Porta = 2121 }
    };

    private static readonly string[] WhatsAppPadrao = { "5517991242825", "5517991642275" };

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly HealthMonitorService _health;
    private readonly ConcurrentDictionary<string, ServidorStatus> _status = new();
    private readonly Dictionary<string, DateTime> _ultimoWhatsApp = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Disparado quando um servidor cai (confirmado) ou volta. Todas as telas escutam.</summary>
    public event Action<ServidorStatus>? OnMudanca;

    /// <summary>Disparado a cada verificação (para a tela do monitor atualizar).</summary>
    public event Action? OnVerificado;

    public MonitorServidorService(IConfiguration config, IWebHostEnvironment env, HealthMonitorService health)
    {
        _config = config;
        _env = env;
        _health = health;

        foreach (var s in Servidores())
            _status[$"{s.Host}:{s.Porta}"] = new ServidorStatus { Nome = s.Nome, Host = s.Host, Porta = s.Porta };
    }

    public IReadOnlyList<ServidorStatus> Status => _status.Values.OrderBy(s => s.Nome).ToList();
    public IEnumerable<ServidorStatus> ForaDoAr => _status.Values.Where(s => s.ForaDoAr);

    // Só o servidor de produção manda WhatsApp (o PC de desenvolvimento também roda o monitor)
    private bool EnviaWhatsApp =>
        _env.IsProduction() || _config.GetValue<bool>("MonitorServidores:WhatsAppEmDev");

    // O histórico no banco também só é gravado pelo servidor (o PC de desenvolvimento usa o mesmo banco)
    private bool GravaNoBanco => _env.IsProduction();

    private List<ServidorMonitoradoConfig> Servidores()
    {
        var lista = _config.GetSection("MonitorServidores:Servidores").Get<List<ServidorMonitoradoConfig>>();
        return lista is { Count: > 0 }
            ? lista.Where(s => !string.IsNullOrWhiteSpace(s.Host) && s.Porta > 0).ToList()
            : ServidoresPadrao.ToList();
    }

    private string[] NumerosWhatsApp()
    {
        var lista = _config.GetSection("MonitorServidores:WhatsApp").Get<string[]>();
        return lista is { Length: > 0 } ? lista : WhatsAppPadrao;
    }

    private SqlConnection CriarConexao() => new(_config.GetConnectionString("Connection"));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await GarantirTabelaAsync(); } catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] Tabela: {ex.Message}"); }
        try { await CarregarQuedasAsync(); } catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] Histórico: {ex.Message}"); }

        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch { return; }

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            foreach (var s in _status.Values.ToList())
            {
                try { await VerificarAsync(s, ct); }
                catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] {s.Nome}: {ex.Message}"); }
            }

            try { OnVerificado?.Invoke(); } catch { }
        }
        while (await AguardarAsync(timer, ct));
    }

    private static async Task<bool> AguardarAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Verifica agora (botão Atualizar da tela).</summary>
    public async Task VerificarAgoraAsync()
    {
        foreach (var s in _status.Values.ToList())
            await VerificarAsync(s, CancellationToken.None);
        try { OnVerificado?.Invoke(); } catch { }
    }

    private readonly SemaphoreSlim _trava = new(1, 1);

    private async Task VerificarAsync(ServidorStatus s, CancellationToken ct)
    {
        await _trava.WaitAsync(ct);
        try
        {
            var (ok, ms, erro) = await TestarConexaoAsync(s.Host, s.Porta, ct);
            var agora = DateTime.Now;
            s.UltimaVerificacao = agora;

            if (ok)
            {
                s.LatenciaMs = ms;
                s.UltimoSucesso = agora;
                s.FalhasSeguidas = 0;
                s.UltimoErro = null;

                if (s.Online != true)
                {
                    var estavaFora = s.Online == false;
                    var desde = s.ForaDesde;
                    s.Online = true;
                    s.ForaDesde = null;

                    if (estavaFora && desde.HasValue)
                        await RegistrarVoltaAsync(s, desde.Value, agora);
                }
                return;
            }

            s.FalhasSeguidas++;
            s.UltimoErro = erro;
            s.LatenciaMs = null;

            if (s.FalhasSeguidas < FalhasParaAlerta)
                return; // espera confirmar (internet pode oscilar)

            if (s.Online != false)
            {
                // queda confirmada: conta desde a primeira falha da sequência
                s.Online = false;
                s.ForaDesde = agora - Intervalo * (FalhasParaAlerta - 1);
                await RegistrarQuedaAsync(s);
            }
            else if (EnviaWhatsApp && _ultimoWhatsApp.TryGetValue(s.Chave, out var ultimo) && agora - ultimo >= LembreteWhatsApp)
            {
                // continua fora: lembrete a cada 2 horas
                await EnviarWhatsAppAsync(s.Chave,
                    $"🔴 *{s.Nome} continua fora do ar*\n" +
                    $"{s.Host}:{s.Porta} sem resposta desde {s.ForaDesde:dd/MM HH:mm} ({FormatarDuracao(agora - s.ForaDesde!.Value)}).");
            }
        }
        finally
        {
            _trava.Release();
        }
    }

    private static async Task<(bool Ok, long Ms, string? Erro)> TestarConexaoAsync(string host, int porta, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TempoLimiteConexao);
        var sw = Stopwatch.StartNew();
        try
        {
            await tcp.ConnectAsync(host, porta, limite.Token);
            return (true, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, 0, $"Sem resposta em {TempoLimiteConexao.TotalSeconds:0}s");
        }
        catch (SocketException ex)
        {
            return (false, 0, ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "Conexão recusada (serviço FTP parado?)",
                SocketError.TimedOut => "Tempo esgotado",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable => "Servidor inacessível",
                _ => ex.Message
            });
        }
    }

    private async Task RegistrarQuedaAsync(ServidorStatus s)
    {
        var desde = s.ForaDesde ?? DateTime.Now;
        s.UltimasQuedas.Insert(0, new ServidorQueda(desde, null, s.UltimoErro));
        if (s.UltimasQuedas.Count > 10) s.UltimasQuedas.RemoveRange(10, s.UltimasQuedas.Count - 10);

        LogHelper.Log($"[MONITOR SRV] FORA DO AR: {s.Nome} ({s.Chave}) — {s.UltimoErro}");
        _health.Log(LogCategory.Sistema, LogSeverity.Critical, $"{s.Nome} fora do ar ({s.Chave})", s.UltimoErro);

        if (GravaNoBanco)
        try
        {
            await using var con = CriarConexao();
            await con.OpenAsync();
            await using var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT 1 FROM MonitorServidorQueda WHERE Host = @H AND Porta = @P AND Fim IS NULL)
                    INSERT INTO MonitorServidorQueda (Servidor, Host, Porta, Inicio, Erro)
                    VALUES (@S, @H, @P, @I, @E)", con);
            cmd.Parameters.AddWithValue("@S", s.Nome);
            cmd.Parameters.AddWithValue("@H", s.Host);
            cmd.Parameters.AddWithValue("@P", s.Porta);
            cmd.Parameters.AddWithValue("@I", desde);
            cmd.Parameters.AddWithValue("@E", (object?)s.UltimoErro ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] Gravar queda: {ex.Message}"); }

        Notificar(s);

        await EnviarWhatsAppAsync(s.Chave,
            $"🔴 *{s.Nome} FORA DO AR*\n" +
            $"{s.Host}:{s.Porta} não responde desde {desde:dd/MM HH:mm} ({FalhasParaAlerta} tentativas seguidas).\n" +
            $"Motivo: {s.UltimoErro}\n" +
            "Verifique se o servidor está ligado e o FTP ativo.");
    }

    private async Task RegistrarVoltaAsync(ServidorStatus s, DateTime desde, DateTime agora)
    {
        var aberta = s.UltimasQuedas.FirstOrDefault(q => q.Fim == null);
        if (aberta != null)
            s.UltimasQuedas[s.UltimasQuedas.IndexOf(aberta)] = aberta with { Fim = agora };

        LogHelper.Log($"[MONITOR SRV] VOLTOU: {s.Nome} ({s.Chave}) — fora {FormatarDuracao(agora - desde)}");
        _health.Log(LogCategory.Sistema, LogSeverity.Success, $"{s.Nome} voltou ({s.Chave})",
            $"Ficou fora {FormatarDuracao(agora - desde)}");

        if (GravaNoBanco)
        try
        {
            await using var con = CriarConexao();
            await con.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE MonitorServidorQueda SET Fim = @F WHERE Host = @H AND Porta = @P AND Fim IS NULL", con);
            cmd.Parameters.AddWithValue("@F", agora);
            cmd.Parameters.AddWithValue("@H", s.Host);
            cmd.Parameters.AddWithValue("@P", s.Porta);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] Gravar volta: {ex.Message}"); }

        Notificar(s);

        await EnviarWhatsAppAsync(s.Chave,
            $"✅ *{s.Nome} voltou*\n" +
            $"{s.Host}:{s.Porta} respondendo de novo. Ficou fora {FormatarDuracao(agora - desde)} " +
            $"({desde:dd/MM HH:mm} → {agora:HH:mm}).");
        _ultimoWhatsApp.Remove(s.Chave);
    }

    private void Notificar(ServidorStatus s)
    {
        var handlers = OnMudanca;
        if (handlers == null) return;
        foreach (Action<ServidorStatus> h in handlers.GetInvocationList())
        {
            try { h(s); }
            catch { /* uma tela com problema não pode impedir as outras */ }
        }
    }

    public static string FormatarDuracao(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "menos de 1 min";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
        if (t.TotalDays < 1) return $"{(int)t.TotalHours}h{t.Minutes:00}";
        return $"{(int)t.TotalDays}d {t.Hours}h";
    }

    // ── WhatsApp ────────────────────────────────────────────────────────────

    private async Task EnviarWhatsAppAsync(string chave, string texto)
    {
        if (!EnviaWhatsApp) return;
        _ultimoWhatsApp[chave] = DateTime.Now;

        try
        {
            var codInstancia = await ObterInstanciaAsync();
            if (codInstancia <= 0)
            {
                LogHelper.Log("[MONITOR SRV] WhatsApp não enviado: nenhuma instância conectada.");
                return;
            }

            foreach (var numero in NumerosWhatsApp())
            {
                try
                {
                    var payload = new
                    {
                        contact_phone_number = new string(numero.Where(char.IsDigit).ToArray()),
                        message_body = texto,
                        message_custom_id = $"MONITOR_{DateTime.Now:yyyyMMddHHmmss}_{numero[^4..]}",
                        cod_instancia = codInstancia
                    };
                    using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    using var resp = await _http.PostAsync($"{BaileysBase}/enviar-mensagem", content);
                    if (!resp.IsSuccessStatusCode)
                        LogHelper.Log($"[MONITOR SRV] WhatsApp {numero}: HTTP {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
                }
                catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] WhatsApp {numero}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { LogHelper.Log($"[MONITOR SRV] WhatsApp: {ex.Message}"); }
    }

    private async Task<int> ObterInstanciaAsync()
    {
        var configurada = _config.GetValue<int>("MonitorServidores:CodInstanciaWhatsApp");
        if (configurada > 0) return configurada;

        // instância de suporte conectada; senão, qualquer uma conectada
        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT TOP 1 Codigo FROM WhatsAppInstancia
            WHERE Status = 'conectado'
            ORDER BY CASE WHEN UPPER(Nome) LIKE '%SUPORTE%' THEN 0 ELSE 1 END, Codigo", con);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? 0 : Convert.ToInt32(r);
    }

    // ── Banco (histórico de quedas) ─────────────────────────────────────────

    private async Task GarantirTabelaAsync()
    {
        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var cmd = new SqlCommand(@"
            IF OBJECT_ID('dbo.MonitorServidorQueda', 'U') IS NULL
            CREATE TABLE dbo.MonitorServidorQueda (
                Codigo   INT IDENTITY(1,1) PRIMARY KEY,
                Servidor NVARCHAR(100) NOT NULL,
                Host     VARCHAR(100)  NOT NULL,
                Porta    INT           NOT NULL,
                Inicio   DATETIME      NOT NULL,
                Fim      DATETIME      NULL,
                Erro     NVARCHAR(300) NULL
            )", con);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CarregarQuedasAsync()
    {
        await using var con = CriarConexao();
        await con.OpenAsync();

        foreach (var s in _status.Values)
        {
            await using var cmd = new SqlCommand(@"
                SELECT TOP 10 Inicio, Fim, Erro FROM MonitorServidorQueda
                WHERE Host = @H AND Porta = @P
                ORDER BY Inicio DESC", con);
            cmd.Parameters.AddWithValue("@H", s.Host);
            cmd.Parameters.AddWithValue("@P", s.Porta);
            await using var rd = await cmd.ExecuteReaderAsync();
            var lista = new List<ServidorQueda>();
            while (await rd.ReadAsync())
                lista.Add(new ServidorQueda(rd.GetDateTime(0),
                    rd.IsDBNull(1) ? null : rd.GetDateTime(1),
                    rd.IsDBNull(2) ? null : rd.GetString(2)));
            s.UltimasQuedas = lista;

            // HELP reiniciou com o servidor fora: continua contando do início da queda
            var aberta = lista.FirstOrDefault(q => q.Fim == null);
            if (aberta != null)
            {
                s.Online = false;
                s.ForaDesde = aberta.Inicio;
                s.FalhasSeguidas = FalhasParaAlerta;
                s.UltimoErro = aberta.Erro;
                _ultimoWhatsApp[s.Chave] = DateTime.Now; // não repete o alerta só porque o HELP reiniciou
            }
        }
    }
}
