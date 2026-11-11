using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

/// <summary>
/// Qualidade das respostas: lacunas (assuntos sem evidência) e gabarito (perguntas com resposta esperada).
/// Lacunas guardam só as palavras-chave da busca — nunca o texto do cliente, nomes ou documentos.
/// </summary>
public sealed record GapEntry(string Key, string[] Terms, string Module, int Count, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);
public sealed record MonthStats(int Answered, int Insufficient);
public sealed record GapReport(Dictionary<string, MonthStats> Months, List<GapEntry> Gaps);

public sealed record GabaritoResult(DateTimeOffset At, bool Correct, bool Evidence, string[] Missing, string Answer);
public sealed record GabaritoItem(Guid Id, string Question, string Expected, string Mode, GabaritoResult? Last = null);

public static class KnowledgeQuality
{
    public const string GapsFile = "knowledge-gaps.json";
    public const string GabaritoFile = "gabarito.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Palavras-chave seguras para registrar: sem números longos (documentos, telefones, códigos de cliente).</summary>
    public static string[] SafeTerms(string question) =>
        KnowledgeSearch.Terms(question).Where(t => !Regex.IsMatch(t, @"\d{4,}")).Take(6).ToArray();

    public static async Task RecordAsync(PanelStore store, string retrievalQuestion, bool sufficient)
    {
        await Gate.WaitAsync();
        try
        {
            var report = await store.ReadAsync<GapReport>(GapsFile) ?? new(new(), []);
            var month = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM");
            var stats = report.Months.GetValueOrDefault(month) ?? new(0, 0);
            report.Months[month] = sufficient ? stats with { Answered = stats.Answered + 1 } : stats with { Insufficient = stats.Insufficient + 1 };
            if (!sufficient)
            {
                var terms = SafeTerms(retrievalQuestion);
                if (terms.Length > 0)
                {
                    var key = string.Join(" ", terms.Order());
                    var now = DateTimeOffset.UtcNow;
                    var index = report.Gaps.FindIndex(g => g.Key == key);
                    if (index >= 0) report.Gaps[index] = report.Gaps[index] with { Count = report.Gaps[index].Count + 1, LastSeen = now };
                    else report.Gaps.Add(new(key, terms, KnowledgeSearch.ModuleOf(retrievalQuestion), 1, now, now));
                }
            }
            // Mantém os 300 assuntos mais recentes e 24 meses de contagem.
            var trimmed = new GapReport(report.Months.OrderByDescending(m => m.Key).Take(24).ToDictionary(m => m.Key, m => m.Value),
                report.Gaps.OrderByDescending(g => g.LastSeen).Take(300).ToList());
            await store.WriteAsync(GapsFile, trimmed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        finally { Gate.Release(); }
    }

    public static async Task RemoveGapAsync(PanelStore store, string key)
    {
        await Gate.WaitAsync();
        try
        {
            var report = await store.ReadAsync<GapReport>(GapsFile);
            if (report is null) return;
            report.Gaps.RemoveAll(g => g.Key == key);
            await store.WriteAsync(GapsFile, report);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Acertou = teve evidência e a resposta contém todos os termos esperados (separados por vírgula).</summary>
    public static GabaritoResult Evaluate(SimulationAnswer answer, string expected)
    {
        var text = KnowledgeSearch.Normalize(string.Join(" ", new[] { answer.Answer, answer.FollowUp }.Concat(answer.Steps ?? [])));
        var missing = expected.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Length > 0 && !text.Contains(KnowledgeSearch.Normalize(e), StringComparison.Ordinal)).ToArray();
        return new(DateTimeOffset.UtcNow, answer.SufficientEvidence && missing.Length == 0, answer.SufficientEvidence, missing,
            ExternalText.Clean(answer.Answer, 800));
    }

    public static async Task<List<GabaritoItem>> GetGabaritoAsync(PanelStore store) =>
        await store.ReadAsync<List<GabaritoItem>>(GabaritoFile) ?? [];

    public static async Task SaveGabaritoAsync(PanelStore store, List<GabaritoItem> items)
    {
        await Gate.WaitAsync();
        try { await store.WriteAsync(GabaritoFile, items); }
        finally { Gate.Release(); }
    }
}
