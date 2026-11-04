namespace EvolutTur.Models
{
    public class VendaD
    {
        public int Codigo { get; set; }
        public int? CodVendaC { get; set; }
        public string Tipo { get; set; }
        public string CompanhiaAerea { get; set; }
        public string Localizador { get; set; }
        public int? NumeroVoo { get; set; }
        public string Origem { get; set; }
        public string Destino { get; set; }
        public DateTime? DataVoo { get; set; }
        public decimal? Valor { get; set; }
        public string Hotel { get; set; }
        public string Reserva { get; set; }
        public DateTime? Checkin { get; set; }
        public DateTime? Checkout { get; set; }
        public int? NumeroQuartos { get; set; }
        public string NomePacote { get; set; }
        public string Transporte { get; set; }
        public DateTime? Emissao { get; set; }
        public string ServicoIncluso { get; set; }
        public string Seguro { get; set; }
        public DateTime? Inicio { get; set; }
        public DateTime? Fim { get; set; }
        public string CategoriaCarro { get; set; }
        public string LocalRetirada { get; set; }
        public string LocalDevolucao { get; set; }
        public DateTime? DataHoraRetirada { get; set; }
        public DateTime? DataHoraDevolucao { get; set; }
        public string Descricao { get; set; }
        public string Companhia { get; set; }
        public string Classe { get; set; }
        public DateTime? DataHoraSaida { get; set; }
        public DateTime? DataHoraChegada { get; set; }
        public string Navio { get; set; }
        public string NumeroCabine { get; set; }
        public string TipoCabine { get; set; }
        public string Acomodacao { get; set; }
        public string Regime { get; set; }
        public string NomePasseio { get; set; }
        public string NomeImgresso { get; set; }
        public DateTime? DataUtilizacao { get; set; }
        public string TipoProduto { get; set; }
    }

}
