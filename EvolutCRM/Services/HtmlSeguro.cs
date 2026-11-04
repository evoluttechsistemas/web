using Ganss.Xss;

namespace EvolutCRM.Services;

/// <summary>
/// Limpa o HTML das documentações antes de mostrar na central de ajuda (página pública).
/// Usa o HtmlSanitizer (lista do que é permitido): mantém texto, cores, tabelas, imagens e
/// vídeos do YouTube/Vimeo; remove scripts, eventos (onclick...), links javascript:,
/// iframes de outros sites, &lt;style&gt;, &lt;meta&gt;, formulários etc.
/// </summary>
public static class HtmlSeguro
{
    // Únicos sites aceitos em <iframe> (vídeos incorporados pelo editor)
    private static readonly string[] SitesDeVideo =
    {
        "www.youtube.com", "youtube.com", "www.youtube-nocookie.com", "youtube-nocookie.com", "player.vimeo.com"
    };

    public static string Limpar(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        return Criar().Sanitize(html);
    }

    private static HtmlSanitizer Criar()
    {
        var s = new HtmlSanitizer();

        // O que o editor usa além do padrão do HtmlSanitizer
        s.AllowedTags.Add("iframe");
        s.AllowedTags.Add("video");
        s.AllowedTags.Add("source");
        foreach (var atributo in new[] { "class", "allowfullscreen", "loading", "controls", "data-type",
                                         "frameborder", "allow", "referrerpolicy", "target", "rel" })
            s.AllowedAttributes.Add(atributo);
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");

        // O padrão do HtmlSanitizer aceita formulários: na página pública isso permitiria
        // montar um "login falso" no nosso domínio. Documentação não precisa disso.
        foreach (var tag in new[] { "form", "input", "textarea", "select", "option", "optgroup",
                                    "button", "label", "fieldset", "legend", "datalist", "output" })
            s.AllowedTags.Remove(tag);
        s.AllowedAttributes.Remove("contenteditable");
        s.AllowedAttributes.Remove("action");
        s.AllowedAttributes.Remove("formaction");

        // Sem posicionamento: impede um bloco de cobrir a página inteira (position: fixed)
        foreach (var css in new[] { "position", "z-index" })
            s.AllowedCssProperties.Remove(css);

        // <iframe> só de YouTube/Vimeo, e só https
        s.FilterUrl += (_, e) =>
        {
            if (e.Tag?.NodeName == "IFRAME" && !VideoPermitido(e.OriginalUrl))
                e.SanitizedUrl = null;
        };

        s.PostProcessDom += (_, e) =>
        {
            // iframe que ficou sem endereço (site não permitido) sai inteiro
            foreach (var iframe in e.Document.QuerySelectorAll("iframe").ToList())
                if (string.IsNullOrWhiteSpace(iframe.GetAttribute("src")))
                    iframe.Remove();

            // links que abrem nova aba não dão acesso à página de origem
            foreach (var link in e.Document.QuerySelectorAll("a[target]"))
                link.SetAttribute("rel", "noopener noreferrer");
        };

        return s;
    }

    private static bool VideoPermitido(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && SitesDeVideo.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
