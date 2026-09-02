namespace EvolutCRM.Models;

public sealed class OpenAiSettings
{
    public string ApiKey { get; set; } = "";
    public string? Model { get; set; } // opcional
}