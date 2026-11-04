using System.Globalization;
using System.Numerics;

namespace EvolutTech.IA.Painel.Services;

public static class ErpVersion
{
    // Compare numeric components rather than SQL MAX(varchar): 26.10 > 26.9.
    // Keep the original display format, including leading zeros.
    public static string Latest(IEnumerable<string?> versions)
    {
        string latest = "";
        BigInteger[]? best = null;
        foreach (var raw in versions)
        {
            var text = raw?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var parts = text.Split('.');
            if (parts.Any(p => p.Length == 0 || p.Any(c => c is < '0' or > '9'))) continue;
            var parsed = parts.Select(p => BigInteger.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (best is not null && Compare(parsed, best) <= 0) continue;
            latest = text;
            best = parsed;
        }
        return latest;
    }

    private static int Compare(BigInteger[] left, BigInteger[] right)
    {
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var comparison = (i < left.Length ? left[i] : BigInteger.Zero)
                .CompareTo(i < right.Length ? right[i] : BigInteger.Zero);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}
