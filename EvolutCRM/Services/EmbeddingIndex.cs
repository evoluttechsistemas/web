using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

public sealed class EmbeddingIndex
{
    private readonly string _cs;
    private volatile List<InsightVec> _insights = new();
    private volatile List<DocVec> _docs = new();
    public DateTime CarregadoEm { get; private set; }

    public EmbeddingIndex(IConfiguration config) => _cs = config.GetConnectionString("Connection")!;

    public sealed record InsightVec(int TicketId, int TopicoIndex, string? Resumo,
        string? Tags, string? Solucao, DateTime CriadoEm, float[] Vec);
    public sealed record DocVec(int CodDoc, string Slug, string Titulo, string Texto, float[] Vec);

    public async Task ReloadAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var ins = new List<InsightVec>();
        await using (var cmd = new SqlCommand(@"
        SELECT CodTicketChamadoC, TopicoIndex, Resumo, Tags, Solucao, CriadoEm, Embedding
        FROM dbo.AiTicketInsights
        WHERE Embedding IS NOT NULL
          AND Resumo IS NOT NULL AND LTRIM(RTRIM(Resumo)) <> '';", conn))
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
            {
                var vec = JsonSerializer.Deserialize<float[]>(rd["Embedding"]!.ToString()!)!;
                ins.Add(new InsightVec(
                    rd.GetInt32(0), rd["TopicoIndex"] is int t ? t : 1,
                    rd["Resumo"] as string, rd["Tags"] as string, rd["Solucao"] as string,
                    rd["CriadoEm"] is DateTime dt ? dt : DateTime.MinValue, vec));
            }

        var docs = new List<DocVec>();
        await using (var cmd2 = new SqlCommand(
            "SELECT CodDocumentacaoTutorial, Slug, Titulo, Texto, Embedding FROM dbo.DocChunk WHERE Embedding IS NOT NULL;", conn))
        await using (var rd2 = await cmd2.ExecuteReaderAsync())
            while (await rd2.ReadAsync())
            {
                var vec = JsonSerializer.Deserialize<float[]>(rd2["Embedding"]!.ToString()!)!;
                docs.Add(new DocVec(rd2.GetInt32(0), rd2["Slug"]?.ToString() ?? "",
                    rd2["Titulo"]?.ToString() ?? "", rd2["Texto"]?.ToString() ?? "", vec));
            }

        _insights = ins;
        _docs = docs;
        CarregadoEm = DateTime.UtcNow;
        Console.WriteLine($"[EmbeddingIndex] Carregado do banco: {_insights.Count} insights, {_docs.Count} docs");
    }

    public List<(InsightVec Item, float Score)> SearchInsights(float[] q, string query, int top)
    {
        var toks = Tokens(query);
        return _insights
            .Select(x => (x, Score: Hybrid(q, x.Vec, $"{x.Resumo} {x.Tags} {x.Solucao}", toks, query)))
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }

    public List<(DocVec Item, float Score)> SearchDocs(float[] q, string query, int top)
    {
        var toks = Tokens(query);
        return _docs
            .Select(x => (x, Score: Hybrid(q, x.Vec, $"{x.Titulo} {x.Texto}", toks, query)))
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }

    // cosseno (0.7) + sobreposição de palavras (0.3) + reforço de token exato importante
    private static float Hybrid(float[] q, float[] d, string texto, HashSet<string> qToks, string queryRaw)
    {
        float cos = Cosine(q, d);
        var dToks = Tokens(texto);
        float overlap = qToks.Count == 0 ? 0 : (float)qToks.Count(dToks.Contains) / qToks.Count;

        float boost = 0;
        foreach (var tk in TokensEspeciais(queryRaw))
            if (texto.Contains(tk, StringComparison.OrdinalIgnoreCase)) boost += 0.08f;

        return 0.7f * cos + 0.3f * overlap + Math.Min(boost, 0.2f);
    }

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9));
    }

    private static HashSet<string> Tokens(string s) =>
        Regex.Matches((s ?? "").ToLowerInvariant(), @"\b[\p{L}0-9]+\b")
             .Select(m => m.Value).Where(w => w.Length >= 3)
             .ToHashSet();

    // termos exatos que NÃO podem ser perdidos: teclas, F-keys, códigos fiscais
    private static IEnumerable<string> TokensEspeciais(string s)
    {
        foreach (Match m in Regex.Matches(s ?? "", @"\b(ctrl\s*\+?\s*\w|f\d{1,2}|cfop\s*\d{3,4}|cst\s*\d+|csosn\s*\d+|ncm\s*\d+)\b",
                 RegexOptions.IgnoreCase))
            yield return m.Value.Trim();
    }
}