namespace EvolutCRM.Services
{
    public class AdminUsuarioCrmDto
    {
        public int Codigo { get; set; }
        public string Usuario { get; set; } = "";
        public string Inativo { get; set; } = "N";
    }

    public class AdminPermissaoUsuarioDto
    {
        public string Modulo { get; set; } = "";
        public string Permissao { get; set; } = "";
        public bool Ativo { get; set; }
    }

    public class AdminClienteVersaoDto
    {
        public int CodCliente { get; set; }
        public string RazaoSocial { get; set; } = "";
        public string NomeFantasia { get; set; } = "";
        public string Telefone { get; set; } = "";
        public List<AdminComputadorVersaoDto> Computadores { get; set; } = new();
    }

    public class AdminComputadorVersaoDto
    {
        public string NomeComputador { get; set; } = "";
        public string Versao { get; set; } = "";
        public DateTime? DataHoraUltimoAcesso { get; set; }
    }

    public class AdminClienteTarefaDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Apelido { get; set; } = "";
        public DateTime? DataHoraUltimaAberturaTarefas { get; set; }

        public int DiasSemAbrir =>
            DataHoraUltimaAberturaTarefas == null
                ? 999
                : (DateTime.Now.Date - DataHoraUltimaAberturaTarefas.Value.Date).Days;
    }

    public class AdminComissaoDto
    {
        public int CodCliente { get; set; }
        public string Nome { get; set; } = "";
        public string Apelido { get; set; } = "";
        public string Observacao { get; set; } = "";
        public string ObservacaoContaReceber { get; set; } = "";
        public DateTime? DataInstalacao { get; set; }
        public DateTime? DataFimTeste { get; set; }
        public DateTime? DataPagamento { get; set; }
        public bool Selecionado { get; set; } = true;
        public bool EstaTestando { get; set; }

        public bool EstaEmTeste =>
            EstaTestando && DataPagamento == null;

        public string StatusComissao =>
            EstaEmTeste ? "Testando" : "Comissão liberada";

        public string ClienteExibicao =>
            !string.IsNullOrWhiteSpace(Apelido) ? Apelido : Nome;
    }

    public class BaixaReceberClienteDto
    {
        public int Codigo { get; set; }
        public string NomeCliente { get; set; } = "";
        public string Apelido { get; set; } = "";
    }

    public class ContaReceberDto
    {
        public int Codigo { get; set; }
        public string NomeTipoConta { get; set; } = "";
        public DateTime? DataMovimento { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string? NomeCliente { get; set; }
        public decimal? Valor { get; set; }
        public string? Parcela { get; set; }
        public string? TotalParcela { get; set; }
        public string? Observacao { get; set; }
        public DateTime? DataPagamento { get; set; }
        public decimal? ValorPago { get; set; }
        public string? TipoPagamento { get; set; }
        public int CodCliente { get; set; }
        public bool Selecionado { get; set; } = false;

        public string Situacao => DataPagamento != null ? "Pago" : "Aberto";

        public int DiasAtraso
        {
            get
            {
                if (DataVencimento == null || DataPagamento != null)
                    return 0;

                int dias = (DateTime.Today - DataVencimento.Value.Date).Days;
                return dias > 0 ? dias : 0;
            }
        }

        private int DiasAtrasoComJuros
        {
            get
            {
                int dias = DiasAtraso - 2;
                return dias > 0 ? dias : 0;
            }
        }

        public decimal ValorComJuros
        {
            get
            {
                var valor = Valor ?? 0m;
                if (DiasAtrasoComJuros <= 0)
                    return valor;

                decimal juros = valor * (2m / 30m) * DiasAtrasoComJuros / 100m;
                return Math.Round(valor + juros, 2);
            }
        }
    }

    public class ContaBancariaDto
    {
        public int Codigo { get; set; }
        public string Descricao { get; set; } = "";
        public string Agencia { get; set; } = "";
        public string Conta { get; set; } = "";
        public string Banco { get; set; } = "";

        public string ExibicaoCombo =>
            $"{Descricao} — {Banco} | Ag: {Agencia} | Cc: {Conta}";
    }

    public class BaixaReceberInputDto
    {
        public List<int> CodigosContaReceber { get; set; } = new();
        public string NomeCliente { get; set; } = "";
        public DateTime DataPagamento { get; set; } = DateTime.Today;
        public DateTime DataCompensacao { get; set; } = DateTime.Today;
        public decimal ValorTotal { get; set; }
        public decimal Acrescimo { get; set; }
        public string FormaPagamento { get; set; } = "Dinheiro";
        public int? CodContaBancaria { get; set; }
        public string? NumeroCheque { get; set; }
    }

    public class CobrancaClienteDto
    {
        public int CodCliente { get; set; }
        public string NomeCliente { get; set; } = "";
        public string Apelido { get; set; } = "";
        public string Celular { get; set; } = "";
        public decimal ValorTotal { get; set; }
        public decimal ValorTotalComJuros { get; set; }
    }

    public class CobrancaTituloDto
    {
        public int Codigo { get; set; }
        public decimal Valor { get; set; }
        public DateTime DataMovimento { get; set; }
        public DateTime DataVencimento { get; set; }
        public bool EhBoleto { get; set; }

        public string TipoIcone => EhBoleto ? "🧾 Boleto" : "⚡ PIX";

        public int DiasAtraso => (DateTime.Today - DataVencimento.Date).Days;

        private int DiasAtrasoComJuros
        {
            get
            {
                int dias = DiasAtraso - 2;
                return dias > 0 ? dias : 0;
            }
        }

        public decimal ValorComJuros
        {
            get
            {
                if (DiasAtrasoComJuros <= 0)
                    return Valor;

                decimal juros = Valor * (2m / 30m) * DiasAtrasoComJuros / 100m;
                return Math.Round(Valor + juros, 2);
            }
        }
    }

    public class MercadoPagoConfigDto
    {
        public string TipoPIX { get; set; } = "";
        public string EmailMercadoPago { get; set; } = "";
        public string CPFMercadoPago { get; set; } = "";
        public string TokenMercadoPago { get; set; } = "";
    }

    public class LogRemotoDto
    {
        public int Codigo { get; set; }
        public string Titulo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string NomeComputador { get; set; } = "";
        public string Atendente { get; set; } = "";
        public string CodCliente { get; set; } = "";
        public string IpOrigem { get; set; } = "";
        public DateTime DataHora { get; set; }
    }
}