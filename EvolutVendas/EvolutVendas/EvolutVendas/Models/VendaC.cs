namespace EvolutVendas.Models
{
    public class VendaC
    {
        public int Codigo { get; set; }
        public string Status { get; set; }
        public string CodCliente { get; set; }
        public string CodVendedor { get; set; }
        public string CodEmp { get; set; }
        public decimal TotalVenda { get; set; }
        public string Observacao { get; set; }
        public string CNPJ { get; set; }
        public string DataHoraMovimento { get; set; }
        public string DataMovimento { get; set; }
        public string NomeCliente { get; set; }
        public string Usuario { get; set; }
    }
}
