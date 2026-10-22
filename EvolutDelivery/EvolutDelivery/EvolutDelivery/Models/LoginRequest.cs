namespace EvolutDelivery.Models
{
    public class LoginRequest
    {
        public string CNPJ { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }
}