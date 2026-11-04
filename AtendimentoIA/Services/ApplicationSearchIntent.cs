namespace EvolutTech.IA.Painel.Services;

public static class ApplicationSearchIntent
{
    public static bool IsConfiguration(string question)=>System.Text.RegularExpressions.Regex.IsMatch(KnowledgeSearch.Normalize(question),@"\b(configur\w*|parametr\w*)\b");
    public static string? PreferredForm(string question)
    {
        var terms=KnowledgeSearch.Terms(question);
        return terms.Any(t=>t.StartsWith("produto",StringComparison.Ordinal))&&(terms.Contains("cadastro")||terms.Contains("balanca"))?"CadProduto":null;
    }
    public static int FormBoost(string path,string question)=>PreferredForm(question) is { } form&&
        Path.GetFileName(path).Replace(".Designer.cs","",StringComparison.OrdinalIgnoreCase).Replace(".cs","",StringComparison.OrdinalIgnoreCase).Equals(form,StringComparison.OrdinalIgnoreCase)?60:0;
}
