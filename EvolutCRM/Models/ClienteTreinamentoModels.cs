namespace EvolutCRM.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Histórico de treinamento do cliente (tabela ClienteTreinamento)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ClienteTreinamentoItem
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }
    public int CodCliente { get; set; }
    public string Titulo { get; set; } = "";
    public string Modulo { get; set; } = "outro";
    public string? Descricao { get; set; }

    /// <summary>"treinado" ou "pendente" (pendente só faz sentido nos itens do treinamento inicial).</summary>
    public string Status { get; set; } = "treinado";

    public bool Inicial { get; set; }

    /// <summary>inicial | ticket | ligacao | remoto | presencial | manual</summary>
    public string Origem { get; set; } = "manual";

    public int? CodTicketChamadoC { get; set; }
    public DateTime? DataTreinamento { get; set; }
    public DateTime? UltimaData { get; set; }
    public int Vezes { get; set; }
    public string? Atendente { get; set; }
    public string? PessoaTreinada { get; set; }
    public string? Observacao { get; set; }

    public int? CodDoc { get; set; }
    public int? CodEmpDoc { get; set; }
    public string? SlugDoc { get; set; }
    public string? TituloDoc { get; set; }

    /// <summary>Tipo de treinamento (cadastro) de onde veio o item do treinamento inicial.</summary>
    public int? CodTreinamentoTipo { get; set; }

    public bool IncluidoPorIA { get; set; }

    /// <summary>Só na tela: o atendente removeu o tutorial de propósito (não ligar sozinho ao salvar).</summary>
    public bool SemTutorial { get; set; }

    public bool Treinado => Status == "treinado";

    /// <summary>Link público do tutorial (abre sem login).</summary>
    public string? UrlTutorial => string.IsNullOrWhiteSpace(SlugDoc)
        ? null
        : (CodEmpDoc > 0 ? $"/curso-tutorial/{CodEmpDoc}/{Uri.EscapeDataString(SlugDoc)}" : $"/curso-tutorial/{Uri.EscapeDataString(SlugDoc)}");
}

/// <summary>Treinamento que a EvolutTech faz (tela Cadastros → Treinamentos). Vira a lista do "Treinamento inicial" de cada cliente.</summary>
public sealed class TreinamentoTipo
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Informe o nome do treinamento.")]
    [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessage = "Máximo de 200 caracteres.")]
    public string Titulo { get; set; } = "";

    public string Modulo { get; set; } = "outro";
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;

    public int? CodDoc { get; set; }
    public int? CodEmpDoc { get; set; }
    public string? SlugDoc { get; set; }
    public string? TituloDoc { get; set; }

    public DateTime? DataGravacao { get; set; }
    public string? UsuarioGravacao { get; set; }

    public string? UrlTutorial => string.IsNullOrWhiteSpace(SlugDoc)
        ? null
        : (CodEmpDoc > 0 ? $"/curso-tutorial/{CodEmpDoc}/{Uri.EscapeDataString(SlugDoc)}" : $"/curso-tutorial/{Uri.EscapeDataString(SlugDoc)}");
}

/// <summary>Resultado de "Atualizar clientes" na tela de treinamentos.</summary>
public sealed record TreinamentoTipoAplicacao(int Clientes, int Incluidos, int Atualizados, int Removidos);

/// <summary>Item do treinamento inicial padrão (usado só para preencher a tela na primeira vez).</summary>
public sealed class ClienteTreinamentoItemInicial
{
    public string Modulo { get; set; } = "";
    public string Titulo { get; set; } = "";
}

/// <summary>O que a IA encontrou de ensinado num ticket.</summary>
public sealed class TreinamentoDetectado
{
    public string Titulo { get; set; } = "";
    public string Modulo { get; set; } = "outro";
    public string Descricao { get; set; } = "";
}

public static class ClienteTreinamentoModulos
{
    /// <summary>Código → nome na tela (na ordem em que aparecem).</summary>
    public static readonly (string Codigo, string Nome)[] Lista =
    {
        ("cadastros", "Cadastros"),
        ("vender", "Vender"),
        ("financeiro", "Financeiro"),
        ("fiscal", "Fiscal"),
        ("estoque", "Estoque"),
        ("relatorio", "Relatórios"),
        ("configuracao", "Configurações e equipamentos"),
        ("outro", "Outros"),
    };

    public static string Nome(string? codigo)
        => Lista.FirstOrDefault(m => m.Codigo == codigo).Nome ?? "Outros";

    public static string Normalizar(string? codigo)
    {
        var c = (codigo ?? "").Trim().ToLowerInvariant();
        return Lista.Any(m => m.Codigo == c) ? c : "outro";
    }

    public static readonly (string Codigo, string Nome)[] Origens =
    {
        ("ticket", "Ticket"),
        ("ligacao", "Ligação"),
        ("remoto", "Acesso remoto"),
        ("presencial", "Presencial"),
        ("manual", "Outro"),
    };

    public static string NomeOrigem(string? codigo) => codigo switch
    {
        "inicial" => "Treinamento inicial",
        _ => Origens.FirstOrDefault(o => o.Codigo == codigo).Nome ?? "Outro"
    };
}
