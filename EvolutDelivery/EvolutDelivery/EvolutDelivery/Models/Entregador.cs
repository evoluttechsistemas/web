namespace EvolutDelivery.Models
{
    public class Entregador
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Telefone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        
        // Veículo
        public string TipoVeiculo { get; set; } = "MOTO"; // MOTO, CARRO, BICICLETA, A_PE
        public string? PlacaVeiculo { get; set; }
        public string? CorVeiculo { get; set; }
        
        // Status
        public bool Ativo { get; set; } = true;
        public bool Disponivel { get; set; } = true;
        public string StatusAtual { get; set; } = "LIVRE"; // LIVRE, EM_ENTREGA, PAUSADO
        
        // Localização atual
        public string? LatitudeAtual { get; set; }
        public string? LongitudeAtual { get; set; }
        public DateTime? UltimaAtualizacaoGPS { get; set; }
        
        // Estatísticas
        public int TotalEntregas { get; set; }
        public decimal AvaliacaoMedia { get; set; }
        public int TotalAvaliacoes { get; set; }
        
        public int CodEmp { get; set; }
        public DateTime DataCadastro { get; set; }
        
        // Helpers
        public string VeiculoFormatado => TipoVeiculo switch
        {
            "MOTO" => "🏍️ Moto",
            "CARRO" => "🚗 Carro",
            "BICICLETA" => "🚴 Bicicleta",
            "A_PE" => "🚶 A pé",
            _ => TipoVeiculo
        };
        
        public string StatusFormatado => StatusAtual switch
        {
            "LIVRE" => "✅ Disponível",
            "EM_ENTREGA" => "🚚 Em entrega",
            "PAUSADO" => "⏸️ Pausado",
            _ => StatusAtual
        };
        
        public bool PodeReceber => Ativo && Disponivel && StatusAtual == "LIVRE";
    }
}
