namespace EvolutCRM.Blog;

// ════════════════════════════════════════════════════════════════════
//  PAGE BUILDER — modelo do layout visual dos posts
//  • Serializado em JSON na coluna Posts.Layout
//  • Compatível com layouts antigos (v1): todas as propriedades antigas
//    foram mantidas com o mesmo nome. LayoutRenderer.Normalizar() migra.
// ════════════════════════════════════════════════════════════════════

/// <summary>Item de listas (cards, números, passos, FAQ, checklist, galeria).</summary>
public class SecaoItem
{
    public string Id { get; set; } = NovoId();
    public string Icone { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Valor { get; set; } = "";   // números / estatísticas
    public string Imagem { get; set; } = "";  // galeria / cards
    public string Link { get; set; } = "";

    public static string NovoId() => Guid.NewGuid().ToString("N")[..8];
}

public class PageSection
{
    // ── v1 (mantidos) ────────────────────────────────────────────────
    public string Id { get; set; } = SecaoItem.NovoId();
    public string Tipo { get; set; } = "texto";
    public string Titulo { get; set; } = "";
    public string Corpo { get; set; } = "";
    public string Coluna2 { get; set; } = "";
    public string CorFundo { get; set; } = "";          // "" = herda do tema | #hex | var:primaria ...
    public string CorTexto { get; set; } = "";          // "" = herda do tema
    public string CorBorda { get; set; } = "";
    public string DecoracaoTopo { get; set; } = "nenhuma"; // nenhuma | onda | ondas | curva | triangulo | inclinado | zigzag
    public string DecoracaoBase { get; set; } = "nenhuma";
    public int PaddingV { get; set; } = 56;             // espaço superior (px)
    public string Imagem { get; set; } = "";
    public string ImagemPosicao { get; set; } = "direita"; // esquerda | direita | topo
    public bool TextoCentralizado { get; set; } = false;   // legado (sincronizado com Alinhamento)
    public string IconeCaixa { get; set; } = "💡";
    public int Ordem { get; set; }

    // ── v2: conteúdo ────────────────────────────────────────────────
    public string Subtitulo { get; set; } = "";         // sobretítulo / etiqueta acima do título
    public string Coluna3 { get; set; } = "";
    public string Variante { get; set; } = "";          // estilo específico do bloco
    public string Legenda { get; set; } = "";
    public string VideoUrl { get; set; } = "";
    public string Autor { get; set; } = "";
    public string AutorCargo { get; set; } = "";
    public string AutorFoto { get; set; } = "";
    public string BotaoTexto { get; set; } = "";
    public string BotaoLink { get; set; } = "";
    public string BotaoEstilo { get; set; } = "solido"; // solido | contorno | claro | link
    public string Botao2Texto { get; set; } = "";
    public string Botao2Link { get; set; } = "";
    public int Colunas { get; set; } = 3;
    public List<SecaoItem> Itens { get; set; } = new();

    // Bloco "conteudo" (o texto real do post)
    public bool Sumario { get; set; } = false;
    public bool LetraCapitular { get; set; } = false;
    public bool EstiloCartao { get; set; } = false;
    public bool MostrarMeta { get; set; } = true;       // hero: data + tempo de leitura

    // Bloco "escrita" (texto livre escrito direto no preview, HTML já sanitizado)
    public string Html { get; set; } = "";

    // ── v2: fundo ───────────────────────────────────────────────────
    public string FundoTipo { get; set; } = "cor";      // cor | gradiente | imagem | capa | nenhum
    public string CorFundo2 { get; set; } = "var:secundaria";
    public int GradienteAngulo { get; set; } = 135;
    public string ImagemFundo { get; set; } = "";
    public string CorOverlay { get; set; } = "#0f172a";
    public int OverlayOpacidade { get; set; } = 55;
    public bool FundoFixo { get; set; } = false;        // efeito parallax

    // ── v2: cores & tipografia ──────────────────────────────────────
    public string CorTitulo { get; set; } = "";
    public string CorDestaque { get; set; } = "";
    public string Alinhamento { get; set; } = "";       // esquerda | centro | direita
    public int EscalaTitulo { get; set; } = 100;        // %

    // ── v2: dimensões ───────────────────────────────────────────────
    public string Formato { get; set; } = "faixa";      // faixa (largura total) | cartao (bloco flutuante)
    public string Largura { get; set; } = "contida";    // estreita | contida | larga | total
    public int PaddingBase { get; set; } = -1;          // -1 = igual ao superior
    public int PaddingLateral { get; set; } = 24;
    public string Altura { get; set; } = "auto";        // auto | media | grande | tela

    // ── v2: borda & sombra ──────────────────────────────────────────
    public string BordaEstilo { get; set; } = "nenhuma"; // nenhuma | esquerda | topo | completa
    public int BordaEspessura { get; set; } = 4;
    public int Raio { get; set; } = -1;                 // -1 = tema
    public string Sombra { get; set; } = "tema";        // tema | nenhuma | suave | media | forte

    // ── v2: divisores / avançado ────────────────────────────────────
    public string CorDecoracao { get; set; } = "";      // "" = automático (cor do bloco vizinho)
    public string Animacao { get; set; } = "tema";      // tema | nenhuma | fade | subir | zoom | esquerda | direita
    public bool Oculta { get; set; } = false;
    public bool OcultarMobile { get; set; } = false;
    public bool OcultarDesktop { get; set; } = false;
    public string Ancora { get; set; } = "";
}

public class PostLayout
{
    // ── v1 (mantidos) ────────────────────────────────────────────────
    public string TemplateBase { get; set; } = "";
    public List<PageSection> Secoes { get; set; } = new();
    public string CorPrimaria { get; set; } = "#2563eb";
    public string CorFundoGeral { get; set; } = "#f0f6ff";
    public string FonteCorpo { get; set; } = "Plus Jakarta Sans";
    public int LarguraMaxima { get; set; } = 780;

    // ── v2: tema ────────────────────────────────────────────────────
    public int Versao { get; set; } = 1;              // 1 = layout antigo (migrado em Normalizar)
    public string Tema { get; set; } = "evoluttech";
    public string CorSecundaria { get; set; } = "#0ea5e9";
    public string CorSuperficie { get; set; } = "#ffffff";
    public string CorTexto { get; set; } = "#1e293b";
    public string CorTitulos { get; set; } = "#12253f";
    public string CorLinks { get; set; } = "";          // "" = primária
    public string FonteTitulos { get; set; } = "Plus Jakarta Sans";
    public int TamanhoFonte { get; set; } = 17;         // px
    public int AlturaLinha { get; set; } = 180;         // 180 = 1.8
    public int PesoTitulos { get; set; } = 800;
    public int Raio { get; set; } = 14;
    public string Sombra { get; set; } = "suave";       // nenhuma | suave | media | forte
    public int EspacoSecoes { get; set; } = 0;          // px entre blocos
    public string EstiloTitulosConteudo { get; set; } = "linha"; // simples | linha | barra | marcador
    public bool Animacoes { get; set; } = true;
    public bool BarraProgresso { get; set; } = true;

    // ── v2: saída ───────────────────────────────────────────────────
    /// <summary>HTML final gerado pelo LayoutRenderer (o site só injeta o conteúdo do post no marcador).</summary>
    public string? HtmlRenderizado { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
