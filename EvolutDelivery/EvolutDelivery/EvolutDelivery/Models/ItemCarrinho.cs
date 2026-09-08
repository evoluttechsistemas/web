namespace EvolutDelivery.Models
{
    public class ItemCarrinho
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal Preco { get; set; }
        public string PermiteDecimal { get; set; }
        public string ImagemBase64 { get; set; } // base64 da imagem do produto
        public string Observacao { get; set; }

    }

}

