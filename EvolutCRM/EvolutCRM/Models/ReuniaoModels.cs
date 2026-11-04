namespace EvolutCRM.Models;

public sealed class ReuniaoModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }
    public string Titulo { get; set; } = "";
    public string CriadoPor { get; set; } = "";
    public DateTime DataCriacao { get; set; }
    public DateTime? DataEncerramento { get; set; }
    public string Status { get; set; } = "Aberta";
    public int LimiteParticipantes { get; set; } = 6;

    // Dados da lista (lobby)
    public int QtdParticipantes { get; set; }
    public int QtdNaSala { get; set; }
    public int QtdMensagens { get; set; }
    public List<string> Participantes { get; set; } = [];
    public List<string> UsuariosNaSala { get; set; } = [];

    // Situação do usuário logado nesta reunião
    public string MeuPapel { get; set; } = "Convidado";
    public string? MinhaResposta { get; set; }
    public bool JaEntrei { get; set; }
    public string ConvidadoPor { get; set; } = "";
    public DateTime DataConvite { get; set; }

    public bool ConvitePendente =>
        Status == "Aberta" && MeuPapel != "Organizador" && !JaEntrei && MinhaResposta is null;

    public TimeSpan? Duracao => DataEncerramento is null ? null : DataEncerramento - DataCriacao;
}

public sealed class ReuniaoParticipanteModel
{
    public int CodReuniao { get; set; }
    public string Usuario { get; set; } = "";
    public string NomeExibicao { get; set; } = "";
    public string Papel { get; set; } = "Convidado";      // Organizador | Convidado
    public string Situacao { get; set; } = "Convidado";   // Na sala | Convidado | Recusou | Saiu
    public bool Online { get; set; }                      // logado no HELP agora
    public bool NaSala { get; set; }
}

public sealed class ReuniaoMensagemModel
{
    public int Codigo { get; set; }
    public int CodReuniao { get; set; }
    public string Usuario { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public DateTime DataHora { get; set; }
}

public sealed class ReuniaoCriarRequest
{
    public string Titulo { get; set; } = "";
    public List<string> UsuariosConvidados { get; set; } = [];
}

/// <summary>Convite que aparece como "chamada recebida" para o usuário.</summary>
public sealed class ReuniaoConviteModel
{
    public int CodReuniao { get; set; }
    public string Titulo { get; set; } = "";
    public string ConvidadoPor { get; set; } = "";
    public DateTime DataConvite { get; set; } = DateTime.Now;
}

/// <summary>Evento disparado entre as telas abertas (todos os usuários) pelo ReuniaoConviteNotifier.</summary>
public sealed class ReuniaoEvento
{
    public string Tipo { get; set; } = "";   // Convite | Recusado | Encerrada | Presenca
    public int CodEmp { get; set; }
    public int CodReuniao { get; set; }
    public string Titulo { get; set; } = "";
    public string Remetente { get; set; } = "";
    public List<string> Destinatarios { get; set; } = [];
}

/// <summary>Helpers visuais compartilhados (cores/iniciais iguais no C# e no JS).</summary>
public static class ReuniaoVisual
{
    private static readonly string[] Cores =
    [
        "#1768DF", "#0F8F7E", "#7C4DDB", "#C2410C", "#B4236C",
        "#2F7D32", "#0E7490", "#9A6B00", "#5B5BD6", "#A23B3B"
    ];

    public static string Cor(string? usuario)
    {
        var u = (usuario ?? "").Trim().ToUpperInvariant();
        var h = 0;
        foreach (var c in u) h = (h * 31 + c) % 100000;
        return Cores[h % Cores.Length];
    }

    public static string Iniciais(string? nome)
    {
        var partes = (nome ?? "").Replace('.', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length switch
        {
            0 => "?",
            1 => partes[0].Length >= 2 ? partes[0][..2].ToUpperInvariant() : partes[0].ToUpperInvariant(),
            _ => $"{partes[0][0]}{partes[^1][0]}".ToUpperInvariant()
        };
    }

    public static string Nome(string? usuario)
    {
        var u = (usuario ?? "").Trim();
        if (u.Length == 0) return "";
        return string.Join(' ', u.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public static string Tempo(DateTime data)
    {
        var d = DateTime.Now - data;
        if (d.TotalSeconds < 60) return "agora";
        if (d.TotalMinutes < 60) return $"há {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"há {(int)d.TotalHours} h";
        return data.ToString("dd/MM 'às' HH:mm");
    }

    public static string Duracao(TimeSpan? t)
    {
        if (t is null) return "";
        var v = t.Value;
        if (v.TotalMinutes < 1) return "menos de 1 min";
        if (v.TotalHours < 1) return $"{(int)v.TotalMinutes} min";
        return $"{(int)v.TotalHours} h {v.Minutes:00} min";
    }
}
