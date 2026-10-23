namespace EvolutCRM.Models;

public record AiChatRequest
{
    public string Message { get; init; } = "";
    public string? ConversationContext { get; init; }
}

public record AiChatResponse
{
    public string Reply { get; init; } = "";
}

/// <summary>Pedido de sugestão de resposta da tela do ticket.</summary>
public sealed class AiPedidoSugestao
{
    public int CodTicket { get; init; }

    /// <summary>Empresa do ticket: a busca usa só documentação e casos dela.</summary>
    public int CodEmp { get; init; }

    /// <summary>Tudo que o cliente mandou desde a última resposta do atendente (uma mensagem por linha).</summary>
    public string MensagensCliente { get; init; } = "";

    /// <summary>Conversa recente (até 25 mensagens), uma por linha: "[dd/MM HH:mm] Cliente: ...".</summary>
    public string? Conversa { get; init; }

    public string? Assunto { get; init; }

    /// <summary>Códigos (TicketChamadoD) das imagens que o cliente mandou nessas mensagens — no máximo 2 são usadas.</summary>
    public List<int> CodigosImagens { get; init; } = new();

    /// <summary>Versão do sistema do cliente (do ticket), quando houver.</summary>
    public string? Versao { get; init; }

    /// <summary>Usuário que pediu a sugestão (vai para o log).</summary>
    public string? Usuario { get; init; }

    /// <summary>Na avaliação automática: não usar casos deste ticket na busca (senão a IA "cola" a resposta).</summary>
    public int IgnorarTicketNaBusca { get; init; }

    /// <summary>"ticket" (tela do atendente) ou "avaliacao" (teste automático).</summary>
    public string Origem { get; init; } = "ticket";
}

/// <summary>De onde a IA tirou a resposta.</summary>
public sealed class AiFonte
{
    public string Id { get; init; } = "";        // D1, C2...
    public string Tipo { get; init; } = "";      // "doc" ou "caso"
    public string Titulo { get; init; } = "";
    public int CodDoc { get; init; }
    public int CodTicket { get; init; }
    public float Score { get; init; }
}

/// <summary>Resultado completo da sugestão: texto + fontes + confiança + log.</summary>
public sealed class AiSugestaoResultado
{
    public bool Ok { get; init; }
    public string Resposta { get; init; } = "";

    /// <summary>"alta", "media" ou "baixa".</summary>
    public string Confianca { get; init; } = "baixa";

    public List<AiFonte> Fontes { get; init; } = new();

    /// <summary>Código do registro em AiSugestaoLog (0 se não gravou).</summary>
    public long CodLog { get; init; }

    public string PerguntaEntendida { get; init; } = "";
}
