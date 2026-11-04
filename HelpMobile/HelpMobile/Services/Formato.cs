namespace HelpMobile.Services
{
    /// <summary>Formatação de horários, iniciais, cores e status (igual ao WhatsApp).</summary>
    public static class Formato
    {
        // Nomes fixos em português (não depende do idioma do celular)
        private static readonly string[] Dias = { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" };
        private static readonly string[] Meses = { "janeiro", "fevereiro", "março", "abril", "maio", "junho",
                                                   "julho", "agosto", "setembro", "outubro", "novembro", "dezembro" };

        // Mesma paleta do GerarCorUsuario do HELP, com hash estável (não muda a cada reinício)
        private static readonly string[] Cores =
        {
            "#e53935", "#1976d2", "#43a047", "#8e24aa", "#fb8c00",
            "#00897b", "#3949ab", "#d81b60", "#5e35b1", "#546e7a"
        };

        public static string Cor(string? texto)
        {
            unchecked
            {
                int h = 17;
                foreach (var c in (texto ?? "").ToUpperInvariant()) h = h * 31 + c;
                return Cores[Math.Abs(h % Cores.Length)];
            }
        }

        public static string Iniciais(string? nome)
        {
            var partes = (nome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Any(char.IsLetter)).ToArray();
            if (partes.Length == 0)
            {
                var digitos = new string((nome ?? "").Where(char.IsDigit).ToArray());
                return digitos.Length >= 2 ? digitos[^2..] : "?";
            }
            if (partes.Length == 1) return partes[0][..Math.Min(2, partes[0].Length)].ToUpperInvariant();
            return (partes[0][0].ToString() + partes[^1][0]).ToUpperInvariant();
        }

        /// <summary>Horário da lista: "14:32", "Ontem", "segunda", "12/10/2026".</summary>
        public static string HoraLista(DateTime dt)
        {
            var hoje = DateTime.Today;
            if (dt.Date == hoje) return dt.ToString("HH:mm");
            if (dt.Date == hoje.AddDays(-1)) return "Ontem";
            if (dt.Date > hoje.AddDays(-7)) return Dias[(int)dt.DayOfWeek];
            return dt.ToString("dd/MM/yyyy");
        }

        /// <summary>Separador de data dentro da conversa.</summary>
        public static string DiaConversa(DateTime dt)
        {
            var hoje = DateTime.Today;
            if (dt.Date == hoje) return "Hoje";
            if (dt.Date == hoje.AddDays(-1)) return "Ontem";
            if (dt.Date > hoje.AddDays(-7)) return Capitalizar(Dias[(int)dt.DayOfWeek]);
            return $"{dt.Day} de {Meses[dt.Month - 1]} de {dt.Year}";
        }

        private static string Capitalizar(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

        /// <summary>enviando | enviado | entregue | lido | erro | "" (sem WhatsApp)</summary>
        public static string Entrega(string? status)
        {
            switch ((status ?? "").Trim().ToLowerInvariant())
            {
                case "sending": case "pending": return "enviando";
                case "sent": return "enviado";
                case "received": case "delivered": case "delivery_ack": case "delivered2user": return "entregue";
                case "read": case "read_ack": return "lido";
                case "error": case "failed": return "erro";
                case "": case "interno": case "saudacao_auto": case "aguarde_auto": case "ia_processing": return "";
                default: return "enviando";
            }
        }

        public static bool UsuarioAutomatico(string? usuario) =>
            (usuario ?? "").Trim().ToUpperInvariant() is "" or "NOVO" or "WHATSAPP" or "CLIENTE" or "BOT" or "SISTEMA" or "IA" or "EVOLUTTECH";

        /// <summary>R$ 1.234,56 (formatado na mão: não depende do idioma do celular).</summary>
        public static string Moeda(decimal valor)
        {
            var neg = valor < 0;
            var s = Math.Abs(valor).ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)
                .Replace(",", "#").Replace(".", ",").Replace("#", ".");
            return (neg ? "-R$ " : "R$ ") + s;
        }

        public static string Data(DateTime? data) => data?.ToString("dd/MM/yyyy") ?? "-";

        public static string Primeiro(string? nome)
        {
            var p = (nome ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return p.Length <= 1 ? p : char.ToUpper(p[0]) + p[1..].ToLowerInvariant();
        }
    }
}
