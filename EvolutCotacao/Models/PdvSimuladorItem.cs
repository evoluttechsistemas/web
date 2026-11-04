namespace EvolutCotacao.Models
{
    public class PdvSimuladorItem
    {
        public int Item { get; set; }
        public string CodigoBarras { get; set; } = "";
        public string Descricao { get; set; } = "";
        public decimal Quantidade { get; set; } = 1m;
        public decimal PrecoUnitario { get; set; } = 10m;
        public decimal Total => Quantidade * PrecoUnitario;
    }
}
