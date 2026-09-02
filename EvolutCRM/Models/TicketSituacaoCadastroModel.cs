using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

public class TicketSetorCadastroModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe a descricao.")]
    [StringLength(80, ErrorMessage = "A descricao deve ter no maximo 80 caracteres.")]
    public string Descricao { get; set; } = "";

    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}

public class TicketSituacaoCadastroModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe a descricao.")]
    [StringLength(80, ErrorMessage = "A descricao deve ter no maximo 80 caracteres.")]
    public string Descricao { get; set; } = "";

    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}

public class TicketTipoCadastroModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe a descricao.")]
    [StringLength(80, ErrorMessage = "A descricao deve ter no maximo 80 caracteres.")]
    public string Descricao { get; set; } = "";

    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}
