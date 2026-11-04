using System.ComponentModel.DataAnnotations;

namespace EvolutTur.Models
{
    public class Fornecedor
    {
        // Gerais
        public int Codigo { get; set; }

        [Required(ErrorMessage = "Nome Fantasia é obrigatório")]
        public string? Fantasia { get; set; }

        [Required(ErrorMessage = "Razão Social é obrigatório")]
        public string? Razao { get; set; }

        [Required(ErrorMessage = "Tipo de pessoa é obrigatório")]
        public string? TipoPessoa { get; set; }

        public string? CPF { get; set; }

        public string? RG { get; set; }      

        public string? CEP { get; set; }

        public string? UF { get; set; }

        public string? Endereco { get; set; }

        public string? Numero { get; set; }

        public string? Bairro { get; set; }

        public string? Cidade { get; set; }

        public string? Complemento { get; set; }      

        public string? Telefone { get; set; }

        public string? Email { get; set; }

        // Financeiro

        public decimal? Comissao { get; set; }       

        // Observação
        public string? Observacao { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (TipoPessoa == "F")
            {
                if (string.IsNullOrWhiteSpace(CPF))
                    yield return new ValidationResult("CPF é obrigatório para pessoa física.", new[] { nameof(CPF) });
            }
            else if (TipoPessoa == "J")
            {
                if (string.IsNullOrWhiteSpace(CPF))
                    yield return new ValidationResult("CNPJ é obrigatório para pessoa jurídica.", new[] { nameof(CPF) });
            }
        }
    }
}
