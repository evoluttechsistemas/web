using System;
using System.ComponentModel.DataAnnotations;

namespace EvolutTur.Validations
{
    public class ValidaCnpj : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var cnpj = value?.ToString()?.Replace(".", "").Replace("/", "").Replace("-", "");
            if (string.IsNullOrWhiteSpace(cnpj) || cnpj.Length != 14 || !long.TryParse(cnpj, out _))
                return new ValidationResult("CNPJ inválido");

            int[] multiplicador1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] multiplicador2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

            string tempCnpj = cnpj[..12];
            int soma = 0;

            for (int i = 0; i < 12; i++)
                soma += int.Parse(tempCnpj[i].ToString()) * multiplicador1[i];

            int resto = soma % 11;
            string digito = (resto < 2 ? 0 : 11 - resto).ToString();

            tempCnpj += digito;
            soma = 0;

            for (int i = 0; i < 13; i++)
                soma += int.Parse(tempCnpj[i].ToString()) * multiplicador2[i];

            resto = soma % 11;
            digito += (resto < 2 ? 0 : 11 - resto).ToString();

            return cnpj.EndsWith(digito) ? ValidationResult.Success : new ValidationResult("CNPJ inválido");
        }
    }
}
