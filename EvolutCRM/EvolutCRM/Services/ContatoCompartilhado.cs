namespace EvolutCRM.Services;

/// <summary>
/// Contato que o cliente compartilhou pelo WhatsApp. O BaileysHelp grava a mensagem assim:
///   📇 Contato compartilhado
///   Nome: Fulano
///   WhatsApp: 5511999999999
/// (um bloco por contato). O ticket lê esse texto e mostra o cartão com o botão "Conversar".
/// </summary>
public static class ContatoCompartilhado
{
    public const string Cabecalho = "📇 Contato compartilhado";

    public sealed record Item(string Nome, string Telefone);

    public static bool EhContato(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) && texto.TrimStart().StartsWith(Cabecalho, StringComparison.Ordinal);

    public static List<Item> Extrair(string? texto)
    {
        var lista = new List<Item>();
        if (!EhContato(texto)) return lista;
        foreach (var bloco in texto!.Replace("\r\n", "\n").Split(Cabecalho, StringSplitOptions.RemoveEmptyEntries))
        {
            var nome = "Contato";
            var telefones = new List<string>();
            foreach (var linha in bloco.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (linha.StartsWith("Nome:", StringComparison.OrdinalIgnoreCase))
                    nome = linha[5..].Trim() is { Length: > 0 } n ? n : nome;
                else if (linha.StartsWith("WhatsApp:", StringComparison.OrdinalIgnoreCase))
                {
                    var d = new string(linha.Where(char.IsAsciiDigit).ToArray());
                    if (d.Length >= 10 && !telefones.Contains(d)) telefones.Add(d);
                }
            }
            if (telefones.Count == 0) lista.Add(new Item(nome, ""));
            else foreach (var t in telefones) lista.Add(new Item(nome, t));
        }
        return lista;
    }

    /// <summary>5511987654321 → +55 (11) 98765-4321</summary>
    public static string Formatar(string telefone)
    {
        var d = new string((telefone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.Length == 0) return "sem número";
        if (d.StartsWith("55") && d.Length is 12 or 13)
        {
            var ddd = d.Substring(2, 2);
            var local = d[4..];
            return $"+55 ({ddd}) {local[..^4]}-{local[^4..]}";
        }
        return "+" + d;
    }
}
