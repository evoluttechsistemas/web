namespace EvolutCRM.Services;

public static class RefreshTokenStore
{
    private static readonly Dictionary<string, (string Token, DateTime Expira)>
        _tokens = new();

    public static void Salvar(string username, string token, DateTime expira)
    {
        _tokens[username] = (token, expira);
    }

    public static bool Validar(string username, string token)
    {
        if (!_tokens.TryGetValue(username, out var data))
            return false;

        if (data.Token != token)
            return false;

        if (data.Expira < DateTime.UtcNow)
        {
            _tokens.Remove(username);
            return false;
        }

        return true;
    }
}
