namespace EvolutVendas.Models
{
    public class ProdutoModel
    {
        public int Codigo { get; set; }
        public string Descricao { get; set; }
        public decimal PrecoUnitario { get; set; }

        public byte[] Imagem { get; set; }  // vem do banco

        // Propriedade auxiliar para exibir no <img>
        public string ImagemBase64 =>
            Imagem != null ? $"data:image/png;base64,{Convert.ToBase64String(Imagem)}" : null;
    }
}
