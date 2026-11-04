using Microsoft.AspNetCore.Components.Forms;
using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

public class CandidatoTalentoModel
{
    public int Id { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe o nome completo.")]
    public string NomeCompleto { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o telefone/WhatsApp.")]
    public string Telefone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a cidade.")]
    public string Cidade { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o estado.")]
    public string Estado { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a área de interesse.")]
    public string AreaInteresse { get; set; } = string.Empty;

    public string CargoPretendido { get; set; } = string.Empty;
    public string Escolaridade { get; set; } = string.Empty;
    public string Experiencias { get; set; } = string.Empty;
    public string Cursos { get; set; } = string.Empty;
    public string Habilidades { get; set; } = string.Empty;
    public string Disponibilidade { get; set; } = string.Empty;
    public string PretensaoSalarial { get; set; } = string.Empty;
    public string Observacoes { get; set; } = string.Empty;

    [Required(ErrorMessage = "A foto é obrigatória.")]
    public string FotoBase64 { get; set; } = string.Empty;

    public string FotoContentType { get; set; } = "image/jpeg";
    public string Status { get; set; } = "Novo";
    public DateTime DataCadastro { get; set; } = DateTime.Now;

    public string FotoDataUrl =>
        string.IsNullOrWhiteSpace(FotoBase64)
            ? string.Empty
            : $"data:{FotoContentType};base64,{FotoBase64}";
}
