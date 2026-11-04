using System.Collections.Concurrent;
using System.Threading.Channels;
using EvolutCRM.Data;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Resumo ao transferir: quando o responsável do ticket passa para outro atendente, a IA escreve um resumo curto
/// (problema, o que já foi feito, próximo passo) e grava como nota interna no ticket, para quem recebe
/// não precisar ler a conversa inteira.
///  - Só quando alguém transfere para OUTRA pessoa (assumir o ticket para si não gera resumo).
///  - Só com conversa de verdade (pelo menos 3 mensagens com texto); ticket finalizado não gera.
///  - Modelo barato (OpenAI:Model, gpt-4.1-mini), em segundo plano, com trava diária (LimiteDiarioIa).
/// Parâmetro TICKET_RESUMO_TRANSFERENCIA (S/N, padrão S), grupo TICKET.
/// </summary>
public sealed class ResumoTransferenciaService : BackgroundService
{
    public const string Parametro = "TICKET_RESUMO_TRANSFERENCIA";
    private const string ParametroDescricao =
        "Ao transferir o ticket para outro atendente, a IA grava uma nota interna com o resumo da conversa " +
        "(problema, o que ja foi feito e proximo passo). S=SIM N=NAO. PADRAO=S";
    private const string UsuarioIa = "IA";
    private const int MinimoMensagens = 3;

    private static readonly HashSet<string> NaoSaoPessoas = new(StringComparer.OrdinalIgnoreCase)
    { "", "NOVO", "WHATSAPP", "CLIENTE", "BOT", "SISTEMA", "IA", "AUTOMATICO" };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cs;
    private readonly Channel<Pedido> _fila =
        Channel.CreateBounded<Pedido>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    // mesmo ticket transferido várias vezes seguidas: um resumo a cada 10 min no máximo
    private readonly ConcurrentDictionary<int, DateTime> _ultimoResumo = new();

    private sealed record Pedido(int Ticket, int CodEmp, string De, string Para, string Por);

    public ResumoTransferenciaService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _cs = config.GetConnectionString("Connection")!;
    }

    /// <summary>
    /// Chamado pelo TicketService depois de salvar. Decide sozinho se é transferência que merece resumo.
    /// Não espera nada (a IA roda em segundo plano).
    /// </summary>
    public void TalvezResumir(int ticket, int codEmp, string? de, string? para, string? por, int codSituacao)
    {
        de = (de ?? "").Trim().ToUpperInvariant();
        para = (para ?? "").Trim().ToUpperInvariant();
        por = (por ?? "").Trim().ToUpperInvariant();

        if (ticket <= 0 || codEmp <= 0 || codSituacao == 3) return;
        if (de == para) return;                       // não mudou o responsável
        if (NaoSaoPessoas.Contains(para)) return;      // foi para fila/NOVO, não para uma pessoa
        if (para == por) return;                       // a pessoa assumiu o ticket para si

        _fila.Writer.TryWrite(new Pedido(ticket, codEmp, de, para, por));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var p in _fila.Reader.ReadAllAsync(ct))
            {
                try { await ResumirAsync(p, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { LogHelper.Log($"[RESUMO TRANSF] Ticket #{p.Ticket}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ResumirAsync(Pedido p, CancellationToken ct)
    {
        var agora = DateTime.Now;
        if (_ultimoResumo.TryGetValue(p.Ticket, out var ultimo) && agora - ultimo < TimeSpan.FromMinutes(10))
            return;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        if (!await sp.GetRequiredService<ParametroDinamicoService>()
                .BuscarBoolAsync(p.CodEmp, Parametro, "TICKET", ParametroDescricao, true))
            return;

        // Conversa curta não precisa de resumo (e não gasta IA)
        if (await ContarMensagensAsync(p.Ticket, ct) < MinimoMensagens)
            return;

        if (!LimiteDiarioIa.Consumir(LimiteDiarioIa.ResumoTransferencia))
            return;

        var conversa = (await sp.GetRequiredService<TicketQueryRepository>()
            .MontarParaInsightAsync(p.Ticket, p.CodEmp, 10000, incluirImagens: false)).Texto;
        if (string.IsNullOrWhiteSpace(conversa)) return;

        var resumo = await sp.GetRequiredService<OpenAiClient>().ResumirParaTransferenciaAsync(conversa);
        if (string.IsNullOrWhiteSpace(resumo)) return;

        var por = string.IsNullOrWhiteSpace(p.Por) ? "" : $" por {p.Por}";
        var nota = $"📋 Resumo para {p.Para} (transferido{por}):\n{resumo.Trim()}";
        await GravarNotaAsync(p.Ticket, p.CodEmp, nota, ct);

        _ultimoResumo[p.Ticket] = agora;
        if (_ultimoResumo.Count > 2000)
            foreach (var k in _ultimoResumo.Where(x => agora - x.Value > TimeSpan.FromHours(1)).Select(x => x.Key).ToList())
                _ultimoResumo.TryRemove(k, out _);

        LogHelper.Log($"[RESUMO TRANSF] Ticket #{p.Ticket}: resumo gravado para {p.Para}.");
    }

    private async Task<int> ContarMensagensAsync(int ticket, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SELECT COUNT(*)
FROM TicketChamadoD D
WHERE D.CodTicketChamadoC = @t
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND ISNULL(D.Interno, 'N') <> 'S'
  AND LEN(LTRIM(RTRIM(ISNULL(NULLIF(D.Anotacao, ''), ISNULL(D.TranscricaoAudio, ''))))) > 0;", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task GravarNotaAsync(int ticket, int codEmp, string nota, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
INSERT INTO TicketChamadoD
    (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
     TelefoneWhatsApp, CodInstancia, LidoSuporte, EnvioCliente, LidoCliente, StatusWhatsApp, Interno)
VALUES
    (@e, @t, @nota, GETDATE(), @u, NULL, NULL, 'S', 'N', NULL, 'interno', 'S');", conn);
        cmd.Parameters.AddWithValue("@e", codEmp);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.Add("@nota", System.Data.SqlDbType.NVarChar, -1).Value = nota;
        cmd.Parameters.AddWithValue("@u", UsuarioIa);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
