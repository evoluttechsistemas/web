using System.Collections.Generic;
using System.Linq;

namespace EvolutCRM.Helpers
{
    /// <summary>
    /// Fonte única de verdade para telefones brasileiros no WhatsApp.
    /// </summary>
    public static class TelefoneBR
    {
        /// <summary>
        /// Formato canônico (o que deve ser GRAVADO):
        ///   Celular = 13 díg (55 + DDD + 9 + 8), recolocando o 9 se faltar.
        ///   Fixo    = 12 díg (55 + DDD + 8), sem injetar 9.
        /// </summary>
        public static string Normalizar(string bruto)
        {
            string t = SoDigitos(bruto).TrimStart('0');
            if (t.Length == 0) return "";
            if (!t.StartsWith("55")) t = "55" + t;
            if (t.Length < 12) return t;

            string ddd = t.Substring(2, 2);
            string local = t.Substring(4);

            if (!int.TryParse(ddd, out int nddd)) return t;

            // Identifica celular; fixo não é tocado
            bool ehCelular = local.Length == 9
                ? local[0] == '9'
                : local.Length == 8 && local[0] >= '6' && local[0] <= '9';

            if (!ehCelular) return t;

            if (nddd <= 30)
            {
                // DDD 11–30: JID COM o 9
                if (local.Length == 8) local = "9" + local;
            }
            else
            {
                // DDD 31–99: JID SEM o 9
                if (local.Length == 9) local = local.Substring(1);
            }

            return "55" + ddd + local;
        }

        /// <summary>
        /// Todas as formas plausíveis do número para LOOKUP (com/sem DDI 55,
        /// com/sem o 9). Não gera variante de 9 para números fixos.
        /// </summary>
        public static List<string> GerarVariantes(string bruto)
        {
            var v = new HashSet<string>();
            string t = SoDigitos(bruto).TrimStart('0');
            if (t.Length < 10) return v.ToList();
            if (!t.StartsWith("55")) t = "55" + t;

            v.Add(t);                 // com DDI
            v.Add(t.Substring(2));    // sem DDI

            if (t.Length >= 12)
            {
                string ddd = t.Substring(2, 2);
                string local = t.Substring(4);

                if (local.Length == 9 && local[0] == '9')
                {
                    // Celular com 9 → adiciona versão SEM 9
                    string sem9 = local.Substring(1);
                    v.Add("55" + ddd + sem9);
                    v.Add(ddd + sem9);
                }
                else if (local.Length == 8 && local[0] >= '6' && local[0] <= '9')
                {
                    // Celular sem 9 → adiciona versão COM 9 (só se parecer celular)
                    string com9 = "9" + local;
                    v.Add("55" + ddd + com9);
                    v.Add(ddd + com9);
                }
                // Fixo (local começa 2–5): não gera variante de 9
            }

            return v.ToList();
        }

        private static string SoDigitos(string s) =>
            new string((s ?? "").Where(char.IsDigit).ToArray());
    }
}