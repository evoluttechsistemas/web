namespace EvolutCRM.Abas;

public class AbaItem
{
    public string Rota { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Icone { get; set; } = "fas fa-window-maximize";
    public Type ComponenteTipo { get; set; } = default!;
    public IDictionary<string, object?> Parametros { get; set; } = new Dictionary<string, object?>();
    public bool Fixa { get; set; }
}