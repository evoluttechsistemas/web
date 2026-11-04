namespace EvolutCRM.Models;

public class DocumentacaoTutorialModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string UrlThumbnail { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Publicado { get; set; } = "N";
    public int Ordem { get; set; }
    public DateTime DataHoraCriacao { get; set; } = DateTime.Now;
    public DateTime DataHoraAlteracao { get; set; } = DateTime.Now;
    public string UsuarioCriacao { get; set; } = "";
    public string UsuarioAlteracao { get; set; } = "";
    public List<DocumentacaoTutorialBlocoModel> Blocos { get; set; } = new();
    public byte[]? Thumbnail { get; set; }
    public string? ThumbnailMime { get; set; }
    public string Sistema { get; set; } = "EVOLUTTECH";

    /// <summary>P = pública (central de ajuda + IA); I = interna (só equipe + IA, o cliente não vê).</summary>
    public string Visibilidade { get; set; } = "P";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EhInterna => Visibilidade == "I";

    /// <summary>
    /// Endereço da capa para usar no &lt;img&gt;: o arquivo (UrlThumbnail) ou, para capas antigas
    /// ainda em binário no banco, /api/thumbnail com a versão para o cache. Null = sem capa.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CapaUrl =>
        !string.IsNullOrWhiteSpace(UrlThumbnail) ? UrlThumbnail
        : Thumbnail != null ? $"/api/thumbnail/{Codigo}?v={DataHoraAlteracao.Ticks}"
        : null;
}
