namespace EvolutCRM.Models
{
    public class DashboardData
    {
        public KPIData KPIs { get; set; } = new();
        public List<LeadPorTemperatura> LeadsPorTemperatura { get; set; } = new();
        public List<LeadPorStatus> LeadsPorStatus { get; set; } = new();
        public List<EvolucaoMensal> EvolucaoMensal { get; set; } = new();
        public List<TopUsuarios> TopUsuarios { get; set; } = new();
        public List<UltimaAtividade> UltimasAtividades { get; set; } = new();
    }

    public class TicketDetalheUsuarioDia
    {
        public int Codigo { get; set; }
        public string Assunto { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Usuario { get; set; } = "";
        public string Situacao { get; set; } = "";
        public DateTime DataHoraAbertura { get; set; }
    }

    public class KPIData
    {
        public int TotalLeads { get; set; }
        public int LeadsAbertos { get; set; }
        public int LeadsFechados { get; set; }
        public int LeadsPerdidos { get; set; }
        public decimal TaxaConversao { get; set; }
        public int LeadsEsteMe { get; set; }
        public decimal CrescimentoMensal { get; set; }
    }

    public class LeadPorTemperatura
    {
        public string Temperatura { get; set; }
        public int Quantidade { get; set; }
        public decimal Percentual { get; set; }
        public string Cor { get; set; }
    }

    public class LeadPorStatus
    {
        public string Status { get; set; }
        public int Quantidade { get; set; }
        public decimal Percentual { get; set; }
    }

    public class EvolucaoMensal
    {
        public string Mes { get; set; }
        public string Ano { get; set; }
        public int LeadsCriados { get; set; }
        public int LeadsFechados { get; set; }
        public int LeadsPerdidos { get; set; }
        public int LeadsEmAberto { get; set; }
        public decimal TaxaConversao { get; set; }
        public decimal TaxaPerda { get; set; }
    }

    public class TopUsuarios
    {
        public string Usuario { get; set; }
        public int LeadsAtivos { get; set; }
        public int LeadsFechados { get; set; }
        public decimal TaxaConversao { get; set; }
    }

    public class UltimaAtividade
    {
        public int CodigoCard { get; set; }
        public string Descricao { get; set; }
        public string NomeCliente { get; set; }
        public string Usuario { get; set; }
        public string Acao { get; set; }
        public DateTime DataHora { get; set; }
        public string Funil { get; set; }
        public string FaixaLead { get; set; }
    }

    public class SuporteDashboardData
    {
        public SuporteKPIData KPIs { get; set; } = new();
        public List<TicketPorSituacao> TicketsPorSituacao { get; set; } = new();
        public List<TicketPorUsuario> TicketsPorUsuario { get; set; } = new();
        public List<TicketEvolucaoMensal> EvolucaoMensal { get; set; } = new();
        public List<TicketsPorDiaUsuario> TicketsPorDiaUsuario { get; set; } = new();
    }

    public class SuporteKPIData
    {
        public int TotalAbertos { get; set; }
        public int EmAndamento { get; set; }
        public int PendenteCliente { get; set; }
        public int FinalizadosMes { get; set; }
        public int AbertosMes { get; set; }

        public decimal TempoMedioAbertoMinutos { get; set; }
        public string TempoMedioAbertoTexto { get; set; } = "0min";
        public int TicketsHoje { get; set; }
    }

    public class TicketPorSituacao
    {
        public string Situacao { get; set; } = "";
        public int Quantidade { get; set; }
        public decimal Percentual { get; set; }
    }

    public class TicketPorUsuario
    {
        public string Usuario { get; set; } = "";
        public int Abertos { get; set; }
        public int Finalizados { get; set; }
        public int Total { get; set; }
        public int QtdRespostasComTempo { get; set; }
        public decimal TempoMedioRespostaMinutos { get; set; }
        public string TempoMedioRespostaTexto { get; set; } = "Sem resposta";
        public int AbertosAgora { get; set; }
    }

    public class TicketEvolucaoMensal
    {
        public string Mes { get; set; } = "";
        public int Abertos { get; set; }
        public int Finalizados { get; set; }
    }

    public class TicketsPorDiaUsuario
    {
        public DateTime Data { get; set; }
        public string DiaTexto => Data.ToString("dd/MM/yyyy");
        public string Usuario { get; set; } = "";
        public int Quantidade { get; set; }
    }

    public class PesquisaSatisfacaoData
    {
        public int TotalEnviadas { get; set; }
        public int TotalRespondidas { get; set; }
        public int TotalComNota { get; set; }
        public int TotalErro { get; set; }
        public decimal MediaNota { get; set; }
        public int Nota1 { get; set; }
        public int Nota2 { get; set; }
        public int Nota3 { get; set; }
        public int Nota4 { get; set; }
        public int Nota5 { get; set; }
        public List<PesquisaDetalhe> Detalhes { get; set; } = new();
    }

    public class PesquisaDetalhe
    {
        public int Codigo { get; set; }
        public int CodTicket { get; set; }
        public string Telefone { get; set; } = "";
        public DateTime DataEnvio { get; set; }
        public bool Respondido { get; set; }
        public DateTime? DataResposta { get; set; }
        public int? Nota { get; set; }
        public string Observacao { get; set; } = "";
        public bool Enviado { get; set; }
        public string ErroEnvio { get; set; } = "";
    }
}