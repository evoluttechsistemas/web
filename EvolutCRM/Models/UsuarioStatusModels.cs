using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

/// <summary>Cadastro de status do usuário (Banheiro, Treinamento...), por empresa.</summary>
public class UsuarioStatusCadastroModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe a descrição.")]
    [StringLength(60, ErrorMessage = "A descrição deve ter no máximo 60 caracteres.")]
    public string Descricao { get; set; } = "";

    public string Cor { get; set; } = "#f59e0b";

    /// <summary>Se a pessoa nesse status conta como disponível para atender.</summary>
    public bool Disponivel { get; set; }

    /// <summary>Se o cliente responder enquanto a pessoa está nesse status, o ticket volta para NOVO.</summary>
    public bool RedistribuiTickets { get; set; }

    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime? DataHoraUltimaGravacao { get; set; }
    public string UsuarioUltimaGravacao { get; set; } = "";
}

/// <summary>Situação atual de um usuário da equipe (para a barra da equipe).</summary>
public class EquipeMembroModel
{
    public string Usuario { get; set; } = "";
    public bool Online { get; set; }
    public int? CodStatus { get; set; }
    public string? StatusDescricao { get; set; }
    public string? StatusCor { get; set; }
    public bool StatusContaDisponivel { get; set; } = true;
    public int? MinutosNoStatus { get; set; }

    /// <summary>Online e (sem status ou num status marcado como disponível).</summary>
    public bool Disponivel => Online && (!CodStatus.HasValue || StatusContaDisponivel);

    public string StatusTexto =>
        !Online ? "Offline"
        : CodStatus.HasValue ? (StatusDescricao ?? "Ocupado")
        : "Disponível";

    public string StatusCorFinal =>
        !Online ? "#94a3b8"
        : CodStatus.HasValue ? (string.IsNullOrWhiteSpace(StatusCor) ? "#f59e0b" : StatusCor!)
        : "#22c55e";

    public string TempoTexto
    {
        get
        {
            if (!Online || !CodStatus.HasValue || !MinutosNoStatus.HasValue) return "";
            var m = Math.Max(0, MinutosNoStatus.Value);
            if (m < 1) return "agora";
            if (m < 60) return $"há {m} min";
            return $"há {m / 60}h{(m % 60):00}";
        }
    }
}
