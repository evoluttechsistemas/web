namespace EvolutDelivery.Models
{
    public class Produto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty; // vem da coluna Descricao
        public string Unidade { get; set; } = string.Empty;
        public int CodSecao { get; set; }
        public string NomeSecao { get; set; } = string.Empty;
        public decimal PrecoUnitario { get; set; }
        public decimal QuantidadeEmbalagem { get; set; }
        public decimal PrecoCusto { get; set; }
        public decimal PrecoVenda { get; set; }
        public decimal PrecoPromocao { get; set; }
        public decimal Estoque { get; set; }
        public decimal EstoqueAtual { get; set; }
        public string PermiteDecimal { get; set; }
        public string CNPJ { get; set; } = string.Empty;
        public byte[]? Imagem { get; set; }

        public string? ImagemBase64 =>
            Imagem != null && Imagem.Length > 0
                ? $"data:image/png;base64,{Convert.ToBase64String(Imagem)}"
                : null;
    }
}