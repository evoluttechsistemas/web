namespace EvolutTur.Models
{
    public class ContaReceber
    {
        public int Codigo { get; set; }
        public int? CodVendaC { get; set; }
        public string Tipo { get; set; }
        public int? NumeroCartao { get; set; }
        public string NomeCartao { get; set; }
        public string Validade { get; set; }
        public string CVV { get; set; }
        public string Parcelas { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string ChavePIX { get; set; }
    }

}
