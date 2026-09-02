namespace EvolutCRM.Models
{
    public class AcessoRemotoModel
    {
        public int Codigo { get; set; }
        public int CodCliente { get; set; }
        public string NomeCliente { get; set; } = "";
        public string NomeComputador { get; set; } = "";
        public string CodigoAcesso { get; set; } = "";
        public bool Online { get; set; }
    }
}
