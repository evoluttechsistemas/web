namespace EvolutVendas.Models
{
    public class VendaBuscaC
    {
        public int Codigo { get; set; }       
        public string CodCliente { get; set; }       
        public string CodEmp { get; set; }
        public decimal TotalVenda { get; set; }       
        public string CNPJ { get; set; }
        public DateTime DataHoraVenda { get; set; }
        public DateTime DataMovimento { get; set; }
        public string NomeCliente { get; set; }      
    }
}
