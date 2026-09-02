using System.ComponentModel.DataAnnotations;

namespace EvolutCRM.Models;

public class ClienteCadastroModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [Required(ErrorMessage = "Informe o nome do cliente.")]
    [StringLength(150, ErrorMessage = "O nome deve ter no máximo 150 caracteres.")]
    public string Nome { get; set; } = "";

    [StringLength(80, ErrorMessage = "O apelido deve ter no máximo 80 caracteres.")]
    public string Apelido { get; set; } = "";

    [StringLength(20, ErrorMessage = "O CPF/CNPJ deve ter no máximo 20 caracteres.")]
    public string CpfCnpj { get; set; } = "";

    [StringLength(150, ErrorMessage = "O endereço deve ter no máximo 150 caracteres.")]
    public string Endereco { get; set; } = "";

    [StringLength(20, ErrorMessage = "O número deve ter no máximo 20 caracteres.")]
    public string NumeroEndereco { get; set; } = "";

    [StringLength(80, ErrorMessage = "O bairro deve ter no máximo 80 caracteres.")]
    public string Bairro { get; set; } = "";

    [StringLength(80, ErrorMessage = "A cidade deve ter no máximo 80 caracteres.")]
    public string NomeCidade { get; set; } = "";

    [StringLength(2, ErrorMessage = "Informe a UF com 2 caracteres.")]
    public string UF { get; set; } = "";

    [StringLength(10, ErrorMessage = "O CEP deve ter no máximo 10 caracteres.")]
    public string CEP { get; set; } = "";

    [StringLength(120, ErrorMessage = "O complemento deve ter no máximo 120 caracteres.")]
    public string ComplementoEndereco { get; set; } = "";

    [StringLength(30, ErrorMessage = "O telefone deve ter no máximo 30 caracteres.")]
    public string Telefone { get; set; } = "";

    [StringLength(30, ErrorMessage = "O celular deve ter no máximo 30 caracteres.")]
    public string Celular { get; set; } = "";

    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(120, ErrorMessage = "O e-mail deve ter no máximo 120 caracteres.")]
    public string Email { get; set; } = "";

    [StringLength(500, ErrorMessage = "A observação deve ter no máximo 500 caracteres.")]
    public string Observacao { get; set; } = "";

    public bool Inativo { get; set; }

    public string FisicaJuridica =>
        SomenteDigitos(CpfCnpj).Length > 11 ? "J" : "F";

    private static string SomenteDigitos(string valor) =>
        new((valor ?? "").Where(char.IsDigit).ToArray());
}
