namespace EvolutCRM.Mobile
{
    // ─────────────────────────────────────────────────────────────
    // DTOs do Financeiro do HELP Mobile (/api/mobile/financeiro/...)
    // O app tem uma cópia destes modelos (HelpMobile/Models/FinanceiroModels.cs).
    // ─────────────────────────────────────────────────────────────

    public class FinClienteDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Apelido { get; set; } = "";
    }

    public class FinContaDto
    {
        public int Codigo { get; set; }
        public string TipoConta { get; set; } = "";
        public DateTime? DataMovimento { get; set; }
        public DateTime? DataVencimento { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorComJuros { get; set; }
        public int DiasAtraso { get; set; }
        public string Parcela { get; set; } = "";
        public string TotalParcela { get; set; } = "";
        public string Observacao { get; set; } = "";
        public DateTime? DataPagamento { get; set; }
        public decimal? ValorPago { get; set; }
        public string TipoPagamento { get; set; } = "";
        public bool Aberto { get; set; }
    }

    public class FinContaBancariaDto
    {
        public int Codigo { get; set; }
        public string Descricao { get; set; } = "";
    }

    public class FinBaixaRequest
    {
        public int CodCliente { get; set; }
        public string NomeCliente { get; set; } = "";
        public List<int> Codigos { get; set; } = new();
        public string FormaPagamento { get; set; } = "Dinheiro";
        public decimal Acrescimo { get; set; }
        public DateTime DataPagamento { get; set; } = DateTime.Today;
        public DateTime DataCompensacao { get; set; } = DateTime.Today;
        public int? CodContaBancaria { get; set; }
        public string? NumeroCheque { get; set; }
    }

    public class FinResultadoDto
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = "";
    }

    public class FinCobrancaClienteDto
    {
        public int CodCliente { get; set; }
        public string Nome { get; set; } = "";
        public string Apelido { get; set; } = "";
        public string Celular { get; set; } = "";
        public decimal ValorTotal { get; set; }
    }

    public class FinCobrancaTituloDto
    {
        public int Codigo { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorComJuros { get; set; }
        public DateTime DataMovimento { get; set; }
        public DateTime DataVencimento { get; set; }
        public int DiasAtraso { get; set; }
        public bool EhBoleto { get; set; }
    }

    public class FinCobrancaDetalheDto
    {
        public int CodCliente { get; set; }
        public string Nome { get; set; } = "";
        public string Celular { get; set; } = "";
        public List<FinCobrancaTituloDto> Titulos { get; set; } = new();
        public decimal Total { get; set; }
        public decimal TotalComJuros { get; set; }
        public string MensagemCobranca { get; set; } = "";
        public string MensagemLembrete { get; set; } = "";

        /// <summary>Links wa.me prontos (vazios quando o cliente não tem celular).</summary>
        public string UrlCobranca { get; set; } = "";
        public string UrlLembrete { get; set; } = "";
    }

    public class FinPixDto
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = "";
        public string Link { get; set; } = "";
        public string UrlWhatsApp { get; set; } = "";
    }

    public class FinLogRequest
    {
        /// <summary>cobranca | lembrete | pix</summary>
        public string Tipo { get; set; } = "";
    }
}
