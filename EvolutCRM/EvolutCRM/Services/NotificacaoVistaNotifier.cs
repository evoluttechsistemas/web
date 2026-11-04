using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Singleton: quando alguém abre um ticket ou card, avisa as telas de TODOS os usuários
/// da empresa para tirar esse item do painel de notificações (ticket novo e card novo
/// aparecem para todos; quando um vê, some para todos).
/// Também guarda por algumas horas quando cada item foi visto, para a verificação
/// periódica não colocar o aviso de volta sem ter chegado mensagem nova.
/// </summary>
public class NotificacaoVistaNotifier
{
    public const string Ticket = "TICKET";
    public const string Card = "CARD";

    /// <summary>(codEmp, tipo, codigo)</summary>
    public event Action<int, string, int>? OnVisto;

    private readonly ConcurrentDictionary<string, DateTime> _vistos = new();
    private DateTime _ultimaLimpeza = DateTime.Now;

    // ── Relógio do banco ──
    // A hora das mensagens (DataHoraUltimaGravacao) vem do GETDATE() do SQL Server. Se o relógio do SQL e o do
    // servidor do HELP não batem, "visto às" x "mensagem às" dava errado (mensagem tratada como já vista).
    // Guarda a diferença entre os dois e usa a hora do banco em tudo que é comparado com a hora das mensagens.
    private readonly string? _cs;
    private TimeSpan _diferencaBanco = TimeSpan.Zero;
    private DateTime _ultimoAcertoRelogio = DateTime.MinValue;
    private int _acertandoRelogio;

    public NotificacaoVistaNotifier(IConfiguration config)
    {
        _cs = config.GetConnectionString("Connection");
        AcertarRelogioEmSegundoPlano();
    }

    /// <summary>Hora atual no relógio do SQL Server (acertada a cada 10 min).</summary>
    public DateTime AgoraBanco()
    {
        if (DateTime.Now - _ultimoAcertoRelogio > TimeSpan.FromMinutes(10))
            AcertarRelogioEmSegundoPlano();
        return DateTime.Now + _diferencaBanco;
    }

    private void AcertarRelogioEmSegundoPlano()
    {
        if (string.IsNullOrWhiteSpace(_cs)) return;
        if (Interlocked.Exchange(ref _acertandoRelogio, 1) == 1) return;
        _ultimoAcertoRelogio = DateTime.Now;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new SqlConnection(_cs);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("SELECT GETDATE();", conn);
                var antes = DateTime.Now;
                var banco = Convert.ToDateTime(await cmd.ExecuteScalarAsync());
                var depois = DateTime.Now;
                var meio = antes + TimeSpan.FromTicks((depois - antes).Ticks / 2);
                _diferencaBanco = banco - meio;
                if (Math.Abs(_diferencaBanco.TotalSeconds) >= 30)
                    LogHelper.Log($"[NOTIFICACOES] Relógio do SQL difere do servidor do HELP em {_diferencaBanco.TotalSeconds:0}s (compensado).");
            }
            catch
            {
                // sem acerto: segue com a última diferença conhecida
            }
            finally
            {
                Interlocked.Exchange(ref _acertandoRelogio, 0);
            }
        });
    }

    private static string Chave(int codEmp, string tipo, int codigo) => $"{codEmp}|{tipo}|{codigo}";

    public void MarcarVisto(int codEmp, string tipo, int codigo)
    {
        if (codigo <= 0) return;

        var agora = AgoraBanco();   // comparado com a hora das mensagens (relógio do banco)
        var chave = Chave(codEmp, tipo, codigo);

        // a mesma tela navegando de novo para o mesmo item não precisa avisar todo mundo outra vez
        if (_vistos.TryGetValue(chave, out var anterior) && agora - anterior < TimeSpan.FromSeconds(5))
            return;

        _vistos[chave] = agora;
        LimparAntigos(agora);

        var handlers = OnVisto;
        if (handlers == null) return;

        foreach (Action<int, string, int> h in handlers.GetInvocationList())
        {
            try { h(codEmp, tipo, codigo); }
            catch { /* uma tela com problema não pode impedir as outras */ }
        }
    }

    /// <summary>Quando o item foi visto por último por alguém da empresa (null = não visto recentemente).</summary>
    public DateTime? VistoEm(int codEmp, string tipo, int codigo)
        => _vistos.TryGetValue(Chave(codEmp, tipo, codigo), out var quando) ? quando : null;

    private void LimparAntigos(DateTime agora)
    {
        if (agora - _ultimaLimpeza < TimeSpan.FromMinutes(30)) return;
        _ultimaLimpeza = agora;

        foreach (var kv in _vistos)
            if (agora - kv.Value > TimeSpan.FromHours(12))
                _vistos.TryRemove(kv.Key, out _);
    }
}
