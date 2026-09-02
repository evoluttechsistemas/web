namespace EvolutCRM.Models
{
    public class CardModels
    {
        public List<AgendaModel> Agendas { get; set; } = new();
        public int Codigo { get; set; }
        public string Descricao { get; set; }
        public int CodCliente { get; set; }
        public int CodEmp { get; set; }
        public string NomeCliente { get; set; }
        public string Email { get; set; }
        public string Telefone { get; set; }
        public string Celular { get; set; }
        public string TelefoneWhatsApp { get; set; }
        public string? ObservacaoCliente { get; set; } = "";

        public bool WhatsApp { get; set; }
        public bool Ligacao { get; set; }
        public bool EmailContato { get; set; }
        public bool Telegram { get; set; }

        public DateTime? DataCriacao { get; set; }
        public DateTime? DataAlteracao { get; set; }
        public DateTime? PrevisaoFechamento { get; set; }

        public string Usuario { get; set; }
        public string Status { get; set; }
        public string Funil { get; set; }

        /// <summary>
        /// Temperatura do lead: "QUENTE", "MORNO", "FRIO", "MUITO FRIO"
        /// </summary>
        public string FaixaLead { get; set; }
        public string Novo { get; set; }
        public string DescricaoUltimaMensagem { get; set; } = "";



        public List<AnotacaoModel> Anotacoes { get; set; } = new();
        public List<ProdutoModel> Produtos { get; set; } = new();
        public List<ServicoModel> Servicos { get; set; } = new();
    }

    public class AnotacaoModel
    {

        public int Codigo { get; set; }
        public DateTime DataHora
        {
            get => _dataHora;
            set => _dataHora = DateTime.SpecifyKind(value, DateTimeKind.Local);
        }
        private DateTime _dataHora;

        public string Texto { get; set; }
        public string Funil { get; set; }      
        public string EnvioCliente { get; set; }
        public string StatusWhatsApp { get; set; }
        public string CaminhoImagem { get; set; }
        public byte[]? ImagemBytes { get; set; }
        public string NomeImagem { get; set; }
        public string Audio { get; set; }
        public string AudioMimeType { get; set; }
        public string Alterado { get; set; }
        public string MensagemExcluida { get; set; }
        public bool IsNova { get; set; } = false;
        public string Usuario { get; set; }
        public int ScoreTemperatura { get; set; }
        public byte[]? AudioBytes { get; set; }
        public string? AudioNome { get; set; }

    }

    public class ProdutoModel
    {
        public int Codigo { get; set; }
        public string Nome { get; set; }
        public string Unidade { get; set; }
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal PrecoTotal { get; set; }
        public decimal PrecoCusto { get; set; }
    }

    public class ServicoModel
    {
        public int Codigo { get; set; }
        public string Nome { get; set; }
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal PrecoTotal { get; set; }
        public decimal PrecoCusto { get; set; }
    }
}
