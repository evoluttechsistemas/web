using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Blog;

// ════════════════════════════════════════════════════════════════════
//  TEXTO LIVRE — bloco "escrita"
//  O usuário escreve direto no preview (com barra de formatação).
//  O HTML chega do navegador, então TUDO passa por SanitizarRico():
//  só sobrevivem tags de texto (p, h2, h3, listas, links, imagens…)
//  e nenhum atributo além de href/src/alt.
// ════════════════════════════════════════════════════════════════════
public static partial class LayoutRenderer
{
    private static readonly HashSet<string> TagsRicas = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "h2", "h3", "h4", "strong", "em", "u", "s", "mark", "code", "pre",
        "blockquote", "ul", "ol", "li", "a", "hr", "img", "sub", "sup",
        "table", "thead", "tbody", "tr", "th", "td"
    };

    private static readonly Regex ReTag = new(
        @"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:\s+[^\s""'>/=]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'>]+))?)*)\s*/?>",
        RegexOptions.CultureInvariant);

    private static string? Atributo(string attrs, string nome)
    {
        var m = Regex.Match(attrs, $@"\s{nome}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RI);
        if (!m.Success) return null;
        var v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return WebUtility.HtmlDecode(v);
    }

    /// <summary>Limpa o HTML vindo do preview: mantém só formatação de texto segura.</summary>
    public static string SanitizarRico(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var h = html.Replace("\r", "");
        h = Regex.Replace(h, @"<!--[\s\S]*?-->", "");
        h = Regex.Replace(h, @"<(script|style|iframe|object|embed|noscript|template|svg|math|button|select|textarea|form|head|title)\b[\s\S]*?</\1\s*>", "", RI);
        h = Regex.Replace(h, @"<(script|style|iframe|object|embed|link|meta|base|input)\b[^>]*>", "", RI);
        // ferramentas do editor que possam ter vindo junto
        h = Regex.Replace(h, @"<div[^>]*class=""[^""]*(pbx-|be-(video|img)-controls|be-resize-handle)[^""]*""[^>]*>[\s\S]*?</div>", "", RI);

        var sb = new StringBuilder(h.Length);
        var pos = 0;
        foreach (Match m in ReTag.Matches(h))
        {
            // texto entre tags: qualquer "<" ou ">" solto vira entidade
            sb.Append(h, pos, m.Index - pos).Replace("<", "&lt;", sb.Length - (m.Index - pos), m.Index - pos);
            pos = m.Index + m.Length;

            var fecha = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant() switch
            {
                "h1" => "h2",
                "h5" or "h6" => "h4",
                "b" => "strong",
                "i" => "em",
                "strike" or "del" => "s",
                "div" or "section" or "article" or "header" or "footer" or "figure" => "p",
                var t => t
            };
            if (!TagsRicas.Contains(tag)) continue;

            if (fecha)
            {
                if (tag is not ("br" or "hr" or "img")) sb.Append($"</{tag}>");
                continue;
            }

            var attrs = m.Groups[3].Value;
            switch (tag)
            {
                case "a":
                {
                    var href = Url(Atributo(attrs, "href"));
                    if (href is "" or "#") href = "#";
                    var externo = Regex.IsMatch(href, @"^https?://", RI);
                    sb.Append($"<a href=\"{H(href)}\"{(externo ? " target=\"_blank\" rel=\"noopener\"" : "")}>");
                    break;
                }
                case "img":
                {
                    var src = Url(Atributo(attrs, "src"), true);
                    if (src is "" or "#") break;
                    sb.Append($"<img src=\"{H(src)}\" alt=\"{H(Atributo(attrs, "alt") ?? "")}\" loading=\"lazy\">");
                    break;
                }
                default:
                    sb.Append($"<{tag}>");
                    break;
            }
        }
        sb.Append(h.AsSpan(pos).ToString().Replace("<", "&lt;"));

        var r = sb.ToString();
        r = Regex.Replace(r, @"<p>\s*</p>", "");
        r = Regex.Replace(r, @"(<br>\s*){3,}", "<br><br>");
        r = Regex.Replace(r, @"(\s*<p>(\s|<br>|&nbsp;)*</p>)+\s*$", "");
        r = r.Trim();
        // só sobrou "<p><br></p>" / espaços → vazio
        if (!r.Contains("<img", StringComparison.OrdinalIgnoreCase) && !r.Contains("<hr", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(Regex.Replace(r, "<[^>]+>", "")).Replace(' ', ' ')))
            return "";
        return r;
    }

    /// <summary>Gera ids nos títulos de um bloco de texto livre e conta as palavras.</summary>
    public static ConteudoProcessado ProcessarEscrita(PageSection s)
    {
        var html = s.Html ?? "";
        var prefixo = Regex.Replace(s.Id ?? "", "[^a-zA-Z0-9]", "");
        prefixo = prefixo.Length > 6 ? prefixo[..6] : prefixo;
        var titulos = new List<TituloConteudo>();
        var n = 0;
        var h = Regex.Replace(html, @"<(h[23])(\s[^>]*)?>([\s\S]*?)</\1>", m =>
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var inner = m.Groups[3].Value;
            var texto = WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", "")).Replace(' ', ' ').Trim();
            if (texto.Length == 0) return m.Value;
            var id = $"evl-e{prefixo}-{++n}";
            titulos.Add(new TituloConteudo(id, texto, tag == "h2" ? 2 : 3));
            return $"<{tag} id=\"{id}\">{inner}</{tag}>";
        }, RI);
        var semMidia = Regex.Replace(html, @"<img[^>]*>", " ", RI);
        var palavras = Regex.Matches(WebUtility.HtmlDecode(Regex.Replace(semMidia, "<[^>]+>", " ")), @"[\p{L}\p{N}]+").Count;
        return new ConteudoProcessado { Html = h, Titulos = titulos, Palavras = palavras };
    }

    /// <summary>
    /// Percorre os blocos visíveis na ordem e monta: sumário do documento inteiro
    /// (texto do post + blocos de texto livre) e o total de palavras.
    /// </summary>
    public static void Preparar(PostLayout l, RenderContexto ctx)
    {
        var titulos = new List<TituloConteudo>();
        var extras = 0;
        ctx.Escritas.Clear();
        foreach (var s in l.Secoes.OrderBy(x => x.Ordem))
        {
            if (s.Tipo == "escrita")
            {
                var p = ProcessarEscrita(s);
                ctx.Escritas[s.Id] = p;
                if (s.Oculta) continue;
                titulos.AddRange(p.Titulos);
                extras += p.Palavras;
            }
            else if (s.Tipo == "conteudo" && !s.Oculta)
            {
                titulos.AddRange(ctx.Processado.Titulos);
            }
        }
        // Se o post não tem bloco "conteudo", o texto do editor não aparece — não entra no sumário
        ctx.TitulosDocumento = titulos;
        ctx.PalavrasExtras = extras;
    }

    private static string Sumario(RenderContexto ctx)
    {
        var titulos = ctx.Titulos;
        if (titulos.Count < 2)
            return ctx.Preview ? "<div class=\"evl-toc pbx-toc-vazio\"><div class=\"evl-toc-t\">Neste artigo</div><small>O sumário aparece quando o texto tiver 2 ou mais títulos (H2/H3).</small></div>" : "";
        var sb = new StringBuilder("<nav class=\"evl-toc\" aria-label=\"Sumário\"><div class=\"evl-toc-t\">Neste artigo</div><ul>");
        foreach (var t in titulos)
            sb.Append($"<li class=\"evl-n{t.Nivel}\"><a href=\"#{t.Id}\">{H(t.Texto)}</a></li>");
        return sb.Append("</ul></nav>").ToString();
    }

    private static string Escrita(PageSection s, RenderContexto ctx)
    {
        if (!ctx.Escritas.TryGetValue(s.Id, out var p)) p = ProcessarEscrita(s);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(s.Subtitulo)) sb.Append(Sobretitulo(s, ctx));
        if (!string.IsNullOrWhiteSpace(s.Titulo)) sb.Append(Titulo(s, ctx));
        if (s.Sumario) sb.Append(Sumario(ctx));
        var cls = "evl-content evl-escrita" + (s.EstiloCartao ? " evl-card" : "") + (s.LetraCapitular ? " evl-dropcap" : "");
        if (ctx.Preview)
            sb.Append($"<div class=\"{cls}\" data-edit=\"Html\" data-rich=\"1\" data-ph=\"Comece a escrever aqui…  (dica: ## + espaço = título, - + espaço = lista)\" contenteditable=\"true\" spellcheck=\"true\">{p.Html}</div>");
        else if (p.Html.Length > 0)
            sb.Append($"<div class=\"{cls}\">{p.Html}</div>");
        return sb.ToString();
    }
}
