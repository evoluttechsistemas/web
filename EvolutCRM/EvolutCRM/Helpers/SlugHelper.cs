using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Helpers;

public static class SlugHelper
{
    public static string GerarSlug(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalizado)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return Regex.Replace(
            sb.ToString().Normalize(NormalizationForm.FormC).ToLower(),
            @"[^a-z0-9\s-]", "")
            .Trim()
            .Replace(" ", "-");
    }
}