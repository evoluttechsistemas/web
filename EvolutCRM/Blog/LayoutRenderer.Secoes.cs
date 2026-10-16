using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Blog;

public static partial class LayoutRenderer
{
    // ─────────────────────────────────────────────────────────────────
    //  Um bloco completo (wrapper + fundo + divisores + conteúdo)
    // ─────────────────────────────────────────────────────────────────
    public static string Secao(PageSection s, PostLayout l, RenderContexto ctx, IReadOnlyList<PageSection> lista)
    {
        var idx = -1;
        for (var i = 0; i < lista.Count; i++) if (lista[i].Id == s.Id) { idx = i; break; }
        var anterior = idx > 0 ? lista[idx - 1] : null;
        var proximo = idx >= 0 && idx < lista.Count - 1 ? lista[idx + 1] : null;

        var cartao = s.Formato == "cartao";
        var accentHex = Hex(string.IsNullOrWhiteSpace(s.CorDestaque) ? l.CorPrimaria : s.CorDestaque, l, "#2563eb");
        var raio = s.Raio >= 0 ? $"{s.Raio}px" : "var(--evl-radius)";
        var sombra = s.Sombra is "tema" or "" ? "var(--evl-shadow)" : SombraCss(s.Sombra);
        var largura = s.Largura switch
        {
            "estreita" => $"{Math.Min(640, l.LarguraMaxima)}px",
            "larga" => $"{Math.Max(l.LarguraMaxima + 320, 1080)}px",
            "total" => "100%",
            _ => $"{l.LarguraMaxima}px"
        };
        var alinhamento = s.Alinhamento switch { "centro" => "evl-center", "direita" => "evl-right", _ => "evl-left" };
        var anim = s.Animacao is "tema" or "" ? (l.Animacoes ? "subir" : "nenhuma") : s.Animacao;

        // ── variáveis do bloco
        var vars = new StringBuilder();
        vars.Append($"--s-accent:{Cor(s.CorDestaque, "var(--evl-primary)")};--s-on-accent:{Contraste(accentHex)};");
        vars.Append($"--s-radius:{raio};--s-shadow:{sombra};--s-scale:{(s.EscalaTitulo / 100.0).ToString("0.00", CultureInfo.InvariantCulture)};");
        var corTexto = Cor(s.CorTexto);
        if (corTexto != "") vars.Append($"--s-text:{corTexto};--s-link:var(--s-accent);");
        var corTitulo = Cor(s.CorTitulo, corTexto);
        if (corTitulo != "") vars.Append($"--s-head:{corTitulo};");

        // ── fundo
        var fundo = new StringBuilder();
        var camadas = new StringBuilder();
        var fundoTipo = s.FundoTipo;
        var imgFundo = fundoTipo == "capa" ? ctx.Capa : s.ImagemFundo;
        if (fundoTipo is "imagem" or "capa" && string.IsNullOrWhiteSpace(imgFundo)) fundoTipo = "gradiente";

        switch (fundoTipo)
        {
            case "nenhum":
                break;
            case "gradiente":
                fundo.Append($"background:linear-gradient({s.GradienteAngulo}deg,{Cor(s.CorFundo, "var(--evl-primary)")},{Cor(s.CorFundo2, "var(--evl-secondary)")});");
                break;
            case "imagem":
            case "capa":
                fundo.Append($"background:{Cor(s.CorOverlay, "#0f172a")};");
                camadas.Append($"<div class=\"evl-bgimg\" style=\"background-image:url('{H(Url(imgFundo, true)).Replace("'", "%27")}');{(s.FundoFixo ? "background-attachment:fixed;" : "")}\"></div>");
                camadas.Append($"<div class=\"evl-overlay\" style=\"background:{Cor(s.CorOverlay, "#0f172a")};opacity:{(s.OverlayOpacidade / 100.0).ToString("0.00", CultureInfo.InvariantCulture)}\"></div>");
                break;
            default:
                var bg = Cor(s.CorFundo, cartao ? "var(--evl-surface)" : "");
                if (bg != "") fundo.Append($"background:{bg};");
                break;
        }

        // ── espaçamento / borda
        var padTopo = s.PaddingV;
        var padBase = s.PaddingBase >= 0 ? s.PaddingBase : s.PaddingV;
        var temDivTopo = !cartao && s.DecoracaoTopo is not ("nenhuma" or "");
        var temDivBase = !cartao && s.DecoracaoBase is not ("nenhuma" or "");
        if (temDivTopo) padTopo += 44;
        if (temDivBase) padBase += 44;
        if (s.Tipo == "espaco") { padTopo = 0; padBase = 0; }

        var caixa = new StringBuilder(fundo.ToString());
        caixa.Append($"padding:{padTopo}px 0 {padBase}px;");
        var corBorda = Cor(s.CorBorda, "var(--evl-primary)");
        switch (s.BordaEstilo)
        {
            case "esquerda": caixa.Append($"border-left:{s.BordaEspessura}px solid {corBorda};"); break;
            case "topo": caixa.Append($"border-top:{s.BordaEspessura}px solid {corBorda};"); break;
            case "completa": caixa.Append($"border:{s.BordaEspessura}px solid {corBorda};"); break;
        }
        if (cartao) caixa.Append($"border-radius:{raio};box-shadow:{sombra};max-width:{largura};");
        var altura = s.Tipo == "hero" ? s.Altura switch { "media" => 380, "grande" => 560, "tela" => -1, _ => 0 } : 0;
        if (altura != 0) caixa.Append(altura < 0 ? "min-height:100vh;" : $"min-height:{altura}px;");
        var hbox = altura != 0 ? " evl-hbox" : "";
        var hClasse = altura != 0 ? $" evl-h-{s.Altura}" : "";

        // ── divisores (cor automática = cor do bloco vizinho)
        var divTopo = temDivTopo ? Divisor(s.DecoracaoTopo, Cor(s.CorDecoracao, CorVizinha(anterior, true)), "top") : "";
        var divBase = temDivBase ? Divisor(s.DecoracaoBase, Cor(s.CorDecoracao, CorVizinha(proximo, false)), "bot") : "";

        // ── classes
        var cls = new StringBuilder($"evl-s evl-t-{s.Tipo} evl-fmt-{(cartao ? "cartao" : "faixa")} {alinhamento}");
        if (!string.IsNullOrWhiteSpace(s.Variante)) cls.Append($" evl-v-{Regex.Replace(s.Variante, @"[^a-z0-9-]", "")}");
        if (anim != "nenhuma") cls.Append($" evl-a-{anim}");
        if (s.OcultarMobile) cls.Append(" evl-hide-m");
        if (s.OcultarDesktop) cls.Append(" evl-hide-d");
        if (ctx.Preview && s.Oculta) cls.Append(" pbx-oculta");

        var ancora = Regex.Replace(s.Ancora ?? "", @"[^a-zA-Z0-9_-]", "");
        var idAttr = ancora != "" ? $" id=\"{ancora}\"" : "";
        var pvAttr = ctx.Preview ? $" data-sid=\"{H(s.Id)}\"" : "";
        var wPad = $"padding:0 {s.PaddingLateral}px;";

        var inner = Interno(s, l, ctx);
        var sb = new StringBuilder();

        if (cartao)
        {
            sb.Append($"<section class=\"{cls}{hClasse}\"{idAttr}{pvAttr} style=\"{vars}\">");
            sb.Append($"<div class=\"evl-box{hbox}{hClasse}\" style=\"{caixa}\">{camadas}");
            sb.Append($"<div class=\"evl-w\" style=\"{wPad}\">{inner}</div></div>");
        }
        else
        {
            sb.Append($"<section class=\"{cls}{hbox}{hClasse}\"{idAttr}{pvAttr} style=\"{vars}{caixa}\">{camadas}{divTopo}");
            sb.Append($"<div class=\"evl-w\" style=\"max-width:{(largura == "100%" ? "100%" : $"calc({largura} + {s.PaddingLateral * 2}px)")};{wPad}\">{inner}</div>{divBase}");
        }
        if (ctx.Preview) sb.Append(Ferramentas(s));
        sb.Append("</section>");
        return sb.ToString();
    }

    private static string CorVizinha(PageSection? v, bool vizinhoAcima)
    {
        if (v is null || v.Formato == "cartao") return "var(--evl-bg)";
        return v.FundoTipo switch
        {
            "nenhum" => "var(--evl-bg)",
            "gradiente" => vizinhoAcima ? Cor(v.CorFundo2, "var(--evl-secondary)") : Cor(v.CorFundo, "var(--evl-primary)"),
            "imagem" or "capa" => Cor(v.CorOverlay, "#0f172a"),
            _ => Cor(v.CorFundo, "var(--evl-bg)")
        };
    }

    private static string Divisor(string tipo, string cor, string lado)
    {
        var paths = tipo switch
        {
            "onda" => "<path d=\"M0,0 H1440 V38 C1200,98 960,4 720,40 C480,76 240,10 0,52 Z\"/>",
            "ondas" => "<path opacity=\".35\" d=\"M0,0 H1440 V62 C1180,20 960,96 720,58 C480,20 250,90 0,48 Z\"/><path opacity=\".6\" d=\"M0,0 H1440 V44 C1210,86 980,14 720,44 C460,74 240,18 0,40 Z\"/><path d=\"M0,0 H1440 V24 C1200,62 960,8 720,28 C480,48 240,6 0,26 Z\"/>",
            "curva" => "<path d=\"M0,0 H1440 V4 Q720,118 0,4 Z\"/>",
            "triangulo" => "<path d=\"M0,0 H1440 L720,92 Z\"/>",
            "inclinado" => "<path d=\"M0,0 H1440 L0,92 Z\"/>",
            "zigzag" => $"<path d=\"{Zigzag()}\"/>",
            _ => ""
        };
        if (paths == "") return "";
        return $"<div class=\"evl-div evl-div-{lado}\" aria-hidden=\"true\"><svg viewBox=\"0 0 1440 100\" preserveAspectRatio=\"none\" style=\"fill:{cor}\">{paths}</svg></div>";
    }

    private static string Zigzag()
    {
        var sb = new StringBuilder("M0,0 H1440 V20");
        var alto = true;
        for (var x = 1400; x >= 0; x -= 40) { sb.Append($" L{x},{(alto ? 58 : 20)}"); alto = !alto; }
        sb.Append(" Z");
        return sb.ToString();
    }

    private static string Ferramentas(PageSection s)
    {
        var info = LayoutPresets.Info(s.Tipo);
        return "<div class=\"pbx-tools\" contenteditable=\"false\">" +
               $"<span class=\"pbx-lbl\">{info.Icone} {H(info.Nome)}{(s.Oculta ? " · oculto" : "")}</span>" +
               "<button type=\"button\" data-pb-act=\"up\" title=\"Subir\">↑</button>" +
               "<button type=\"button\" data-pb-act=\"down\" title=\"Descer\">↓</button>" +
               "<button type=\"button\" data-pb-act=\"dup\" title=\"Duplicar\">⧉</button>" +
               $"<button type=\"button\" data-pb-act=\"hide\" title=\"{(s.Oculta ? "Mostrar" : "Ocultar")}\">{(s.Oculta ? "◉" : "◌")}</button>" +
               "<button type=\"button\" data-pb-act=\"del\" title=\"Remover\" class=\"pbx-del\">✕</button>" +
               "</div><button type=\"button\" class=\"pbx-add pbx-add-txt\" data-pb-act=\"add-escrita\" title=\"Escrever um texto abaixo\" contenteditable=\"false\">✍</button>" +
               "<button type=\"button\" class=\"pbx-add\" data-pb-act=\"add\" title=\"Inserir bloco abaixo\" contenteditable=\"false\">＋</button>";
    }

    // ─────────────────────────────────────────────────────────────────
    //  Peças reutilizáveis
    // ─────────────────────────────────────────────────────────────────
    private static string Sobretitulo(PageSection s, RenderContexto ctx) =>
        string.IsNullOrWhiteSpace(s.Subtitulo) ? "" : $"<div class=\"evl-eyebrow\"{Ed(ctx, "Subtitulo", s.Subtitulo)}>{T(s.Subtitulo, ctx)}</div>";

    private static string Titulo(PageSection s, RenderContexto ctx, string tag = "h2", string ph = "Título do bloco")
    {
        if (string.IsNullOrWhiteSpace(s.Titulo) && !ctx.Preview) return "";
        return $"<{tag} class=\"evl-t\"{Ed(ctx, "Titulo", s.Titulo, ph)}>{T(s.Titulo, ctx)}</{tag}>";
    }

    private static string Corpo(PageSection s, RenderContexto ctx, string classe = "evl-rich", string ph = "Escreva um texto…", string campo = "Corpo", string? valor = null)
    {
        valor ??= s.Corpo;
        if (string.IsNullOrWhiteSpace(valor) && !ctx.Preview) return "";
        return $"<div class=\"{classe}\"{Ed(ctx, campo, valor, ph)}>{Rico(valor, ctx)}</div>";
    }

    private static string Cabecalho(PageSection s, RenderContexto ctx, bool comCorpo = true) =>
        Sobretitulo(s, ctx) + Titulo(s, ctx) + (comCorpo && (!string.IsNullOrWhiteSpace(s.Corpo) || ctx.Preview)
            ? Corpo(s, ctx, "evl-lead evl-rich", "Texto de apoio (opcional)") : "");

    private static string Botoes(PageSection s, RenderContexto ctx)
    {
        var temB1 = !string.IsNullOrWhiteSpace(s.BotaoTexto);
        var temB2 = !string.IsNullOrWhiteSpace(s.Botao2Texto);
        if (!temB1 && !temB2) return "";
        var estilo = s.BotaoEstilo is "solido" or "contorno" or "claro" or "link" ? s.BotaoEstilo : "solido";
        var estilo2 = estilo == "solido" ? "contorno" : "link";
        var sb = new StringBuilder("<div class=\"evl-btns\">");
        if (temB1) sb.Append($"<a class=\"evl-btn evl-btn-{estilo}\" href=\"{H(Url(s.BotaoLink) is { Length: > 0 } u ? u : "#")}\"><span{Ed(ctx, "BotaoTexto", s.BotaoTexto)}>{T(s.BotaoTexto, ctx)}</span></a>");
        if (temB2) sb.Append($"<a class=\"evl-btn evl-btn-{estilo2}\" href=\"{H(Url(s.Botao2Link) is { Length: > 0 } u2 ? u2 : "#")}\"><span{Ed(ctx, "Botao2Texto", s.Botao2Texto)}>{T(s.Botao2Texto, ctx)}</span></a>");
        return sb.Append("</div>").ToString();
    }

    private static string Imagem(string? url, string alt, RenderContexto ctx, string vazio = "🖼 Adicione uma imagem")
    {
        if (string.IsNullOrWhiteSpace(url))
            return ctx.Preview ? $"<div class=\"evl-vazio\">{vazio}</div>" : "";
        return $"<img src=\"{H(Url(url, true))}\" alt=\"{H(alt)}\" loading=\"lazy\">";
    }

    private static string ComMidia(PageSection s, RenderContexto ctx, string texto)
    {
        if (string.IsNullOrWhiteSpace(s.Imagem)) return texto;
        var img = $"<div class=\"evl-media\">{Imagem(s.Imagem, s.Titulo, ctx)}</div>";
        if (s.ImagemPosicao == "topo") return $"<div class=\"evl-stack\">{img}{texto}</div>";
        return $"<div class=\"evl-split evl-img-{(s.ImagemPosicao == "esquerda" ? "esquerda" : "direita")}\"><div>{texto}</div>{img}</div>";
    }

    private static string ItemEd(RenderContexto ctx, int i, string campo, string? valor, string ph = "") => Ed(ctx, $"Itens.{i}.{campo}", valor, ph);

    // ─────────────────────────────────────────────────────────────────
    //  Conteúdo interno por tipo de bloco
    // ─────────────────────────────────────────────────────────────────
    private static string Interno(PageSection s, PostLayout l, RenderContexto ctx)
    {
        var itens = s.Itens ?? new List<SecaoItem>();
        switch (s.Tipo)
        {
            case "hero":
            {
                var meta = "";
                if (s.MostrarMeta)
                    meta = $"<div class=\"evl-meta\"><span>📅 {H(ctx.Data.ToString("dd 'de' MMMM 'de' yyyy", PtBr))}</span><span>⏱ {ctx.MinutosLeitura} min de leitura</span></div>";
                var texto = Sobretitulo(s, ctx) + Titulo(s, ctx, "h1", "Título principal") +
                            Corpo(s, ctx, "evl-lead evl-rich", "Uma frase de impacto que resume o artigo") + meta + Botoes(s, ctx);
                return ComMidia(s, ctx, texto);
            }

            case "conteudo":
            {
                var p = ctx.Processado;
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(s.Titulo) || !string.IsNullOrWhiteSpace(s.Subtitulo))
                    sb.Append(Sobretitulo(s, ctx)).Append(string.IsNullOrWhiteSpace(s.Titulo) ? "" : Titulo(s, ctx));
                if (s.Sumario) sb.Append(Sumario(ctx));
                var cls = "evl-content" + (s.EstiloCartao ? " evl-card" : "") + (s.LetraCapitular ? " evl-dropcap" : "");
                string corpo;
                if (!ctx.Preview) corpo = MarcadorConteudo;
                else if (string.IsNullOrWhiteSpace(Regex.Replace(p.Html, "<[^>]+>|&nbsp;", "")) && !p.Html.Contains("<img", StringComparison.OrdinalIgnoreCase))
                    corpo = "<div class=\"evl-vazio\">📝 O texto do post aparece aqui.<br><small>Escreva no editor principal — ou use blocos <b>✍ Texto livre</b> para escrever direto no layout.</small></div>";
                else corpo = p.Html;
                sb.Append($"<div class=\"{cls}\">{corpo}</div>");
                return sb.ToString();
            }

            case "escrita":
                return Escrita(s, ctx);

            case "texto":
                return ComMidia(s, ctx, Sobretitulo(s, ctx) + Titulo(s, ctx) + Corpo(s, ctx) + Botoes(s, ctx));

            case "colunas":
            {
                var n = s.Colunas >= 3 ? 3 : 2;
                var sb = new StringBuilder(Sobretitulo(s, ctx) + (string.IsNullOrWhiteSpace(s.Titulo) && !ctx.Preview ? "" : Titulo(s, ctx)));
                sb.Append($"<div class=\"evl-cols\" style=\"--cols:{n}\">");
                sb.Append(Corpo(s, ctx, "evl-rich", "Coluna 1"));
                sb.Append(Corpo(s, ctx, "evl-rich", "Coluna 2", "Coluna2", s.Coluna2));
                if (n == 3) sb.Append(Corpo(s, ctx, "evl-rich", "Coluna 3", "Coluna3", s.Coluna3));
                sb.Append("</div>").Append(Botoes(s, ctx));
                return sb.ToString();
            }

            case "citacao":
            {
                var foto = string.IsNullOrWhiteSpace(s.AutorFoto) ? "" : $"<img src=\"{H(Url(s.AutorFoto, true))}\" alt=\"{H(s.Autor)}\">";
                var cap = string.IsNullOrWhiteSpace(s.Autor) && !ctx.Preview ? "" :
                    $"<figcaption>{foto}<div><b{Ed(ctx, "Autor", s.Autor, "Nome")}>{T(s.Autor, ctx)}</b><small{Ed(ctx, "AutorCargo", s.AutorCargo, "Cargo / empresa")}>{T(s.AutorCargo, ctx)}</small></div></figcaption>";
                return $"<figure class=\"evl-quote\"><span class=\"evl-qmark\" aria-hidden=\"true\">“</span><blockquote{Ed(ctx, "Corpo", s.Corpo, "Uma frase marcante…")}>{T(s.Corpo, ctx)}</blockquote>{cap}</figure>";
            }

            case "caixa":
            {
                var (bgc, ac, fg, bd, ico) = s.Variante switch
                {
                    "sucesso" => ("#f0fdf4", "#16a34a", "#14532d", "#bbf7d0", "✅"),
                    "aviso" => ("#fffbeb", "#d97706", "#78350f", "#fde68a", "⚠️"),
                    "perigo" => ("#fef2f2", "#dc2626", "#7f1d1d", "#fecaca", "🚨"),
                    "dica" => ("#f5f3ff", "#7c3aed", "#3b0764", "#ddd6fe", "✨"),
                    "custom" => (Cor(s.CorFundo, "var(--evl-soft)"), Cor(s.CorBorda, "var(--evl-primary)"), Cor(s.CorTexto, "var(--evl-text)"), "transparent", "💡"),
                    _ => ("var(--evl-soft)", "var(--evl-primary)", "var(--evl-heading)", "color-mix(in srgb,var(--evl-primary) 22%,transparent)", "💡")
                };
                var icone = string.IsNullOrWhiteSpace(s.IconeCaixa) ? ico : s.IconeCaixa;
                var tit = string.IsNullOrWhiteSpace(s.Titulo) && !ctx.Preview ? "" : $"<h3{Ed(ctx, "Titulo", s.Titulo, "Título do destaque")}>{T(s.Titulo, ctx)}</h3>";
                return $"<div class=\"evl-callout\" style=\"--cb-bg:{bgc};--cb-ac:{ac};--cb-fg:{fg};--cb-bd:{bd}\"><span class=\"evl-cico\"{Ed(ctx, "IconeCaixa", s.IconeCaixa)}>{H(icone)}</span><div>{tit}{Corpo(s, ctx)}</div></div>";
            }

            case "checklist":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx));
                sb.Append($"<ul class=\"evl-check\" style=\"--cols:{(s.Colunas >= 2 ? 2 : 1)}\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    var marca = s.Variante switch { "numero" => (i + 1).ToString(), "seta" => "→", "estrela" => "★", _ => "✓" };
                    sb.Append($"<li><span class=\"evl-ck\">{marca}</span><div><strong{ItemEd(ctx, i, "Titulo", it.Titulo, "Item")}>{T(it.Titulo, ctx)}</strong>");
                    if (!string.IsNullOrWhiteSpace(it.Texto) || ctx.Preview) sb.Append($"<p{ItemEd(ctx, i, "Texto", it.Texto, "Detalhe (opcional)")}>{T(it.Texto, ctx)}</p>");
                    sb.Append("</div></li>");
                }
                return sb.Append("</ul>").Append(Botoes(s, ctx)).ToString();
            }

            case "imagem":
            {
                var cap = string.IsNullOrWhiteSpace(s.Legenda) && !ctx.Preview ? "" : $"<figcaption{Ed(ctx, "Legenda", s.Legenda, "Legenda (opcional)")}>{T(s.Legenda, ctx)}</figcaption>";
                return $"{Sobretitulo(s, ctx)}{(string.IsNullOrWhiteSpace(s.Titulo) ? "" : Titulo(s, ctx))}<figure class=\"evl-fig\">{Imagem(s.Imagem, s.Legenda, ctx)}{cap}</figure>";
            }

            case "galeria":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx, false));
                sb.Append($"<div class=\"evl-gal\" style=\"--cols:{Math.Clamp(s.Colunas, 2, 4)}\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    sb.Append("<figure>").Append(Imagem(it.Imagem, it.Titulo, ctx, "🖼"));
                    if (!string.IsNullOrWhiteSpace(it.Titulo)) sb.Append($"<figcaption{ItemEd(ctx, i, "Titulo", it.Titulo)}>{T(it.Titulo, ctx)}</figcaption>");
                    sb.Append("</figure>");
                }
                return sb.Append("</div>").ToString();
            }

            case "video":
            {
                var emb = VideoEmbed(s.VideoUrl);
                var player = emb != "" ? $"<div class=\"evl-video\">{emb}</div>"
                    : ctx.Preview ? "<div class=\"evl-vazio\">▶ Cole o link do YouTube, Vimeo ou .mp4</div>" : "";
                var cap = string.IsNullOrWhiteSpace(s.Legenda) ? "" : $"<p style=\"margin-top:.8em;font-size:.88em;opacity:.72;text-align:center\"{Ed(ctx, "Legenda", s.Legenda)}>{T(s.Legenda, ctx)}</p>";
                return Cabecalho(s, ctx, !string.IsNullOrWhiteSpace(s.Corpo)) + player + cap;
            }

            case "cards":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx));
                sb.Append($"<div class=\"evl-grid\" style=\"--cols:{Math.Clamp(s.Colunas, 1, 4)}\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    sb.Append("<div class=\"evl-card-i\">");
                    if (!string.IsNullOrWhiteSpace(it.Imagem)) sb.Append(Imagem(it.Imagem, it.Titulo, ctx));
                    else if (!string.IsNullOrWhiteSpace(it.Icone)) sb.Append($"<div class=\"evl-ico\"{ItemEd(ctx, i, "Icone", it.Icone)}>{H(it.Icone)}</div>");
                    sb.Append($"<h3{ItemEd(ctx, i, "Titulo", it.Titulo, "Título")}>{T(it.Titulo, ctx)}</h3>");
                    sb.Append($"<p{ItemEd(ctx, i, "Texto", it.Texto, "Descrição")}>{T(it.Texto, ctx)}</p>");
                    if (!string.IsNullOrWhiteSpace(it.Link)) sb.Append($"<a class=\"evl-btn evl-btn-link\" href=\"{H(Url(it.Link))}\">Saiba mais</a>");
                    sb.Append("</div>");
                }
                return sb.Append("</div>").Append(Botoes(s, ctx)).ToString();
            }

            case "numeros":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx));
                sb.Append($"<div class=\"evl-grid\" style=\"--cols:{Math.Clamp(s.Colunas, 1, 4)}\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    sb.Append($"<div class=\"evl-num\"><strong{ItemEd(ctx, i, "Valor", it.Valor, "99%")}>{T(it.Valor, ctx)}</strong><span{ItemEd(ctx, i, "Titulo", it.Titulo, "Rótulo")}>{T(it.Titulo, ctx)}</span>");
                    if (!string.IsNullOrWhiteSpace(it.Texto)) sb.Append($"<small{ItemEd(ctx, i, "Texto", it.Texto)}>{T(it.Texto, ctx)}</small>");
                    sb.Append("</div>");
                }
                return sb.Append("</div>").ToString();
            }

            case "passos":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx));
                sb.Append("<ol class=\"evl-steps\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    sb.Append($"<li class=\"evl-step\"><h3{ItemEd(ctx, i, "Titulo", it.Titulo, "Etapa")}>{T(it.Titulo, ctx)}</h3><p{ItemEd(ctx, i, "Texto", it.Texto, "Descreva a etapa")}>{T(it.Texto, ctx)}</p></li>");
                }
                return sb.Append("</ol>").Append(Botoes(s, ctx)).ToString();
            }

            case "faq":
            {
                var sb = new StringBuilder(Cabecalho(s, ctx));
                sb.Append("<div class=\"evl-faq\">");
                for (var i = 0; i < itens.Count; i++)
                {
                    var it = itens[i];
                    sb.Append($"<details{(i == 0 ? " open" : "")}><summary>{T(it.Titulo, ctx)}</summary><div class=\"evl-faq-a\"{ItemEd(ctx, i, "Texto", it.Texto, "Resposta")}>{Rico(it.Texto, ctx)}</div></details>");
                }
                sb.Append("</div>");
                if (!ctx.Preview && itens.Count > 0) sb.Append(FaqJsonLd(itens, ctx));
                return sb.ToString();
            }

            case "banner":
                return ComMidia(s, ctx, Sobretitulo(s, ctx) + Titulo(s, ctx) + Corpo(s, ctx, "evl-lead evl-rich", "Texto do banner") + Botoes(s, ctx));

            case "cta":
                return Sobretitulo(s, ctx) + Titulo(s, ctx, "h2", "Chamada para ação") + Corpo(s, ctx, "evl-lead evl-rich", "Convide o leitor para o próximo passo") + Botoes(s, ctx);

            case "autor":
            {
                var foto = string.IsNullOrWhiteSpace(s.AutorFoto)
                    ? $"<div class=\"evl-av\">{H(Iniciais(s.Autor))}</div>"
                    : $"<img src=\"{H(Url(s.AutorFoto, true))}\" alt=\"{H(s.Autor)}\">";
                var rotulo = string.IsNullOrWhiteSpace(s.Subtitulo) ? "Escrito por" : s.Subtitulo;
                return $"<div class=\"evl-author\">{foto}<div><small>{T(rotulo, ctx)}</small><h3{Ed(ctx, "Autor", s.Autor, "Nome do autor")}>{T(s.Autor, ctx)}</h3>" +
                       $"<div class=\"evl-role\"{Ed(ctx, "AutorCargo", s.AutorCargo, "Cargo")}>{T(s.AutorCargo, ctx)}</div>{Corpo(s, ctx, "evl-rich", "Mini bio")}{Botoes(s, ctx)}</div></div>";
            }

            case "divider":
            {
                var cor = Cor(s.CorBorda, "color-mix(in srgb,currentColor 22%,transparent)");
                return s.Variante switch
                {
                    "degrade" => $"<hr class=\"evl-hr-degrade\" style=\"--hr-c:{Cor(s.CorBorda, "var(--evl-primary)")}\">",
                    "icone" => $"<div class=\"evl-hr-icone\" style=\"--hr-c:{cor}\"><span>{H(string.IsNullOrWhiteSpace(s.IconeCaixa) ? "✦" : s.IconeCaixa)}</span></div>",
                    "pontos" => $"<div class=\"evl-hr-pontos\" style=\"--hr-c:{Cor(s.CorBorda, "var(--evl-primary)")}\">•••</div>",
                    "duplo" => $"<hr class=\"evl-hr evl-hr-duplo\" style=\"--hr-c:{cor}\">",
                    "pontilhado" => $"<hr class=\"evl-hr\" style=\"--hr-c:{cor};--hr-s:dotted;--hr-w:3px\">",
                    "tracejado" => $"<hr class=\"evl-hr\" style=\"--hr-c:{cor};--hr-s:dashed\">",
                    _ => $"<hr class=\"evl-hr\" style=\"--hr-c:{cor}\">"
                };
            }

            case "espaco":
                return $"<div style=\"height:{s.PaddingV}px\"{(ctx.Preview ? " class=\"pbx-espaco\"" : "")}>{(ctx.Preview ? $"<span>↕ {s.PaddingV}px</span>" : "")}</div>";

            default:
                return Sobretitulo(s, ctx) + Titulo(s, ctx) + Corpo(s, ctx);
        }
    }

    public static string VideoEmbed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        url = url.Trim();
        var yt = Regex.Match(url, @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([\w-]{6,})", RI);
        if (yt.Success)
            return $"<iframe src=\"https://www.youtube.com/embed/{yt.Groups[1].Value}\" title=\"Vídeo\" allow=\"accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" allowfullscreen loading=\"lazy\"></iframe>";
        var vi = Regex.Match(url, @"vimeo\.com/(?:video/)?(\d+)", RI);
        if (vi.Success)
            return $"<iframe src=\"https://player.vimeo.com/video/{vi.Groups[1].Value}\" title=\"Vídeo\" allowfullscreen loading=\"lazy\"></iframe>";
        if (Regex.IsMatch(url, @"\.(mp4|webm|ogg)(\?|$)", RI))
            return $"<video controls preload=\"metadata\" src=\"{H(Url(url))}\"></video>";
        return "";
    }

    private static string FaqJsonLd(List<SecaoItem> itens, RenderContexto ctx)
    {
        static string J(string v) => System.Text.Json.JsonSerializer.Serialize(v).Replace("</", "<\\/");
        var ents = itens.Where(i => !string.IsNullOrWhiteSpace(i.Titulo))
            .Select(i => $"{{\"@type\":\"Question\",\"name\":{J(Tokens(i.Titulo, ctx))},\"acceptedAnswer\":{{\"@type\":\"Answer\",\"text\":{J(Tokens(i.Texto, ctx))}}}}}");
        return $"<script type=\"application/ld+json\">{{\"@context\":\"https://schema.org\",\"@type\":\"FAQPage\",\"mainEntity\":[{string.Join(",", ents)}]}}</script>";
    }
}
