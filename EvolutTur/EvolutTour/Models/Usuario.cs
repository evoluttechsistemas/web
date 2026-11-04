using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvolutTur.Models
{
    public class UsuarioMaster
    {
        public int Codigo { get; set; }

        [Required(ErrorMessage = "Nome é obrigatório")]
        public string Nome { get; set; }

        [Required(ErrorMessage = "E-mail é obrigatório")]
        [EmailAddress(ErrorMessage = "E-mail inválido")]
        public string Usuario { get; set; }

        [MinLength(8, ErrorMessage = "A senha deve ter no mínimo 8 caracteres")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*\W).+$",
            ErrorMessage = "Senha deve conter letra maiúscula, minúscula, número e símbolo")]
        public string? Senha { get; set; }

        [NotMapped]
        [Compare(nameof(Senha), ErrorMessage = "As senhas não coincidem")]
        public string? ConfirmarSenha { get; set; }

       
        public string PermInserir { get; set; }
        public string PermExcluir { get; set; }
        public string PermAlterar { get; set; }

        public string Master { get; set; }

        public bool usuarioMasterBool
        {
            get => Master == "S";
            set => Master = value ? "S" : "N";
        }


        // Propriedades auxiliares (não mapeadas no banco)
        public bool PermInserirBool
        {
            get => PermInserir == "S";
            set => PermInserir = value ? "S" : "N";
        }

        public bool PermAlterarBool
        {
            get => PermAlterar == "S";
            set => PermAlterar = value ? "S" : "N";
        }

        public bool PermExcluirBool
        {
            get => PermExcluir == "S";
            set => PermExcluir = value ? "S" : "N";
        }
    }


}
