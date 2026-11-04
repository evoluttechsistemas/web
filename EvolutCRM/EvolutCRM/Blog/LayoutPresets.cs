namespace EvolutCRM.Blog;

// ════════════════════════════════════════════════════════════════════
//  Catálogo de blocos, temas, templates e estilos rápidos do Page Builder
// ════════════════════════════════════════════════════════════════════

public sealed record BlocoInfo(string Tipo, string Icone, string Nome, string Descricao, string Categoria, bool TemItens = false);

public sealed record TemaInfo(
    string Chave, string Nome, string Primaria, string Secundaria, string Fundo, string Superficie,
    string Texto, string Titulos, string FonteTitulos, string FonteCorpo, int Raio, string Sombra,
    string EstiloTitulos, int PesoTitulos = 800);

public sealed record TemplateInfo(string Chave, string Icone, string Nome, string Descricao);

public sealed record EstiloRapido(string Nome, string Fundo, string FundoTipo, string Fundo2, string Texto, string Destaque);

public static class LayoutPresets
{
    // ── Blocos ──────────────────────────────────────────────────────
    public static readonly BlocoInfo[] Blocos =
    {
        new("hero",      "🌟", "Capa (Hero)",       "Título grande, imagem de fundo e botões", "Estrutura"),
        new("escrita",   "✍️", "Texto livre",       "Escreva direto aqui: títulos, listas, links, imagens", "Estrutura"),
        new("conteudo",  "📝", "Conteúdo do post",  "O texto real escrito no editor",          "Estrutura"),
        new("texto",     "📄", "Texto",             "Título + texto, com imagem opcional",     "Texto"),
        new("colunas",   "▥",  "Colunas",           "2 ou 3 colunas lado a lado",              "Texto"),
        new("citacao",   "❝",  "Citação",           "Frase em destaque com autor",             "Texto"),
        new("caixa",     "💡", "Caixa destaque",    "Dica, aviso, sucesso ou alerta",          "Texto"),
        new("checklist", "✅", "Checklist",         "Lista de itens com marcadores",           "Texto", true),
        new("imagem",    "🖼", "Imagem",            "Imagem com legenda",                      "Mídia"),
        new("galeria",   "🧩", "Galeria",           "Grade de imagens",                        "Mídia", true),
        new("video",     "▶",  "Vídeo",             "YouTube, Vimeo ou .mp4",                  "Mídia"),
        new("cards",     "🗂", "Cards",             "Grade de recursos / benefícios",          "Destaques", true),
        new("numeros",   "📊", "Números",           "Estatísticas e resultados",               "Destaques", true),
        new("passos",    "🪜", "Passo a passo",     "Linha do tempo numerada",                 "Destaques", true),
        new("faq",       "❓", "Perguntas (FAQ)",   "Acordeão de perguntas e respostas",       "Destaques", true),
        new("banner",    "📣", "Banner",            "Faixa chamativa com botão",               "Conversão"),
        new("cta",       "🎯", "Chamada (CTA)",     "Convite para ação com botões",            "Conversão"),
        new("autor",     "👤", "Autor",             "Caixa sobre quem escreveu",               "Conversão"),
        new("divider",   "➖", "Divisor",           "Linha, pontos, ícone ou degradê",         "Separadores"),
        new("espaco",    "↕",  "Espaço",            "Respiro vertical",                        "Separadores"),
    };

    public static readonly string[] Categorias = { "Estrutura", "Texto", "Mídia", "Destaques", "Conversão", "Separadores" };

    public static BlocoInfo Info(string? tipo) =>
        Blocos.FirstOrDefault(b => b.Tipo == tipo) ?? new(tipo ?? "?", "▫", tipo ?? "Bloco", "", "Outros");

    // ── Temas ───────────────────────────────────────────────────────
    public static readonly TemaInfo[] Temas =
    {
        new("evoluttech",  "EvolutTech",   "#2563eb", "#0ea5e9", "#f0f6ff", "#ffffff", "#1e293b", "#12253f", "Plus Jakarta Sans", "Plus Jakarta Sans", 14, "suave", "linha"),
        new("indigo",      "Índigo",       "#6366f1", "#ec4899", "#f8f9fb", "#ffffff", "#1e293b", "#0f172a", "Inter",             "Inter",             12, "suave", "marcador"),
        new("editorial",   "Editorial",    "#b45309", "#78350f", "#faf7f2", "#fffdf8", "#292524", "#1c1917", "Playfair Display",  "Lora",              4,  "nenhuma", "simples", 700),
        new("noturno",     "Noturno",      "#22d3ee", "#a78bfa", "#0b1120", "#111827", "#cbd5e1", "#f8fafc", "Space Grotesk",     "Inter",             16, "forte", "barra", 700),
        new("minimal",     "Minimal",      "#111827", "#6b7280", "#ffffff", "#ffffff", "#374151", "#111827", "Inter",             "Inter",             6,  "nenhuma", "simples", 700),
        new("natureza",    "Natureza",     "#15803d", "#ca8a04", "#f4f7f2", "#ffffff", "#1f2937", "#14532d", "DM Sans",           "Nunito",            18, "suave", "barra"),
        new("sunset",      "Pôr do sol",   "#f97316", "#e11d48", "#fff7ed", "#ffffff", "#3f3f46", "#431407", "Poppins",           "Poppins",           20, "media", "marcador", 700),
        new("corporativo", "Corporativo",  "#0f766e", "#1e3a8a", "#f1f5f9", "#ffffff", "#334155", "#0f172a", "Montserrat",        "IBM Plex Sans",     8,  "suave", "linha", 700),
    };

    public static void AplicarTema(PostLayout l, string chave)
    {
        var t = Temas.FirstOrDefault(x => x.Chave == chave);
        if (t is null) return;
        l.Tema = t.Chave;
        l.CorPrimaria = t.Primaria; l.CorSecundaria = t.Secundaria;
        l.CorFundoGeral = t.Fundo; l.CorSuperficie = t.Superficie;
        l.CorTexto = t.Texto; l.CorTitulos = t.Titulos; l.CorLinks = "";
        l.FonteTitulos = t.FonteTitulos; l.FonteCorpo = t.FonteCorpo;
        l.Raio = t.Raio; l.Sombra = t.Sombra; l.EstiloTitulosConteudo = t.EstiloTitulos;
        l.PesoTitulos = t.PesoTitulos;
    }

    // ── Estilos rápidos (1 clique no bloco) ─────────────────────────
    public static readonly EstiloRapido[] EstilosRapidos =
    {
        new("Transparente", "",               "cor",       "",                "",        ""),
        new("Superfície",   "var:superficie", "cor",       "",                "",        ""),
        new("Suave",        "var:suave",      "cor",       "",                "",        ""),
        new("Primária",     "var:primaria",   "cor",       "",                "#ffffff", "#ffffff"),
        new("Gradiente",    "var:primaria",   "gradiente", "var:secundaria",  "#ffffff", "#ffffff"),
        new("Escuro",       "#0f172a",        "cor",       "",                "#e2e8f0", "var:secundaria"),
        new("Aurora",       "#312e81",        "gradiente", "#0e7490",         "#ffffff", "#67e8f9"),
        new("Pêssego",      "#fff1e6",        "gradiente", "#fde2e4",         "#431407", "#ea580c"),
    };

    public static void AplicarEstilo(PageSection s, EstiloRapido e)
    {
        s.FundoTipo = e.FundoTipo;
        s.CorFundo = e.Fundo;
        if (e.FundoTipo == "gradiente") s.CorFundo2 = e.Fundo2;
        s.CorTexto = e.Texto;
        s.CorTitulo = "";
        s.CorDestaque = e.Destaque;
    }

    // ── Blocos novos com conteúdo de exemplo ────────────────────────
    public static PageSection NovoBloco(string tipo)
    {
        var s = new PageSection { Tipo = tipo, PaddingV = 56, Alinhamento = "esquerda" };
        switch (tipo)
        {
            case "hero":
                s.Titulo = "{titulo}";
                s.Subtitulo = "Blog EvolutTech";
                s.Corpo = "Uma frase de impacto que resume o artigo e convida o leitor a continuar.";
                s.FundoTipo = "gradiente"; s.CorFundo = "var:primaria"; s.CorFundo2 = "var:secundaria";
                s.CorTexto = "#ffffff"; s.CorDestaque = "#ffffff"; s.PaddingV = 88;
                s.Largura = "larga"; s.Animacao = "nenhuma";
                break;
            case "conteudo":
                s.PaddingV = 48; s.Sumario = true;
                break;
            case "escrita":
                s.PaddingV = 28; s.FundoTipo = "nenhum";
                break;
            case "texto":
                s.Titulo = "Um título para esta seção";
                s.Corpo = "Escreva aqui. Dá para usar **negrito**, *itálico*, ==marca-texto== e [links](https://evoluttech.com.br).\n\n- Listas também\n- funcionam assim";
                break;
            case "colunas":
                s.Titulo = "Lado a lado"; s.Colunas = 2;
                s.Corpo = "**Antes:** processos manuais, retrabalho e informações espalhadas.";
                s.Coluna2 = "**Depois:** tudo centralizado, com indicadores em tempo real.";
                s.Coluna3 = "**Resultado:** mais tempo para o que importa.";
                break;
            case "citacao":
                s.Corpo = "A melhor forma de prever o futuro é criá-lo.";
                s.Autor = "Peter Drucker"; s.AutorCargo = "Consultor de gestão";
                s.Alinhamento = "centro"; s.Variante = "classica"; s.Largura = "estreita";
                break;
            case "caixa":
                s.Titulo = "Dica importante"; s.Variante = "info"; s.IconeCaixa = ""; s.PaddingV = 24;
                s.Corpo = "Destaque aqui uma informação que o leitor não pode perder.";
                break;
            case "checklist":
                s.Titulo = "O que você vai precisar"; s.Colunas = 1; s.Variante = "check";
                s.Itens = Itens(("Acesso ao sistema", "Usuário com permissão de administrador"), ("Planilha de produtos", "Exportada do seu ERP"), ("15 minutos", "É tudo o que leva"));
                break;
            case "imagem":
                s.Legenda = "Legenda da imagem"; s.Variante = "sombra"; s.PaddingV = 32;
                break;
            case "galeria":
                s.Titulo = "Galeria"; s.Colunas = 3;
                s.Itens = Itens(("Foto 1", ""), ("Foto 2", ""), ("Foto 3", ""));
                break;
            case "video":
                s.Titulo = "Assista"; s.Largura = "contida";
                break;
            case "cards":
                s.Subtitulo = "Recursos"; s.Titulo = "Por que isso importa"; s.Colunas = 3; s.Variante = "cartao"; s.Alinhamento = "centro";
                s.Corpo = "";
                s.Itens = Itens(("Rápido", "Configure em minutos, sem complicação.", "⚡"), ("Seguro", "Seus dados protegidos de ponta a ponta.", "🔒"), ("Integrado", "Conversa com as ferramentas que você já usa.", "🔗"));
                break;
            case "numeros":
                s.Titulo = "Resultados que falam por si"; s.Colunas = 3; s.Alinhamento = "centro"; s.Variante = "simples";
                s.Itens = new()
                {
                    new() { Valor = "+42%", Titulo = "Produtividade", Texto = "no primeiro trimestre" },
                    new() { Valor = "3x", Titulo = "Mais rápido", Texto = "no atendimento" },
                    new() { Valor = "98%", Titulo = "Satisfação", Texto = "dos clientes" },
                };
                break;
            case "passos":
                s.Titulo = "Passo a passo"; s.Variante = "vertical";
                s.Itens = Itens(("Acesse o painel", "Entre com seu usuário e senha."), ("Configure", "Ajuste as opções conforme sua operação."), ("Pronto!", "Acompanhe os resultados em tempo real."));
                break;
            case "faq":
                s.Titulo = "Perguntas frequentes"; s.Largura = "contida";
                s.Itens = Itens(("Preciso instalar algo?", "Não. Tudo funciona direto no navegador."), ("Quanto tempo leva?", "A configuração inicial leva poucos minutos."), ("Tem suporte?", "Sim, nossa equipe atende pelo WhatsApp e pelo HELP."));
                break;
            case "banner":
                s.Subtitulo = "Novidade"; s.Titulo = "Um banner que chama atenção";
                s.Corpo = "Use para anunciar uma novidade, promoção ou material gratuito.";
                s.FundoTipo = "cor"; s.CorFundo = "var:suave"; s.BotaoTexto = "Quero saber mais"; s.BotaoLink = "/";
                s.Formato = "cartao"; s.PaddingV = 44; s.PaddingLateral = 40;
                break;
            case "cta":
                s.Titulo = "Pronto para dar o próximo passo?";
                s.Corpo = "Fale com a nossa equipe e descubra como podemos ajudar.";
                s.BotaoTexto = "Fale com a gente"; s.BotaoLink = "/"; s.Botao2Texto = "Ver mais artigos"; s.Botao2Link = "/blogs";
                s.FundoTipo = "gradiente"; s.CorFundo = "var:primaria"; s.CorFundo2 = "var:secundaria";
                s.CorTexto = "#ffffff"; s.CorDestaque = "#ffffff"; s.Alinhamento = "centro"; s.PaddingV = 72;
                break;
            case "autor":
                s.Autor = "Equipe EvolutTech"; s.AutorCargo = "Especialistas em gestão";
                s.Corpo = "Conteúdos práticos para quem quer vender mais e trabalhar melhor.";
                s.Formato = "cartao"; s.FundoTipo = "cor"; s.CorFundo = "var:superficie"; s.PaddingV = 32; s.PaddingLateral = 32;
                break;
            case "divider":
                s.Variante = "degrade"; s.PaddingV = 24; s.Largura = "estreita";
                break;
            case "espaco":
                s.PaddingV = 40; s.Animacao = "nenhuma";
                break;
        }
        return s;
    }

    private static List<SecaoItem> Itens(params (string Titulo, string Texto)[] xs) =>
        xs.Select(x => new SecaoItem { Titulo = x.Titulo, Texto = x.Texto }).ToList();

    private static List<SecaoItem> Itens(params (string Titulo, string Texto, string Icone)[] xs) =>
        xs.Select(x => new SecaoItem { Titulo = x.Titulo, Texto = x.Texto, Icone = x.Icone }).ToList();

    public static SecaoItem NovoItem(string tipo) => tipo switch
    {
        "numeros" => new SecaoItem { Valor = "100+", Titulo = "Novo número" },
        "cards" => new SecaoItem { Icone = "⭐", Titulo = "Novo card", Texto = "Descrição curta." },
        "faq" => new SecaoItem { Titulo = "Nova pergunta?", Texto = "Resposta." },
        "passos" => new SecaoItem { Titulo = "Nova etapa", Texto = "O que fazer aqui." },
        "galeria" => new SecaoItem { Titulo = "" },
        _ => new SecaoItem { Titulo = "Novo item" }
    };

    // ── Templates de página ─────────────────────────────────────────
    public static readonly TemplateInfo[] Templates =
    {
        new("montar",    "🧱", "Montar e escrever", "Escreva direto no layout, intercalando blocos"),
        new("artigo",    "📝", "Artigo",          "Capa + sumário + dica + autor + CTA"),
        new("magazine",  "📰", "Revista",         "Capa com foto, letra capitular, citação"),
        new("minimal",   "✍️", "Minimal",         "Só o texto, limpo e elegante"),
        new("tutorial",  "🎓", "Tutorial",        "Checklist, passo a passo e FAQ"),
        new("case",      "🏆", "Case de sucesso", "Resultados em números + depoimento"),
        new("novidade",  "🚀", "Novidade",        "Anúncio de recurso com cards e vídeo"),
        new("landing",   "🎯", "Landing page",    "Página de conversão completa"),
    };

    public static List<PageSection> Template(string chave)
    {
        PageSection B(string tipo, Action<PageSection>? cfg = null) { var s = NovoBloco(tipo); cfg?.Invoke(s); return s; }

        var lista = chave switch
        {
            "montar" => new List<PageSection>
            {
                B("hero", s => { s.DecoracaoBase = "onda"; }),
                B("escrita", s => { s.Sumario = true; s.PaddingV = 44; s.Html = "<h2>Introdução</h2><p>Clique aqui e comece a escrever. Selecione um trecho para formatar — ou use atalhos: <strong>##</strong> + espaço vira título, <strong>-</strong> + espaço vira lista.</p>"; }),
                B("caixa", s => { s.Variante = "dica"; s.Titulo = "Dica"; s.Corpo = "Intercale blocos de destaque entre os trechos de texto."; s.PaddingV = 12; }),
                B("escrita", s => { s.Html = "<h2>Desenvolvimento</h2><p>Continue o artigo aqui. Use o ＋ entre os blocos para inserir imagens, citações, vídeos…</p>"; }),
                B("citacao", s => { s.PaddingV = 32; }),
                B("escrita", s => { s.PaddingBase = 48; s.Html = "<h2>Conclusão</h2><p>Feche o raciocínio e convide o leitor para o próximo passo.</p>"; }),
                B("cta", s => { s.DecoracaoTopo = "curva"; }),
            },
            "artigo" => new List<PageSection>
            {
                B("hero", s => { s.DecoracaoBase = "onda"; }),
                B("conteudo", s => s.Sumario = true),
                B("caixa", s => { s.Variante = "dica"; s.Titulo = "Resumo rápido"; s.Corpo = "Os três pontos principais deste artigo, em uma frase cada."; }),
                B("autor"),
                B("cta", s => { s.DecoracaoTopo = "curva"; }),
            },
            "magazine" => new List<PageSection>
            {
                B("hero", s => { s.FundoTipo = "capa"; s.Altura = "grande"; s.OverlayOpacidade = 55; s.Alinhamento = "centro"; s.Subtitulo = "Reportagem"; s.Largura = "contida"; }),
                B("conteudo", s => { s.LetraCapitular = true; s.EstiloCartao = true; s.Sumario = false; s.PaddingV = 40; s.Largura = "contida"; }),
                B("citacao", s => { s.Variante = "destaque"; s.Alinhamento = "esquerda"; s.Largura = "contida"; }),
                B("divider", s => { s.Variante = "icone"; s.IconeCaixa = "✦"; }),
                B("cta"),
            },
            "minimal" => new List<PageSection>
            {
                B("hero", s => { s.FundoTipo = "nenhum"; s.CorTexto = ""; s.CorDestaque = ""; s.Subtitulo = "{data}"; s.Corpo = ""; s.PaddingV = 72; s.PaddingBase = 8; s.MostrarMeta = false; s.Largura = "contida"; }),
                B("divider", s => { s.Variante = "linha"; s.PaddingV = 8; s.Largura = "contida"; }),
                B("conteudo", s => { s.Sumario = false; }),
            },
            "tutorial" => new List<PageSection>
            {
                B("hero", s => { s.Subtitulo = "Tutorial"; s.DecoracaoBase = "inclinado"; }),
                B("checklist", s => { s.Formato = "cartao"; s.FundoTipo = "cor"; s.CorFundo = "var:superficie"; s.PaddingV = 32; s.PaddingLateral = 36; s.Colunas = 2; }),
                B("conteudo", s => s.Sumario = true),
                B("passos", s => { s.FundoTipo = "cor"; s.CorFundo = "var:suave"; s.Subtitulo = "Resumo"; }),
                B("faq"),
                B("cta"),
            },
            "case" => new List<PageSection>
            {
                B("hero", s => { s.Subtitulo = "Case de sucesso"; s.FundoTipo = "cor"; s.CorFundo = "#0f172a"; s.CorTexto = "#e2e8f0"; s.CorTitulo = "#ffffff"; s.CorDestaque = "var:secundaria"; s.ImagemPosicao = "direita"; s.DecoracaoBase = "ondas"; }),
                B("numeros", s => { s.Variante = "cartao"; s.PaddingV = 24; }),
                B("conteudo", s => s.Sumario = false),
                B("citacao", s => { s.Corpo = "Em três meses a operação mudou completamente. Hoje decidimos com dados, não com achismo."; s.Autor = "Nome do cliente"; s.AutorCargo = "Diretor, Empresa X"; s.FundoTipo = "cor"; s.CorFundo = "var:suave"; }),
                B("cta"),
            },
            "novidade" => new List<PageSection>
            {
                B("banner", s => { s.Subtitulo = "Novidade"; s.Titulo = "{titulo}"; s.Corpo = "Conheça o novo recurso que acabou de chegar."; s.FundoTipo = "gradiente"; s.CorFundo = "#312e81"; s.CorFundo2 = "#0e7490"; s.CorTexto = "#ffffff"; s.CorDestaque = "#67e8f9"; s.BotaoTexto = ""; s.Formato = "cartao"; s.Largura = "larga"; s.PaddingV = 64; s.Animacao = "zoom"; }),
                B("conteudo", s => s.Sumario = false),
                B("cards"),
                B("video"),
                B("cta"),
            },
            "landing" => new List<PageSection>
            {
                B("hero", s => { s.Altura = "grande"; s.Alinhamento = "centro"; s.BotaoTexto = "Começar agora"; s.BotaoLink = "/"; s.Botao2Texto = "Saiba mais"; s.Botao2Link = "#conteudo"; s.BotaoEstilo = "solido"; s.MostrarMeta = false; s.DecoracaoBase = "curva"; }),
                B("numeros", s => { s.Titulo = ""; s.Variante = "divisoria"; s.PaddingV = 24; }),
                B("cards", s => { s.FundoTipo = "nenhum"; }),
                B("conteudo", s => { s.Ancora = "conteudo"; s.Sumario = false; s.EstiloCartao = true; }),
                B("faq", s => { s.FundoTipo = "cor"; s.CorFundo = "var:suave"; }),
                B("cta", s => { s.Formato = "cartao"; s.Largura = "larga"; }),
            },
            _ => new List<PageSection> { B("conteudo") }
        };

        for (var i = 0; i < lista.Count; i++) lista[i].Ordem = i;
        return lista;
    }
}
