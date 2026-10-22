namespace EvolutDelivery.Models
{
    public class UsuarioLoginModel
    {
        public int Codigo { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public int? CodVendedor { get; set; }
        public string CNPJ { get; set; } = string.Empty;
        public int CodEmp { get; set; }
    }
}