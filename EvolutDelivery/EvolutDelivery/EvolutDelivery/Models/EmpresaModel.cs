namespace EvolutDelivery.Models
{
    public class EmpresaModel
    {
        public int Codigo { get; set; }
        public string Fantasia { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }
}