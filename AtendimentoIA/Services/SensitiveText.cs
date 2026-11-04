using System.Text.RegularExpressions;
namespace EvolutTech.IA.Painel.Services;

// Best-effort display masking, not approval for external AI transmission. Raw content stays in SQL.
public static class SensitiveText
{
    public static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?im)\b(password|pwd|senha|token|api[_-]?key|secret|authorization)\s*[:=]\s*[^;\r\n<]+", "$1=[oculto]", RegexOptions.None, TimeSpan.FromSeconds(1));
        value = Regex.Replace(value, @"(?i)\bBearer\s+[A-Za-z0-9_.~+/=-]+", "Bearer [oculto]", RegexOptions.None, TimeSpan.FromSeconds(1));
        value = Regex.Replace(value, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "[e-mail oculto]", RegexOptions.None, TimeSpan.FromSeconds(1));
        return value;
    }
}
