namespace EvolutTur.Models
{
    public class VendaC
    {
        public int Codigo { get; set; }
        public int? CodEmp { get; set; }
        public int? CodFornecedor { get; set; }
        public int? CodVendedor { get; set; }
        public DateTime? DataVenda { get; set; }
        public int? CodCliente { get; set; }
        public int? OrcamentoAtendimento { get; set; }
        public DateTime? DataHoraVenda { get; set; }
        public string Usuario { get; set; }
    }
}
