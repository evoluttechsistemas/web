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