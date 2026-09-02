namespace EvolutCRM.Abas;

public class AbaService
{
    private readonly List<AbaItem> _abas = new();
    public IReadOnlyList<AbaItem> Abas => _abas;
    public string? RotaAtiva { get; private set; }

    public event Action? OnMudanca;

    private static readonly Dictionary<Type, (string Titulo, string Icone)> _meta = new();
    private static readonly Dictionary<string, Type> _rotaParaTipo = new();

    public static void RegistrarMeta(Type tipo, string titulo, string icone, string rota)
    {
        _meta[tipo] = (titulo, icone);
        _rotaParaTipo[Normalizar(rota)] = tipo;
    }

    public void AbrirOuAtivarPorRota(string rotaRelativa)
    {
        rotaRelativa = Normalizar(rotaRelativa);

        // 1. Tenta rota exata
        if (_rotaParaTipo.TryGetValue(rotaRelativa, out var tipo))
        {
            AbrirOuAtivar(tipo!, new Dictionary<string, object?>(), rotaRelativa);
            return;
        }

        var partes = rotaRelativa.Split('/');

        // 2. Tenta prefixo de dois segmentos (ex: "documentacoes/novo", "admin/logs")
        // 2. Tenta prefixo de dois segmentos
        if (partes.Length >= 2)
        {
            var prefixo2 = partes[0] + "/" + partes[1];

            // Se segundo segmento é número, tenta como parâmetro do primeiro tipo com dois segmentos
            if (int.TryParse(partes[1], out var codigoValor))
            {
                // busca qualquer tipo registrado com o mesmo primeiro segmento + "novo"
                var rotaNovo = partes[0] + "/novo";
                if (_rotaParaTipo.TryGetValue(rotaNovo, out tipo))
                {
                    var parametros = new Dictionary<string, object?>
                    {
                        ["Codigo"] = (object?)codigoValor
                    };
                    AbrirOuAtivar(tipo!, parametros, rotaRelativa);
                    return;
                }
            }

            if (_rotaParaTipo.TryGetValue(prefixo2, out tipo))
            {
                AbrirOuAtivar(tipo!, new Dictionary<string, object?>(), rotaRelativa);
                return;
            }
        }

        // 3. Tenta prefixo de um segmento (ex: "ticket/5", "card/3")
        if (partes.Length >= 2)
        {
            var prefixo1 = partes[0];
            if (_rotaParaTipo.TryGetValue(prefixo1, out tipo))
            {
                var parametros = new Dictionary<string, object?>();
                if (int.TryParse(partes[1], out var idValor))
                    parametros["id"] = (object?)idValor;
                AbrirOuAtivar(tipo!, parametros, rotaRelativa);
                return;
            }
        }
    }

    public void AbrirOuAtivar(Type componenteTipo, IDictionary<string, object?> parametros, string rotaRelativa)
    {
        rotaRelativa = Normalizar(rotaRelativa);

        var existente = _abas.FirstOrDefault(a => a.Rota == rotaRelativa);
        if (existente is null)
        {
            var (titulo, icone) = ResolverMeta(componenteTipo, parametros);
            _abas.Add(new AbaItem
            {
                Rota = rotaRelativa,
                Titulo = titulo,
                Icone = icone,
                ComponenteTipo = componenteTipo,
                Parametros = new Dictionary<string, object?>(parametros),
                Fixa = rotaRelativa == ""
            });
        }

        RotaAtiva = rotaRelativa;
        OnMudanca?.Invoke();
    }

    public AbaItem? Fechar(string rotaRelativa)
    {
        rotaRelativa = Normalizar(rotaRelativa);
        var aba = _abas.FirstOrDefault(a => a.Rota == rotaRelativa);
        if (aba is null || aba.Fixa) return null;

        var idx = _abas.IndexOf(aba);
        _abas.Remove(aba);

        AbaItem? proxima = null;
        if (RotaAtiva == rotaRelativa)
        {
            if (_abas.Count > 0)
            {
                proxima = _abas[Math.Min(idx, _abas.Count - 1)];
                RotaAtiva = proxima.Rota;
            }
            else RotaAtiva = null;
        }

        OnMudanca?.Invoke();
        return proxima;
    }

    private (string, string) ResolverMeta(Type tipo, IDictionary<string, object?> parametros)
    {
        if (_meta.TryGetValue(tipo, out var m))
        {
            var titulo = m.Titulo;
            if (parametros.Count > 0 && parametros.Values.First() is { } v)
                titulo = $"{m.Titulo} #{v}";
            return (titulo, m.Icone);
        }
        return (Humanizar(tipo.Name), "fas fa-window-maximize");
    }

    private static string Normalizar(string rota)
        => (rota ?? "").Trim().TrimStart('/').ToLowerInvariant();

    private static string Humanizar(string nome)
        => nome.EndsWith("Page") ? nome[..^4] : nome;
}