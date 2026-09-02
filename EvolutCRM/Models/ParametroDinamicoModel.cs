using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

public class ParametroDinamicoModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe o parametro.")]
    [StringLength(80, ErrorMessage = "O parametro deve ter no maximo 80 caracteres.")]
    public string Parametro { get; set; } = "";

    [Required(ErrorMessage = "Informe o grupo.")]
    [StringLength(50, ErrorMessage = "O grupo deve ter no maximo 50 caracteres.")]
    public string Grupo { get; set; } = "";

    [StringLength(350, ErrorMessage = "A descricao deve ter no maximo 350 caracteres.")]
    public string Descricao { get; set; } = "";

    [StringLength(4000, ErrorMessage = "O valor deve ter no maximo 4000 caracteres.")]
    public string Valor { get; set; } = "";

    [StringLength(4000, ErrorMessage = "O valor padrao deve ter no maximo 4000 caracteres.")]
    public string ValorPadrao { get; set; } = "";

    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}
