namespace EvolutCRM.Models;

public class DocumentacaoTutorialBlocoModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }
    public int CodDocumentacaoTutorial { get; set; }
    public string Tipo { get; set; } = "TEXTO";
    public string Titulo { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public string UrlMidia { get; set; } = "";
    public string UrlLink { get; set; } = "";
    public int? CodDocumentacaoRelacionada { get; set; }
    public int Ordem { get; set; }
}
