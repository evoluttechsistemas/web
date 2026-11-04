namespace EvolutTur.Models
{
    public class RelatorioVendas
    {
        public int Codigo { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string Item { get; set; } = string.Empty;
        public string Pagamento { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public decimal Comissao { get; set; }
        public DateTime Data { get; set; }
    }
}
