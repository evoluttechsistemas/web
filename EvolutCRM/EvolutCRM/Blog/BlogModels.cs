namespace EvolutCRM.Blog;

public class Post
{
    public int IdPost { get; set; }
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public DateTime DataCriacao { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public string? Imagem { get; set; }
    public string Status { get; set; } = "";
    public string? Layout { get; set; }
}

public class PostDto
{
    public int IdPost { get; set; }
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public DateTime DataCriacao { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public string? Imagem { get; set; }
    public string Status { get; set; } = "";
}

public class CreatePostDto
{
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public string? Imagem { get; set; }
    public string Status { get; set; } = "";
}

public class UpdatePostDto
{
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public string? Imagem { get; set; }
    public string Status { get; set; } = "";
    public string? Layout { get; set; }
}