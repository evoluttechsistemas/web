using System.ComponentModel.DataAnnotations;

namespace EvolutVendas.Models
{
    public class UsuarioLogin
    {
        public int Codigo { get; set; }
        [Required(ErrorMessage = "Informe o usuário")]
        public string Usuario { get; set; }
        
        [Required(ErrorMessage = "Informe a senha")]
        public string Senha { get; set; }
        [Required(ErrorMessage = "Informe o CNPJ")]
        public string CNPJ {get; set; }
        public int CodVendedor { get; set; }
    }

    
}
