namespace EvolutCotacao.Models
{
    public class ProdutoCotado
    {
        public string Codigo { get; set; }
        public string Nome { get; set; }
        public decimal Quantidade { get; set; }
        public string PrecoTexto { get; set; } = "";
        public decimal PrecoUnitario { get; set; }
        public decimal Total => PrecoUnitario * Quantidade;
        public string Observacao { get; set; } = string.Empty; // Novo campo
    }
}
