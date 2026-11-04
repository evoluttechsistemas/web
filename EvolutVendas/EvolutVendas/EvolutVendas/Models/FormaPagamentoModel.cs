namespace EvolutVendas.Models
{
    public class FormaPagamentoModel
    {
        public int Codigo { get; set; }
        public int CodFormaPagamento { get; set; }
        public string Descricao { get; set; } = "";
        public int Parcelas { get; set; }
        public int Dias { get; set; }
        public int CodCliente { get; set; }
        public string TipoPagamento { get; set; }

    }

}

