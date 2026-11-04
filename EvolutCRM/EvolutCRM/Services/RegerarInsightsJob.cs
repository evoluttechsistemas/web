using Microsoft.Data.SqlClient;
using System.Text;

namespace EvolutCRM.Services;

/// <summary>
/// Regera, em segundo plano, os insights dos tickets em que NENHUM insight tem solução
/// (com o histórico completo e os prints, a IA costuma achar a solução que antes ficava de fora).
/// Iniciar: /api/ia/regerar-insights?key=...   Acompanhar: /api/ia/regerar-insights/status?key=...
/// </summary>
public sealed class RegerarInsightsJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cs;
    private readonly object _trava = new();

    public bool Rodando { get; private set; }
    private int _total, _feitos, _comSolucao, _erros;
    private DateTime? _inicio, _fim;
    private string _ultimoErro = "";

    public RegerarInsightsJob(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _cs = config.GetConnectionString("Connection")!;
    }

    public bool Iniciar(int limite)
    {
        lock (_trava)
        {
            if (Rodando) return false;
            Rodando = true;
            _total = _feitos = _comSolucao = _erros = 0;
            _inicio = DateTime.Now;
            _fim = null;
            _ultimoErro = "";
        }
        _ = Task.Run(() => RodarAsync(limite));
        return true;
    }

    private async Task RodarAsync(int limite)
    {
        try
        {
            var tickets = new List<(int Ticket, int CodEmp)>();
            await using (var conn = new SqlConnection(_cs))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(@"
SELECT TOP (@Limite) I.CodTicketChamadoC, T.CodEmp
FROM dbo.AiTicketInsights I
JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
GROUP BY I.CodTicketChamadoC, T.CodEmp
HAVING SUM(CASE WHEN ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> '' THEN 1 ELSE 0 END) = 0
ORDER BY I.CodTicketChamadoC DESC;", conn);
                cmd.Parameters.AddWithValue("@Limite", limite);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    tickets.Add((rd.GetInt32(0), rd.GetInt32(1)));
            }

            _total = tickets.Count;

            foreach (var (ticket, codEmp) in tickets)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var auto = scope.ServiceProvider.GetRequiredService<AiAutoLearningService>();
                    var insights = await auto.GenerateAndSaveManyAsync(ticket, force: true, codEmp: codEmp);
                    if (insights.Any(x => !string.IsNullOrWhiteSpace(x.solucao))) _comSolucao++;
                }
                catch (Exception ex)
                {
                    _erros++;
                    _ultimoErro = $"Ticket #{ticket}: {ex.Message}";
                    LogHelper.Log("[REGERAR INSIGHTS] " + _ultimoErro);
                }
                _feitos++;
                await Task.Delay(300); // não estourar o limite da OpenAI
            }
        }
        catch (Exception ex)
        {
            _ultimoErro = ex.Message;
            LogHelper.Log("[REGERAR INSIGHTS] Erro geral: " + ex.Message);
        }
        finally
        {
            _fim = DateTime.Now;
            Rodando = false;
            LogHelper.Log($"[REGERAR INSIGHTS] Fim: {_feitos}/{_total} tickets | agora com solução: {_comSolucao} | erros: {_erros}");
        }
    }

    public string Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Rodando ? "Rodando..." : _inicio == null ? "Ainda não foi iniciado." : "Terminado.");
        if (_inicio != null)
        {
            sb.AppendLine($"Início: {_inicio:dd/MM HH:mm:ss}" + (_fim != null ? $" | Fim: {_fim:dd/MM HH:mm:ss}" : ""));
            sb.AppendLine($"Tickets: {_feitos} de {_total}");
            sb.AppendLine($"Passaram a ter solução: {_comSolucao}");
            sb.AppendLine($"Erros: {_erros}" + (_ultimoErro.Length > 0 ? $" (último: {_ultimoErro})" : ""));
        }
        return sb.ToString();
    }
}
