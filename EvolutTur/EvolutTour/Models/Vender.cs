namespace EvolutTur.Models

{



    public class Vender
    {
        public decimal? PercentualComissaoFornecedor { get; set; }
        public decimal? PercentualComissaoVendedor { get; set; }
        public int ActiveTab { get; set; } = 1;
        public string Status { get; set; } = "Em Andamento";

        // Informações básicas
        public string Grupo { get; set; } = "";
        public string Voucher { get; set; } = "";
        public string Consultor { get; set; } = "";
        public DateTime DataVenda { get; set; } = DateTime.Now;
        public string ResponsavelCompra { get; set; } = "";
        public List<Models.VendaViajante> Viajantes { get; set; } = new List<Models.VendaViajante>();

        public string OrcamentoAtendimento { get; set; } = "";

        // Itens de venda
        public string ItemSelecionado { get; set; } = "";
        public decimal ValorItem { get; set; } = 0;
        public string DescricaoItem { get; set; } = "";

        // Passagem aérea
        public string CompanhiaAerea { get; set; } = "";
        public string NumeroVoo { get; set; } = "";
        public string Localizador { get; set; } = "";
        public string OrigemVoo { get; set; } = "";
        public string DestinoVoo { get; set; } = "";
        public DateTime DataVoo { get; set; } = DateTime.Now;

        // Hospedagem
        public string NomeHotel { get; set; } = "";
        public string LocalizacaoHotel { get; set; } = "";
        public DateTime CheckIn { get; set; } = DateTime.Now;
        public DateTime CheckOut { get; set; } = DateTime.Now.AddDays(1);
        public int NumeroQuartos { get; set; } = 1;

        // Pacote turístico
        public string NomePacote { get; set; } = "";
        public string TransportePacote { get; set; } = "";
        public DateTime EmissaoPacote { get; set; } = DateTime.Today;
        public DateTime InicioPacote { get; set; } = DateTime.Now;
        public DateTime FimPacote { get; set; } = DateTime.Now;
        public string ReservaPacote { get; set; } = "";
        public string ServicosInclusosPacote { get; set; } = "";
        public string SubProdutoPacote { get; set; } = "";

        //Seguro Vendas
        public string Seguro { get; set; } = "";
        public DateTime EmissaoSeguro { get; set; } = DateTime.Today;
        public DateTime InicioSeguro { get; set; } = DateTime.Now;
        public DateTime FimSeguro { get; set; } = DateTime.Now;
        public string ReservaSeguro { get; set; } = "";

        //Aluguel de Veiculo
        public string NomeVeiculo { get; set; } = "";
        public DateTime EmissaoVeiculo { get; set; } = DateTime.Today;
        public string ReservaVeiculo { get; set; } = "";
        public string LocalRetiradaVeiculo { get; set; } = "";
        public DateTime DataHoraRetiradaVeiculo { get; set; } = DateTime.Now;
        public string LocalDevolucaoVeiculo { get; set; } = "";
        public DateTime DataHoraDevolucaoVeiculo { get; set; } = DateTime.Now;
        public string ServicosInclusosVeiculo { get; set; } = "";

        //Rodoviário
        public string DescricaoRodoviario { get; set; } = "";
        public string ReservaRodoviario { get; set; } = "";
        public DateTime DataEmissaoRodoviario { get; set; } = DateTime.Today;
        public string CompanhiaRodoviario { get; set; } = "";
        public string ClasseRodoviario { get; set; } = "";
        public string OrigemRodoviario { get; set; } = "";
        public string DestinoRodoviario { get; set; } = "";
        public DateTime SaidaRodoviario { get; set; } = DateTime.Now;
        public DateTime ChegadaRodoviario { get; set; } = DateTime.Now;

        //Trem
        public string DescricaoFerroviario { get; set; } = "";
        public string ReservaFerroviario { get; set; } = "";
        public DateTime DataEmissaoFerroviario { get; set; } = DateTime.Today;
        public string CompanhiaFerroviario { get; set; } = "";
        public string ClasseFerroviario { get; set; } = "";
        public string OrigemFerroviario { get; set; } = "";
        public string DestinoFerroviario { get; set; } = "";
        public DateTime SaidaFerroviario { get; set; } = DateTime.Now;
        public DateTime ChegadaFerroviario { get; set; } = DateTime.Now;

        //Cruzeiro
        public string NavioCruzeiro { get; set; } = "";
        public DateTime EmissaoCruzeiro { get; set; } = DateTime.Today;
        public DateTime InicioCruzeiro { get; set; } = DateTime.Now;
        public DateTime FimCruzeiro { get; set; } = DateTime.Now;
        public string ReservaCruzeiro { get; set; } = "";
        public string NumeroCabineCruzeiro { get; set; } = "";
        public string TipoCabineCruzeiro { get; set; } = "";
        public string AcomodacaoCruzeiro { get; set; } = "";
        public string RegimeCruzeiro { get; set; } = "";
        public string DestinoCruzeiro { get; set; } = "";
        public string ServicosInclusosCruzeiro { get; set; } = "";

        //Passeio
        public string NomePasseio { get; set; } = "";
        public DateTime EmissaoPasseio { get; set; } = DateTime.Today;
        public DateTime InicioPasseio { get; set; } = DateTime.Now;
        public DateTime FimPasseio { get; set; } = DateTime.Now;
        public string ServicosInclusosPasseio { get; set; } = "";
        public string ReservaPasseio { get; set; } = "";

        //Ingresso
        public string Ingresso { get; set; } = "";
        public string ReservaIngresso { get; set; } = "";
        public DateTime EmissaoIngresso { get; set; } = DateTime.Today;
        public DateTime DataUtilizacaoIngresso { get; set; } = DateTime.Now;

        //Educacional
        public string Escola { get; set; } = "";
        public string ProdutoEducacionalEscola { get; set; } = "";
        public string IdiomaEscola { get; set; } = "";
        public string ReservaEscola { get; set; } = "";
        public string CidadeEscola { get; set; } = "";
        public string PaisEscola { get; set; } = "";
        public DateTime EmissaoEscola { get; set; } = DateTime.Today;
        public DateTime InicioEscola { get; set; } = DateTime.Now;
        public DateTime FimEscola { get; set; } = DateTime.Now;

        //Outros
        public string DescricaoOutros { get; set; } = "";
        public string TipoProdutoOutros { get; set; } = "";
        public string ReservaOutros { get; set; } = "";
        public string ServicosInclusosOutros { get; set; } = "";
        public DateTime EmissaoOutros { get; set; } = DateTime.Today;
        public DateTime InicioOutros { get; set; } = DateTime.Today;
        public DateTime FimOutros { get; set; } = DateTime.Today;

        // Pagamento
        public string FormaPagamento { get; set; } = "";
        public string NumeroCartao { get; set; } = "";
        public string NomeCartao { get; set; } = "";
        public string ValidadeCartao { get; set; } = "";
        public string CvvCartao { get; set; } = "";
        public int Parcelas { get; set; } = 1;
        public DateTime VencimentoBoleto { get; set; } = DateTime.Now.AddDays(3);
        public string ChavePix { get; set; } = "CPF";

        public void Clear()
        {
            ResponsavelCompra = string.Empty;
            Consultor = string.Empty;
            Grupo = string.Empty;
            ItemSelecionado = string.Empty;
            FormaPagamento = string.Empty;
            ValorItem = 0;
            Status = string.Empty;
            DataVenda = DateTime.Now;
            Viajantes.Clear();
        }
    }

}