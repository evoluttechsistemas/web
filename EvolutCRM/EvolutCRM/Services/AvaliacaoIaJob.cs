using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Text;

namespace EvolutCRM.Services;

/// <summary>
/// Prova automática da IA: pega casos com solução CONFIRMADA, faz a pergunta do caso para a IA
/// (sem deixar ela ver o próprio caso na busca) e um "juiz" compara com a solução que resolveu.
/// Dá a porcentagem de acerto para comparar antes/depois de cada ajuste.
/// Iniciar: /api/ia/avaliar?key=...&limite=50   Acompanhar: /api/ia/avaliar/status?key=...
/// </summary>
public sealed class AvaliacaoIaJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cs;
    private readonly object _trava = new();

    public bool Rodando { get; private set; }
    private int _total, _feitos, _certas, _parciais, _erradas, _falhas;
    private DateTime? _inicio, _fim;
    private string _ultimoErro = "";

    // acerto por confiança: [confianca] = (qtd, certas)
    private readonly Dictionary<string, (int Qtd, int Certas)> _porConfianca = new();
    private readonly List<string> _exemplosErrados = new();

    public AvaliacaoIaJob(IServiceScopeFactory scopeFactory, IConfiguration config)
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
            _total = _feitos = _certas = _parciais = _erradas = _falhas = 0;
            _porConfianca.Clear();
            _exemplosErrados.Clear();
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
            var casos = new List<(int Ticket, int CodEmp, string Pergunta, string Solucao)>();
            await using (var conn = new SqlConnection(_cs))
            {
                await conn.OpenAsync();
                // Sorteia casos confirmados (um por ticket)
                await using var cmd = new SqlCommand(@"
SELECT TOP (@Limite) X.CodTicketChamadoC, X.CodEmp, X.Resumo, X.Solucao
FROM (
    SELECT I.CodTicketChamadoC, ISNULL(T.CodEmp, 0) AS CodEmp, I.Resumo, I.Solucao,
           ROW_NUMBER() OVER (PARTITION BY I.CodTicketChamadoC ORDER BY I.TopicoIndex) AS N
    FROM dbo.AiTicketInsights I
    LEFT JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
    WHERE I.SolucaoConfirmada = 1
      AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
      AND ISNULL(LTRIM(RTRIM(I.Resumo)), '') <> ''
) X
WHERE X.N = 1
ORDER BY NEWID();", conn);
                cmd.Parameters.AddWithValue("@Limite", limite);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    casos.Add((rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3)));
            }

            _total = casos.Count;

            foreach (var caso in casos)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ai = scope.ServiceProvider.GetRequiredService<AiService>();

                    var r = await ai.SugerirAsync(new AiPedidoSugestao
                    {
                        CodTicket = caso.Ticket,
                        CodEmp = caso.CodEmp,
                        MensagensCliente = caso.Pergunta,
                        IgnorarTicketNaBusca = caso.Ticket,
                        Origem = "avaliacao",
                        Usuario = "avaliacao"
                    });

                    if (!r.Ok)
                    {
                        _falhas++;
                        _ultimoErro = $"Ticket #{caso.Ticket}: {r.Resposta}";
                    }
                    else
                    {
                        var (nota, motivo) = await ai.JulgarRespostaAsync(caso.Pergunta, caso.Solucao, r.Resposta);
                        if (nota < 0) { _falhas++; _ultimoErro = $"Juiz, ticket #{caso.Ticket}: {motivo}"; }
                        else
                        {
                            if (nota == 2) _certas++;
                            else if (nota == 1) _parciais++;
                            else
                            {
                                _erradas++;
                                if (_exemplosErrados.Count < 15)
                                    _exemplosErrados.Add($"#{caso.Ticket} [{r.Confianca}] {Curto(caso.Pergunta, 90)} → {motivo}");
                            }

                            lock (_trava)
                            {
                                _porConfianca.TryGetValue(r.Confianca, out var pc);
                                _porConfianca[r.Confianca] = (pc.Qtd + 1, pc.Certas + (nota == 2 ? 1 : 0));
                            }

                            await GravarNotaAsync(r.CodLog, nota, motivo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _falhas++;
                    _ultimoErro = $"Ticket #{caso.Ticket}: {ex.Message}";
                    LogHelper.Log("[AVALIAÇÃO IA] " + _ultimoErro);
                }
                _feitos++;
                await Task.Delay(300); // não estourar o limite da OpenAI
            }
        }
        catch (Exception ex)
        {
            _ultimoErro = ex.Message;
            LogHelper.Log("[AVALIAÇÃO IA] Erro geral: " + ex.Message);
        }
        finally
        {
            _fim = DateTime.Now;
            Rodando = false;
            LogHelper.Log("[AVALIAÇÃO IA] Fim\n" + Status());
        }
    }

    private async Task GravarNotaAsync(long codLog, int nota, string motivo)
    {
        if (codLog <= 0) return;
        try
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE AiSugestaoLog SET NotaAvaliacao = @n, MotivoAvaliacao = @m WHERE CodAiSugestaoLog = @c", conn);
            cmd.Parameters.AddWithValue("@n", nota);
            cmd.Parameters.AddWithValue("@m", Curto(motivo, 500));
            cmd.Parameters.AddWithValue("@c", codLog);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogHelper.Log("[AVALIAÇÃO IA] Não gravou a nota: " + ex.Message);
        }
    }

    private static string Curto(string s, int max)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "...";
    }

    private static string Pct(int a, int b) => b == 0 ? "0%" : $"{100.0 * a / b:F1}%";

    public string Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Rodando ? "Rodando..." : _inicio == null ? "Ainda não foi iniciado." : "Terminado.");
        if (_inicio == null) return sb.ToString();

        int julgadas = _certas + _parciais + _erradas;
        sb.AppendLine($"Início: {_inicio:dd/MM HH:mm:ss}" + (_fim != null ? $" | Fim: {_fim:dd/MM HH:mm:ss}" : ""));
        sb.AppendLine($"Casos: {_feitos} de {_total} | julgados: {julgadas} | falhas: {_falhas}");
        sb.AppendLine();
        sb.AppendLine($"ACERTO (resposta certa):        {Pct(_certas, julgadas)}  ({_certas})");
        sb.AppendLine($"Parcial (direção certa):        {Pct(_parciais, julgadas)}  ({_parciais})");
        sb.AppendLine($"Errada:                         {Pct(_erradas, julgadas)}  ({_erradas})");
        sb.AppendLine($"Certa + parcial:                {Pct(_certas + _parciais, julgadas)}");
        sb.AppendLine();
        sb.AppendLine("Acerto por confiança (a confiança tem que bater com o acerto):");
        lock (_trava)
        {
            foreach (var k in new[] { "alta", "media", "baixa" })
                if (_porConfianca.TryGetValue(k, out var v))
                    sb.AppendLine($"  {k,-6}: {v.Qtd} casos, {Pct(v.Certas, v.Qtd)} certas");
        }
        if (_ultimoErro.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Última falha: " + _ultimoErro);
        }
        if (_exemplosErrados.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Exemplos de erradas (para olhar a documentação/insight):");
            foreach (var e in _exemplosErrados) sb.AppendLine("  " + e);
        }
        return sb.ToString();
    }
}
