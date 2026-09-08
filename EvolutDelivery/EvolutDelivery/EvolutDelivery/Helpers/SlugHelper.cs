using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutDelivery.Helpers
{
    public static class SlugHelper
    {
        public static string GerarSlugBase(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return "empresa";

            texto = texto.ToLower().Trim();

            var normalized = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            var semAcento = sb.ToString().Normalize(NormalizationForm.FormC);

            semAcento = Regex.Replace(semAcento, @"[^a-z0-9\s-]", "");
            semAcento = Regex.Replace(semAcento, @"\s+", "-");
            semAcento = Regex.Replace(semAcento, @"-+", "-");
            semAcento = semAcento.Trim('-');

            return string.IsNullOrWhiteSpace(semAcento) ? "empresa" : semAcento;
        }
    }
}