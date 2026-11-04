using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

public class ParametroDinamicoModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe o parametro.")]
    [StringLength(200, ErrorMessage = "O parametro deve ter no maximo 200 caracteres.")]
    public string Parametro { get; set; } = "";

    [Required(ErrorMessage = "Informe o grupo.")]
    [StringLength(20, ErrorMessage = "O grupo deve ter no maximo 20 caracteres.")]
    public string Grupo { get; set; } = "";

    [StringLength(2000, ErrorMessage = "A descricao deve ter no maximo 2000 caracteres.")]
    public string Descricao { get; set; } = "";

    [StringLength(4000, ErrorMessage = "O valor deve ter no maximo 4000 caracteres.")]
    public string Valor { get; set; } = "";

    [StringLength(100, ErrorMessage = "O valor padrao deve ter no maximo 100 caracteres.")]
    public string ValorPadrao { get; set; } = "";

    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}
