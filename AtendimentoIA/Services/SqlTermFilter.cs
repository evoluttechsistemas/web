using System.Data;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

/// <summary>
/// Monta o filtro e a nota de relevância das consultas SQL a partir dos grupos de termos
/// (termo + raiz + sinônimos). Tudo vai por parâmetro: nenhum texto do cliente entra no SQL.
/// </summary>
public sealed class SqlTermFilter
{
    private readonly IReadOnlyList<string[]> groups;
    public SqlTermFilter(IReadOnlyList<string[]> groups) =>
        this.groups = groups.Where(g => g.Length > 0).Take(8).Select(WithFiscalSpellings).ToList();

    // No banco o texto está como "NFC-e"/"NF-e": a busca por "nfce"/"nfe" também procura com hífen.
    private static string[] WithFiscalSpellings(string[] group) =>
        group.Concat(group.Select(t => System.Text.RegularExpressions.Regex.Match(t, "^(nfc|nfs|nf|ct|mdf|cf)e$"))
                .Where(m => m.Success).Select(m => m.Groups[1].Value + "-e"))
            .Distinct().ToArray();
    public int Count => groups.Count;

    private string Any(int group, string expression) =>
        "(" + string.Join(" OR ", groups[group].Select((_, j) => $"{expression} COLLATE Latin1_General_CI_AI LIKE @T{group}_{j}")) + ")";

    /// <summary>Verdadeiro quando qualquer grupo aparece em alguma das expressões.</summary>
    public string Matches(params string[] expressions) =>
        "(" + string.Join(" OR ", Enumerable.Range(0, Count).Select(i => string.Join(" OR ", expressions.Select(e => Any(i, e))))) + ")";

    /// <summary>Nota: cada grupo soma o peso da primeira expressão em que aparece (título antes do conteúdo).</summary>
    public string Score(params (string Expression, int Weight)[] parts) =>
        "(" + string.Join(" + ", Enumerable.Range(0, Count).Select(i =>
            "CASE " + string.Join(" ", parts.Select(p => $"WHEN {Any(i, p.Expression)} THEN {p.Weight}")) + " ELSE 0 END")) + ")";

    /// <summary>Quantos grupos diferentes aparecem (para exigir um mínimo de termos em comum).</summary>
    public string MatchedGroups(params string[] expressions) =>
        "(" + string.Join(" + ", Enumerable.Range(0, Count).Select(i =>
            $"CASE WHEN {string.Join(" OR ", expressions.Select(e => Any(i, e)))} THEN 1 ELSE 0 END")) + ")";

    public void AddParameters(SqlCommand command)
    {
        for (var i = 0; i < Count; i++)
            for (var j = 0; j < groups[i].Length; j++)
                command.Parameters.Add($"@T{i}_{j}", SqlDbType.NVarChar, 250).Value = "%" + Escape(groups[i][j]) + "%";
    }

    // % _ [ digitados pelo cliente não podem virar curinga do LIKE.
    private static string Escape(string value) => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
