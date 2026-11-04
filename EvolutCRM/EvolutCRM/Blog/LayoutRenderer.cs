using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Blog;

// ════════════════════════════════════════════════════════════════════
//  LAYOUT RENDERER — gera o HTML do Page Builder.
//  O MESMO código gera o preview do editor e o HTML final do site,
//  então o que aparece no preview é exatamente o que vai para o ar.
//  • Preview: conteúdo do post é injetado direto + atributos de edição
//  • Site: conteúdo vira o marcador <!--EVL:CONTEUDO--> (o site injeta
//          o Conteudo atual do post; assim a imagem base64 não duplica)
// ════════════════════════════════════════════════════════════════════

public sealed class RenderContexto
{
    public string TituloPost { get; set; } = "";
    public string? Capa { get; set; }
    public DateTime Data { get; set; } = DateTime.Now;
    public string ConteudoHtml { get; set; } = "";
    public bool Preview { get; set; }

    private ConteudoProcessado? _proc;
    public ConteudoProcessado Processado => _proc ??= LayoutRenderer.ProcessarConteudo(ConteudoHtml);

    // Preenchidos por LayoutRenderer.Preparar(): blocos de texto livre + sumário do documento inteiro
    public Dictionary<string, ConteudoProcessado> Escritas { get; } = new();
    public List<TituloConteudo>? TitulosDocumento { get; set; }
    public int PalavrasExtras { get; set; }

    /// <summary>Tempo de leitura do post inteiro (texto do editor + blocos de texto livre).</summary>
    public int MinutosLeitura => Math.Max(1, (int)Math.Ceiling((Processado.Palavras + PalavrasExtras) / 200.0));

    /// <summary>Títulos para o sumário: documento inteiro quando preparado, senão só o texto do post.</summary>
    public List<TituloConteudo> Titulos => TitulosDocumento ?? Processado.Titulos;
}

public sealed record TituloConteudo(string Id, string Texto, int Nivel);

public sealed class ConteudoProcessado
{
    public string Html { get; init; } = "";
    public List<TituloConteudo> Titulos { get; init; } = new();
    public int Palavras { get; init; }
    public int MinutosLeitura => Math.Max(1, (int)Math.Ceiling(Palavras / 200.0));
}

public static partial class LayoutRenderer
{
    public const string MarcadorConteudo = "<!--EVL:CONTEUDO-->";
    private static readonly CultureInfo PtBr = new("pt-BR");
    private const RegexOptions RI = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ─────────────────────────────────────────────────────────────────
    //  Normalização / migração de layouts antigos
    // ─────────────────────────────────────────────────────────────────
    public static void Normalizar(PostLayout l)
    {
        l.Secoes ??= new();
        var v1 = l.Versao < 2;

        foreach (var s in l.Secoes)
        {
            s.Itens ??= new();
            s.Html ??= "";
            s.Tipo = string.IsNullOrWhiteSpace(s.Tipo) ? "texto" : s.Tipo.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s.Alinhamento))
                s.Alinhamento = s.TextoCentralizado ? "centro" : "esquerda";
            if (s.CorBorda == "transparent") s.CorBorda = "";

            if (v1)
            {
                if (s.Tipo == "hero" && !string.IsNullOrWhiteSpace(s.Imagem) && string.IsNullOrWhiteSpace(s.ImagemFundo))
                {
                    s.FundoTipo = "imagem";
                    s.ImagemFundo = s.Imagem;
                    s.Imagem = "";
                }
                if (s.Tipo == "caixa") s.Variante = "custom";
                if (s.Tipo == "cta" && string.IsNullOrWhiteSpace(s.BotaoTexto))
                {
                    s.BotaoTexto = "Ver mais artigos";
                    s.BotaoLink = "/blogs";
                    s.CorDestaque = "#ffffff";
                    s.Alinhamento = "centro";
                }
                if (s.Tipo is "caixa" or "banner" or "texto" && s.CorFundo is "#ffffff") s.CorFundo = "";
            }

            s.PaddingV = Math.Clamp(s.PaddingV, 0, 400);
            s.PaddingLateral = Math.Clamp(s.PaddingLateral, 0, 120);
            s.EscalaTitulo = Math.Clamp(s.EscalaTitulo <= 0 ? 100 : s.EscalaTitulo, 50, 200);
            s.Colunas = Math.Clamp(s.Colunas, 1, 4);
            s.OverlayOpacidade = Math.Clamp(s.OverlayOpacidade, 0, 100);
            s.BordaEspessura = Math.Clamp(s.BordaEspessura, 1, 20);
        }

        // Layout antigo não mostrava o texto do post no site → adiciona o bloco de conteúdo
        if (v1 && l.Secoes.Count > 0 && !l.Secoes.Any(x => x.Tipo == "conteudo"))
        {
            var ordenadas = l.Secoes.OrderBy(x => x.Ordem).ToList();
            var posHero = ordenadas.FindIndex(x => x.Tipo == "hero");
            ordenadas.Insert(posHero + 1, new PageSection { Tipo = "conteudo", PaddingV = 48, Sumario = true });
            l.Secoes = ordenadas;
        }

        l.Secoes = l.Secoes.OrderBy(x => x.Ordem).ToList();
        for (var i = 0; i < l.Secoes.Count; i++)
        {
            l.Secoes[i].Ordem = i;
            l.Secoes[i].TextoCentralizado = l.Secoes[i].Alinhamento == "centro";
        }

        l.LarguraMaxima = Math.Clamp(l.LarguraMaxima <= 0 ? 780 : l.LarguraMaxima, 480, 1600);
        l.TamanhoFonte = Math.Clamp(l.TamanhoFonte <= 0 ? 17 : l.TamanhoFonte, 12, 24);
        l.AlturaLinha = Math.Clamp(l.AlturaLinha <= 0 ? 180 : l.AlturaLinha, 120, 240);
        l.Raio = Math.Clamp(l.Raio, 0, 40);
        l.EspacoSecoes = Math.Clamp(l.EspacoSecoes, 0, 120);
        if (l.PesoTitulos is < 400 or > 900) l.PesoTitulos = 800;
        if (string.IsNullOrWhiteSpace(l.FonteTitulos)) l.FonteTitulos = l.FonteCorpo;
        l.Versao = 2;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Cores
    // ─────────────────────────────────────────────────────────────────
    public static readonly (string Token, string Nome, string Var)[] TokensCor =
    {
        ("var:primaria",   "Primária",   "--evl-primary"),
        ("var:secundaria", "Secundária", "--evl-secondary"),
        ("var:suave",      "Suave",      "--evl-soft"),
        ("var:superficie", "Superfície", "--evl-surface"),
        ("var:fundo",      "Fundo",      "--evl-bg"),
        ("var:texto",      "Texto",      "--evl-text"),
        ("var:titulos",    "Títulos",    "--evl-heading"),
    };

    private static readonly Regex ReHex = new(@"^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$", RI);
    private static readonly Regex ReRgb = new(@"^rgba?\(\s*[\d.]+%?\s*,\s*[\d.]+%?\s*,\s*[\d.]+%?\s*(,\s*[\d.]+%?\s*)?\)$", RI);

    /// <summary>Converte o valor salvo em um valor CSS seguro.</summary>
    public static string Cor(string? v, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        v = v.Trim();
        foreach (var t in TokensCor)
            if (string.Equals(t.Token, v, StringComparison.OrdinalIgnoreCase)) return $"var({t.Var})";
        if (v.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return "transparent";
        if (ReHex.IsMatch(v) || ReRgb.IsMatch(v)) return v;
        return fallback;
    }

    /// <summary>Resolve tokens do tema para #rrggbb (para inputs de cor e cálculos de contraste).</summary>
    public static string Hex(string? v, PostLayout l, string fallback = "#ffffff")
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        v = v.Trim().ToLowerInvariant();
        return v switch
        {
            "var:primaria" => Hex(l.CorPrimaria, l, "#2563eb"),
            "var:secundaria" => Hex(l.CorSecundaria, l, "#0ea5e9"),
            "var:superficie" => Hex(l.CorSuperficie, l, "#ffffff"),
            "var:fundo" => Hex(l.CorFundoGeral, l, "#f8fafc"),
            "var:texto" => Hex(l.CorTexto, l, "#1e293b"),
            "var:titulos" => Hex(l.CorTitulos, l, "#0f172a"),
            "var:suave" => Mix(Hex(l.CorPrimaria, l, "#2563eb"), Hex(l.CorSuperficie, l, "#ffffff"), 0.09),
            _ when ReHex.IsMatch(v) => v.Length == 4 ? $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}" : v[..7],
            _ => fallback
        };
    }

    public static string Mix(string a, string b, double pesoA)
    {
        var (r1, g1, b1) = Rgb(a);
        var (r2, g2, b2) = Rgb(b);
        int M(int x, int y) => (int)Math.Round(x * pesoA + y * (1 - pesoA));
        return $"#{M(r1, r2):x2}{M(g1, g2):x2}{M(b1, b2):x2}";
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length < 6) return (255, 255, 255);
        return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }

    public static double Luminancia(string hex)
    {
        var (r, g, b) = Rgb(hex);
        double L(int c) { var x = c / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(r) + 0.7152 * L(g) + 0.0722 * L(b);
    }

    /// <summary>Cor de texto legível sobre o fundo informado.</summary>
    public static string Contraste(string hex) => Luminancia(hex) > 0.45 ? "#0f172a" : "#ffffff";

    // ─────────────────────────────────────────────────────────────────
    //  Texto
    // ─────────────────────────────────────────────────────────────────
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Substitui variáveis dinâmicas: {titulo} {data} {leitura} {ano}</summary>
    public static string Tokens(string? txt, RenderContexto ctx)
    {
        if (string.IsNullOrEmpty(txt) || !txt.Contains('{')) return txt ?? "";
        return txt
            .Replace("{titulo}", string.IsNullOrWhiteSpace(ctx.TituloPost) ? "Título do post" : ctx.TituloPost, StringComparison.OrdinalIgnoreCase)
            .Replace("{data}", ctx.Data.ToString("dd 'de' MMMM 'de' yyyy", PtBr), StringComparison.OrdinalIgnoreCase)
            .Replace("{leitura}", $"{ctx.MinutosLeitura} min de leitura", StringComparison.OrdinalIgnoreCase)
            .Replace("{ano}", ctx.Data.Year.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Texto simples (com variáveis), codificado.</summary>
    private static string T(string? txt, RenderContexto ctx) => H(Tokens(txt, ctx));

    /// <summary>Texto rico leve: parágrafos, listas (- item / 1. item), **negrito**, *itálico*, ==marca==, [link](url).</summary>
    public static string Rico(string? txt, RenderContexto ctx)
    {
        if (string.IsNullOrWhiteSpace(txt)) return "";
        var t = H(Tokens(txt, ctx)).Replace("\r", "").Trim();
        var sb = new StringBuilder();
        foreach (var bloco in Regex.Split(t, @"\n\s*\n"))
        {
            var linhas = bloco.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (linhas.Count == 0) continue;
            if (linhas.All(x => Regex.IsMatch(x, @"^[-•*]\s+")))
            {
                sb.Append("<ul>");
                foreach (var x in linhas) sb.Append("<li>").Append(Inline(Regex.Replace(x, @"^[-•*]\s+", ""))).Append("</li>");
                sb.Append("</ul>");
            }
            else if (linhas.All(x => Regex.IsMatch(x, @"^\d+[.)]\s+")))
            {
                sb.Append("<ol>");
                foreach (var x in linhas) sb.Append("<li>").Append(Inline(Regex.Replace(x, @"^\d+[.)]\s+", ""))).Append("</li>");
                sb.Append("</ol>");
            }
            else
            {
                sb.Append("<p>").Append(string.Join("<br>", linhas.Select(Inline))).Append("</p>");
            }
        }
        return sb.ToString();
    }

    private static string Inline(string s)
    {
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        s = Regex.Replace(s, @"(?<![*\w])\*(?!\s)(.+?)(?<!\s)\*(?![*\w])", "<em>$1</em>");
        s = Regex.Replace(s, @"==(.+?)==", "<mark>$1</mark>");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)", m =>
            $"<a href=\"{H(Url(WebUtility.HtmlDecode(m.Groups[2].Value)))}\">{m.Groups[1].Value}</a>");
        return s;
    }

    /// <summary>Campo pode ser editado direto no preview? (texto puro, sem formatação/variáveis/listas)</summary>
    private static bool Editavel(string? v) =>
        !Regex.IsMatch(v ?? "", @"\*|==|\[[^\]]*\]\(|\{(titulo|data|leitura|ano)\}|(^|\n)\s*([-•]|\d+[.)])\s", RI);

    /// <summary>Atributos de edição inline (somente preview).</summary>
    private static string Ed(RenderContexto ctx, string campo, string? valor, string placeholder = "")
    {
        if (!ctx.Preview || !Editavel(valor)) return "";
        var ph = string.IsNullOrEmpty(placeholder) ? "" : $" data-ph=\"{H(placeholder)}\"";
        return $" data-edit=\"{campo}\"{ph} contenteditable=\"true\" spellcheck=\"false\"";
    }

    public static string Url(string? v, bool imagem = false)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        v = v.Trim();
        if (Regex.IsMatch(v, @"^\s*(javascript|vbscript):", RI)) return "#";
        if (v.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return imagem && v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ? v : "#";
        if (Regex.IsMatch(v, @"^(https?:|mailto:|tel:|/|#|\.)", RI)) return v;
        if (Regex.IsMatch(v, @"^[\w-]+(\.[\w-]+)+(/|$)")) return "https://" + v;
        return v;
    }

    private static string Iniciais(string? nome)
    {
        var partes = (nome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return "✍";
        return (partes[0][..1] + (partes.Length > 1 ? partes[^1][..1] : "")).ToUpperInvariant();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Conteúdo do post (HTML do editor principal)
    // ─────────────────────────────────────────────────────────────────
    private static readonly object _cacheLock = new();
    private static string? _cacheFonte;
    private static ConteudoProcessado? _cacheResultado;

    public static ConteudoProcessado ProcessarConteudo(string? html)
    {
        html ??= "";
        lock (_cacheLock)
        {
            if (_cacheResultado is not null && (ReferenceEquals(_cacheFonte, html) || _cacheFonte == html))
                return _cacheResultado;
        }

        var h = html;
        // controles do editor (botões de imagem/vídeo, alça de redimensionar)
        h = Regex.Replace(h, @"<div[^>]*class=""[^""]*be-(video|img)-controls[^""]*""[^>]*>[\s\S]*?</div>", "", RI);
        h = Regex.Replace(h, @"<div[^>]*class=""[^""]*be-resize-handle[^""]*""[^>]*>\s*</div>", "", RI);
        h = Regex.Replace(h, @"\scontenteditable=""[^""]*""", "", RI);
        h = Regex.Replace(h, @"<script[\s\S]*?</script>", "", RI);
        // mesma limpeza do site: remove fundo inline (vem de textos colados)
        h = Regex.Replace(h, @"(\sstyle=""[^""]*?)background(-color)?\s*:\s*[^;""]+(;)?", "$1", RI);
        h = Regex.Replace(h, @"\sstyle=""\s*""", "", RI);

        // ids nos títulos para o sumário (mesmo algoritmo usado no site)
        var titulos = new List<TituloConteudo>();
        var n = 0;
        h = Regex.Replace(h, @"<(h[23])(\s[^>]*)?>([\s\S]*?)</\1>", m =>
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var attrs = m.Groups[2].Value;
            var inner = m.Groups[3].Value;
            var texto = WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", "")).Replace(' ', ' ').Trim();
            if (texto.Length == 0) return m.Value;
            var id = $"evl-h-{++n}";
            attrs = Regex.Replace(attrs, @"\sid=""[^""]*""", "", RI);
            titulos.Add(new TituloConteudo(id, texto, tag == "h2" ? 2 : 3));
            return $"<{tag} id=\"{id}\"{attrs}>{inner}</{tag}>";
        }, RI);

        // contagem de palavras (ignora imagens/base64)
        var semMidia = Regex.Replace(html, @"<(img|iframe|video)[^>]*>", " ", RI);
        var texto = WebUtility.HtmlDecode(Regex.Replace(semMidia, "<[^>]+>", " "));
        var palavras = Regex.Matches(texto, @"[\p{L}\p{N}]+").Count;

        var res = new ConteudoProcessado { Html = h, Titulos = titulos, Palavras = palavras };
        lock (_cacheLock) { _cacheFonte = html; _cacheResultado = res; }
        return res;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Fontes / variáveis do tema
    // ─────────────────────────────────────────────────────────────────
    public static readonly string[] Fontes =
    {
        "Plus Jakarta Sans", "Inter", "IBM Plex Sans", "Poppins", "Montserrat", "DM Sans", "Manrope",
        "Outfit", "Nunito", "Space Grotesk", "Playfair Display", "Fraunces", "Lora", "Merriweather",
        "Source Serif 4", "Roboto Slab"
    };

    private static readonly string[] Serifadas = { "Playfair Display", "Fraunces", "Lora", "Merriweather", "Source Serif 4", "Roboto Slab" };

    private static string Pesos(string fonte) => fonte switch
    {
        "Lora" or "IBM Plex Sans" or "Space Grotesk" => "400;500;600;700",
        "Merriweather" => "400;700;900",
        _ => "400;500;600;700;800"
    };

    public static string Familia(string? fonte)
    {
        fonte = string.IsNullOrWhiteSpace(fonte) ? "Plus Jakarta Sans" : fonte.Replace("'", "").Replace("\"", "").Replace(";", "");
        var fb = Serifadas.Contains(fonte) ? "Georgia, 'Times New Roman', serif" : "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
        return $"'{fonte}', {fb}";
    }

    public static string FontesLink(PostLayout l)
    {
        var fams = new[] { l.FonteTitulos, l.FonteCorpo }.Where(f => Fontes.Contains(f)).Distinct().ToList();
        if (fams.Count == 0) return "";
        var q = string.Join("&amp;", fams.Select(f => $"family={f.Replace(' ', '+')}:wght@{Pesos(f)}"));
        return $"<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?{q}&amp;display=swap\">";
    }

    public static string SombraCss(string? tipo) => tipo switch
    {
        "nenhuma" => "none",
        "media" => "0 4px 12px rgba(15,23,42,.08), 0 18px 40px rgba(15,23,42,.10)",
        "forte" => "0 10px 25px rgba(15,23,42,.14), 0 30px 70px rgba(15,23,42,.22)",
        _ => "0 1px 2px rgba(15,23,42,.04), 0 8px 24px rgba(15,23,42,.06)"
    };

    public static string VarsCss(PostLayout l)
    {
        var prim = Hex(l.CorPrimaria, l, "#2563eb");
        var sup = Hex(l.CorSuperficie, l, "#ffffff");
        var lh = (l.AlturaLinha / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
        return
            $"--evl-primary:{Cor(l.CorPrimaria, "#2563eb")};" +
            $"--evl-secondary:{Cor(l.CorSecundaria, "#0ea5e9")};" +
            $"--evl-bg:{Cor(l.CorFundoGeral, "#f8fafc")};" +
            $"--evl-surface:{Cor(l.CorSuperficie, "#ffffff")};" +
            $"--evl-text:{Cor(l.CorTexto, "#1e293b")};" +
            $"--evl-heading:{Cor(l.CorTitulos, "#0f172a")};" +
            $"--evl-link:{Cor(l.CorLinks, "var(--evl-primary)")};" +
            $"--evl-soft:{Mix(prim, sup, 0.09)};" +
            $"--evl-on-primary:{Contraste(prim)};" +
            $"--evl-font-body:{Familia(l.FonteCorpo)};" +
            $"--evl-font-head:{Familia(l.FonteTitulos)};" +
            $"--evl-fs:{l.TamanhoFonte}px;" +
            $"--evl-lh:{lh};" +
            $"--evl-hw:{l.PesoTitulos};" +
            $"--evl-radius:{l.Raio}px;" +
            $"--evl-shadow:{SombraCss(l.Sombra)};" +
            $"--evl-gap:{l.EspacoSecoes}px;" +
            $"--evl-maxw:{l.LarguraMaxima}px;";
    }

    public static string ClassesRaiz(PostLayout l) =>
        "evl-root" + (l.Animacoes ? " evl-anim-on" : "") + $" evl-h2-{(string.IsNullOrWhiteSpace(l.EstiloTitulosConteudo) ? "linha" : l.EstiloTitulosConteudo)}";

    // ─────────────────────────────────────────────────────────────────
    //  Documento final (site)
    // ─────────────────────────────────────────────────────────────────
    public static string Documento(PostLayout l, RenderContexto ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<!-- EvolutTech Page Builder v2 -->");
        sb.Append(FontesLink(l));
        sb.Append("<style>").Append(Css).Append("</style>");
        sb.Append($"<div class=\"{ClassesRaiz(l)}\" style=\"{H(VarsCss(l))}\">");
        if (l.BarraProgresso) sb.Append(BarraProgressoHtml);
        Preparar(l, ctx);
        var visiveis = l.Secoes.OrderBy(x => x.Ordem).Where(x => !x.Oculta).ToList();
        foreach (var s in visiveis) sb.Append(Secao(s, l, ctx, visiveis));
        sb.Append("</div>");
        return sb.ToString();
    }

    public const string BarraProgressoHtml = "<div class=\"evl-progress\" aria-hidden=\"true\"><span></span></div>";
}
