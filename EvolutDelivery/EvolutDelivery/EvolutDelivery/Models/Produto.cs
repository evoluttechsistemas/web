namespace EvolutDelivery.Models
{
    public class Produto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
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
        public string PermiteDecimal { get; set; } = "N";
        public string CNPJ { get; set; } = string.Empty;
        public byte[]? Imagem { get; set; }

        private string? _imagemBase64;
        public string? ImagemBase64
        {
            get => _imagemBase64;
            set => _imagemBase64 = value;
        }
    }
}