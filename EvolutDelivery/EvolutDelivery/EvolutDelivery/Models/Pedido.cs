namespace EvolutDelivery.Models
{
    public class Pedido
    {
        public int Codigo { get; set; }              // Codigo do PedidoDelivery
        public int CodVenda { get; set; }            // Codigo da VendaCAFV
        public int CodCliente { get; set; }

        public string NomeCliente { get; set; } = string.Empty;
        public string TelefoneCliente { get; set; } = string.Empty;
        public string EmailCliente { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;

        // Endereço usado no pedido
        public string EnderecoCompleto { get; set; } = string.Empty;
        public string Bairro { get; set; } = string.Empty;
        public string Cidade { get; set; } = string.Empty;
        public string CEP { get; set; } = string.Empty;
        public string Complemento { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Referencia { get; set; } = string.Empty;

        // Dados financeiros
        public DateTime DataPedido { get; set; }
        public DateTime? DataEntrega { get; set; }
        public decimal ValorSubtotal { get; set; }
        public decimal ValorEntrega { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal ValorTotal { get; set; }

        // Status e controle
        public string Status { get; set; } = "P";
        public string FormaPagamento { get; set; } = string.Empty;
        public string TipoEntrega { get; set; } = "ENTREGA";
        public string Observacao { get; set; } = string.Empty;

        // Entregador
        public int? CodEntregador { get; set; }
        public string NomeEntregador { get; set; } = string.Empty;

        // Rastreamento
        public string? LatitudeEntrega { get; set; }
        public string? LongitudeEntrega { get; set; }
        public DateTime? UltimaAtualizacaoLocalizacao { get; set; }

        // Controle interno
        public string Usuario { get; set; } = "SITE";
        public int CodEmp { get; set; }
        public bool Notificado { get; set; }

        // Itens para UI
        public List<ItemPedido> Itens { get; set; } = new();

        public string StatusFormatado => Status switch
        {
            "PENDENTE" => "⏳ Aguardando Confirmação",
            "CONFIRMADO" => "✅ Confirmado",
            "PREPARANDO" => "👨‍🍳 Em Preparo",
            "SAIU_ENTREGA" => "🚚 Saiu para Entrega",
            "ENTREGUE" => "✓ Entregue",
            "CANCELADO" => "❌ Cancelado",
            _ => Status
        };

        public string StatusClass => Status switch
        {
            "PENDENTE" => "status-pending",
            "CONFIRMADO" => "status-confirmed",
            "PREPARANDO" => "status-preparing",
            "SAIU_ENTREGA" => "status-delivering",
            "ENTREGUE" => "status-delivered",
            "CANCELADO" => "status-cancelled",
            _ => ""
        };

        public bool PodeEditar => Status == "PENDENTE";
        public bool PodeCancelar => Status is "PENDENTE" or "CONFIRMADO";
        public bool EmAndamento => Status is "CONFIRMADO" or "PREPARANDO" or "SAIU_ENTREGA";
    }

    public class ItemPedido
    {
        public int Codigo { get; set; }
        public int CodPedido { get; set; }       // opcional para UI
        public int CodVenda { get; set; }        // VendaCAFV
        public int CodProduto { get; set; }
        public string NomeProduto { get; set; } = string.Empty;
        public decimal PrecoUnitario { get; set; }
        public decimal Quantidade { get; set; }
        public decimal PrecoTotal { get; set; }
        public string Observacao { get; set; } = string.Empty;

        public byte[]? ImagemProduto { get; set; }
        public string? ImagemBase64 => ImagemProduto != null
            ? $"data:image/png;base64,{Convert.ToBase64String(ImagemProduto)}"
            : null;
    }
}