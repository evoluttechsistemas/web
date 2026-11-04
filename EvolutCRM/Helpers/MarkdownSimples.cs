using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Helpers;

/// <summary>
/// Markdown leve e SEGURO para as respostas do Assistente IA:
/// escapa todo HTML primeiro e só depois aplica títulos, listas, negrito, código, links (só http/https e links internos)
/// e as citações [D1]/[C1]/[W1]. Sem dependência externa.
/// </summary>
public static class MarkdownSimples
{
    private static readonly Regex RxBlocoCliente = new(@"<resposta_cliente>\s*(?<t>[\s\S]*?)\s*(</resposta_cliente>|$)", RegexOptions.IgnoreCase);

    /// <summary>Separa o texto pronto para o cliente (entre &lt;resposta_cliente&gt;) do resto da resposta.</summary>
    public static (string Antes, string? Cliente, string Depois) SepararRespostaCliente(string? texto)
    {
        texto ??= "";
        var m = RxBlocoCliente.Match(texto);
        if (!m.Success) return (texto, null, "");
        return (texto[..m.Index].TrimEnd(), m.Groups["t"].Value.Trim(), texto[(m.Index + m.Length)..].TrimStart());
    }

    public static string ParaHtml(string? md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "";
        var linhas = md.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        string? lista = null;          // "ul" | "ol"
        bool emCodigo = false;
        var codigo = new StringBuilder();
        var paragrafo = new List<string>();

        void FecharParagrafo()
        {
            if (paragrafo.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br>", paragrafo.Select(Inline))).Append("</p>");
            paragrafo.Clear();
        }
        void FecharLista()
        {
            if (lista == null) return;
            sb.Append("</").Append(lista).Append('>');
            lista = null;
        }

        foreach (var bruta in linhas)
        {
            var linha = bruta.TrimEnd();

            if (linha.TrimStart().StartsWith("```"))
            {
                if (emCodigo)
                {
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(codigo.ToString().TrimEnd('\n'))).Append("</code></pre>");
                    codigo.Clear();
                    emCodigo = false;
                }
                else
                {
                    FecharParagrafo(); FecharLista();
                    emCodigo = true;
                }
                continue;
            }
            if (emCodigo) { codigo.Append(bruta).Append('\n'); continue; }

            if (string.IsNullOrWhiteSpace(linha)) { FecharParagrafo(); FecharLista(); continue; }

            var t = linha.TrimStart();

            var h = Regex.Match(t, @"^(#{1,6})\s+(.+)$");
            if (h.Success)
            {
                FecharParagrafo(); FecharLista();
                var nivel = h.Groups[1].Value.Length <= 2 ? "h4" : "h5";
                sb.Append('<').Append(nivel).Append('>').Append(Inline(h.Groups[2].Value)).Append("</").Append(nivel).Append('>');
                continue;
            }

            if (Regex.IsMatch(t, @"^(-{3,}|\*{3,}|_{3,})$")) { FecharParagrafo(); FecharLista(); sb.Append("<hr>"); continue; }

            var ul = Regex.Match(t, @"^[-*•]\s+(.+)$");
            var ol = Regex.Match(t, @"^(\d{1,3})[.)]\s+(.+)$");
            if (ul.Success || ol.Success)
            {
                FecharParagrafo();
                var tipo = ul.Success ? "ul" : "ol";
                if (lista != tipo)
                {
                    FecharLista();
                    lista = tipo;
                    if (tipo == "ol" && ol.Groups[1].Value != "1")
                        sb.Append("<ol start=\"").Append(int.Parse(ol.Groups[1].Value)).Append("\">");
                    else sb.Append('<').Append(tipo).Append('>');
                }
                sb.Append("<li>").Append(Inline(ul.Success ? ul.Groups[1].Value : ol.Groups[2].Value)).Append("</li>");
                continue;
            }

            if (t.StartsWith("> "))
            {
                FecharParagrafo(); FecharLista();
                sb.Append("<blockquote>").Append(Inline(t[2..])).Append("</blockquote>");
                continue;
            }

            // continuação de item de lista (linha recuada)
            if (lista != null && bruta.StartsWith("  "))
            {
                sb.Length -= "</li>".Length;
                sb.Append("<br>").Append(Inline(t)).Append("</li>");
                continue;
            }

            FecharLista();
            paragrafo.Add(t);
        }

        if (emCodigo) sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(codigo.ToString())).Append("</code></pre>");
        FecharParagrafo();
        FecharLista();
        return sb.ToString();
    }

    private static string Inline(string texto)
    {
        // 1) separa os trechos de código para não mexer neles
        var codigos = new List<string>();
        texto = Regex.Replace(texto, @"`([^`]+)`", m =>
        {
            codigos.Add(m.Groups[1].Value);
            return $"\u0001{codigos.Count - 1}\u0001";
        });

        // 2) links markdown antes de escapar (guarda e devolve depois)
        var links = new List<(string Texto, string Url)>();
        texto = Regex.Replace(texto, @"\[([^\]]{1,200})\]\((https?://[^\s)]+|/[^\s)]*)\)", m =>
        {
            links.Add((m.Groups[1].Value, m.Groups[2].Value));
            return $"\u0002{links.Count - 1}\u0002";
        });

        var s = WebUtility.HtmlEncode(texto);

        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "<em>$1</em>");
        s = Regex.Replace(s, @"(?<![\w])_(?!\s)(.+?)(?<!\s)_(?![\w])", "<em>$1</em>");

        // URLs soltas
        s = Regex.Replace(s, @"(?<![""'=/\w])(https?://[^\s<]+[^\s<.,;:!?)\]])", m =>
        {
            var url = WebUtility.HtmlDecode(m.Value);
            return $"<a href=\"{WebUtility.HtmlEncode(url)}\" target=\"_blank\" rel=\"noopener noreferrer\">{m.Value}</a>";
        });

        // citações [D1] [C2] [W3]
        s = Regex.Replace(s, @"\[([DCW]\d{1,2})\]", "<span class=\"aia-cit\" data-fonte=\"$1\">$1</span>");

        s = Regex.Replace(s, "\u0002(\\d+)\u0002", m =>
        {
            var (t, url) = links[int.Parse(m.Groups[1].Value)];
            var externo = url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            return $"<a href=\"{WebUtility.HtmlEncode(url)}\"{(externo ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "")}>{WebUtility.HtmlEncode(t)}</a>";
        });
        s = Regex.Replace(s, "\u0001(\\d+)\u0001", m =>
            "<code>" + WebUtility.HtmlEncode(codigos[int.Parse(m.Groups[1].Value)]) + "</code>");
        return s;
    }

    /// <summary>Texto puro para colar na resposta ao cliente: sem markdown, sem citações.</summary>
    public static string ParaTextoPuro(string? md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "";
        var s = md.Replace("\r\n", "\n");
        s = Regex.Replace(s, @"```[a-zA-Z]*\n?", "");
        s = Regex.Replace(s, @"^\s{0,3}#{1,6}\s+", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
        s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "$1");
        s = Regex.Replace(s, @"`([^`]+)`", "$1");
        s = Regex.Replace(s, @"\[([^\]]+)\]\((https?://[^\s)]+)\)", "$1 ($2)");
        s = Regex.Replace(s, @"\s?\[[DCW]\d{1,2}\]", "");
        s = Regex.Replace(s, @"^\s*[*•]\s+", "- ", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^\s*(-{3,}|\*{3,})\s*$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }
}
