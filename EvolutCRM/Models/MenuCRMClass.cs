// Models/MenuCRMClass.cs
namespace EvolutCRM.Models;

public class UserItem
{
    public int Codigo { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class FunnelColumn
{
    public string FunnelCode { get; }
    public string Title { get; }
    public List<FunnelItem> Items { get; } = new();
    public int Count { get; set; }
    


    public FunnelColumn(string code, string title)
    {
        FunnelCode = code;
        Title = title;
    }
}

public class FunnelItem
{
    public int Codigo { get; set; }
    public int codigoTicket { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string UltimaDataHora { get; set; } = string.Empty;
    public int AgendaCount { get; set; }
    public decimal TotalLinha { get; set; }
    public DateTime? DataCriacao { get; set; }

    /// <summary>
    /// Temperatura do lead: "QUENTE", "MORNO", "FRIO", "MUITO FRIO"
    /// </summary>
    public string FaixaLead { get; set; } = string.Empty;
    public string Novo { get; set; } = string.Empty;

    public int ScoreTemperatura { get; set; }
}

public class ImplantacaoItem
{
    public int Codigo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string UltimaDataHora { get; set; } = string.Empty;
}

public class AgendaItem
{
    public int Codigo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime DataAgendamento { get; set; }
    public string HoraAgendamento { get; set; } = "00:00";
    public string Status { get; set; } = string.Empty;
    public int CodCRMC { get; set; }
}

public class CardResumo
{
    public int Codigo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string NomeCliente { get; set; } = string.Empty;
    public string DataCriacao { get; set; } = string.Empty;
    public string DataPrevisaoFechamento { get; set; } = string.Empty;
}

public class StageSet
{
    public string Stage1 { get; set; } = "-";
    public string Stage2 { get; set; } = "-";
    public string Stage3 { get; set; } = "-";
    public string Stage4 { get; set; } = "-";
    public string Stage5 { get; set; } = "-";
    public string Stage6 { get; set; } = "-";
}
