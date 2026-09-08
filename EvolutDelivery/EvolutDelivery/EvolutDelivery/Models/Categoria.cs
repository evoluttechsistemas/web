namespace EvolutDelivery.Models
{
    public class Categoria
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Icone { get; set; }
        public string? Cor { get; set; }
        public int Ordem { get; set; }
        public bool Ativo { get; set; } = true;
        public int CodEmp { get; set; }
        
        public byte[]? Imagem { get; set; }
        public string? ImagemBase64 => Imagem != null 
            ? $"data:image/png;base64,{Convert.ToBase64String(Imagem)}" 
            : null;
    }
    
    public class Promocao
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public string TipoDesconto { get; set; } = "PERCENTUAL"; // PERCENTUAL, VALOR_FIXO
        public decimal ValorDesconto { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime DataFim { get; set; }
        public bool Ativo { get; set; } = true;
        public int? CodProduto { get; set; }
        public int CodEmp { get; set; }
        
        public bool EstaAtiva => Ativo && 
                                 DateTime.Now >= DataInicio && 
                                 DateTime.Now <= DataFim;
        
        public string DescontoFormatado => TipoDesconto == "PERCENTUAL"
            ? $"{ValorDesconto}% OFF"
            : $"R$ {ValorDesconto:F2} OFF";
    }
    
    public class Avaliacao
    {
        public int Codigo { get; set; }
        public int CodPedido { get; set; }
        public int CodCliente { get; set; }
        public string NomeCliente { get; set; } = string.Empty;
        public int Estrelas { get; set; } // 1 a 5
        public string? Comentario { get; set; }
        public DateTime DataAvaliacao { get; set; }
        public int? CodProduto { get; set; }
        public int? CodEntregador { get; set; }
        
        public string EstrelasSimbolo => new string('⭐', Estrelas);
    }
}
