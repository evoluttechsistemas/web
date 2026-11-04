using System.Collections.Concurrent;

namespace EvolutCRM.Services;

/// <summary>
/// Trava de gasto da IA nas rotinas automáticas (as que rodam sem ninguém clicar).
/// Cada rotina tem um máximo de chamadas por dia; passou do máximo, ela para até o dia seguinte
/// e registra no log. Assim um erro de regra (ex.: varredura pegando tickets demais) não gasta todo o crédito.
/// O contador fica em memória: reiniciar o HELP zera a contagem do dia.
/// </summary>
public static class LimiteDiarioIa
{
    public const string Insight = "INSIGHT";               // insight automático (fila + madrugada)
    public const string Documentacao = "DOCUMENTACAO";     // documentação interna automática (por ticket)
    public const string Faxina = "FAXINA";                 // junção de documentações do mesmo assunto (por par)
    public const string AnaliseContexto = "ANALISE";       // assunto/sentimento da mensagem do cliente
    public const string ResumoTransferencia = "RESUMO";    // resumo ao transferir o ticket

    private static readonly Dictionary<string, int> Maximos = new()
    {
        [Insight] = 80,
        [Documentacao] = 40,
        [Faxina] = 20,
        [AnaliseContexto] = 800,
        [ResumoTransferencia] = 150,
    };

    private static readonly object _trava = new();
    private static DateTime _dia = DateTime.Today;
    private static readonly Dictionary<string, int> _uso = new();
    private static readonly ConcurrentDictionary<string, bool> _avisado = new();

    /// <summary>Reserva uma chamada da rotina. Devolve false quando o máximo do dia já foi atingido.</summary>
    public static bool Consumir(string rotina)
    {
        lock (_trava)
        {
            if (_dia != DateTime.Today)
            {
                _dia = DateTime.Today;
                _uso.Clear();
                _avisado.Clear();
            }

            var max = Maximos.TryGetValue(rotina, out var m) ? m : 50;
            _uso.TryGetValue(rotina, out var usado);
            if (usado >= max)
            {
                if (_avisado.TryAdd(rotina, true))
                    LogHelper.Log($"[LIMITE IA] {rotina}: atingiu o máximo de {max} chamadas hoje; a rotina para até amanhã.");
                return false;
            }
            _uso[rotina] = usado + 1;
            return true;
        }
    }

    /// <summary>Uso de hoje, para conferência (ex.: "INSIGHT 12/80 | DOCUMENTACAO 5/40").</summary>
    public static string Resumo()
    {
        lock (_trava)
        {
            if (_dia != DateTime.Today) return "nenhum uso hoje";
            return string.Join(" | ", Maximos.Select(kv => $"{kv.Key} {(_uso.TryGetValue(kv.Key, out var u) ? u : 0)}/{kv.Value}"));
        }
    }
}
