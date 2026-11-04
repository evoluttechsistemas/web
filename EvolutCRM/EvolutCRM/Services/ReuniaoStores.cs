using EvolutCRM.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EvolutCRM.Services;

/// <summary>
/// Singleton: avisa todas as telas abertas (de todos os usuários) sobre convites,
/// recusas, encerramentos e entradas/saídas nas reuniões. Mesmo padrão do UsuarioStatusNotifier.
/// </summary>
public sealed class ReuniaoConviteNotifier
{
    public event Action<ReuniaoEvento>? OnEvento;

    public void Notificar(ReuniaoEvento evento)
    {
        var handlers = OnEvento;
        if (handlers == null) return;

        foreach (Action<ReuniaoEvento> h in handlers.GetInvocationList())
        {
            try { h(evento); }
            catch { /* uma tela com problema não pode impedir as outras */ }
        }
    }
}

/// <summary>
/// Passe de entrada na sala. O Blazor (que sabe quem é o usuário logado) emite o passe
/// e o navegador entrega ao hub. Assim ninguém entra informando outro usuário pelo JavaScript.
/// </summary>
public sealed class ReuniaoAcessoStore
{
    private static readonly TimeSpan Validade = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, PasseReuniao> _passes = new();

    public string Emitir(int codReuniao, int codEmp, string usuario)
    {
        LimparExpirados();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _passes[token] = new PasseReuniao(codReuniao, codEmp, usuario.Trim().ToUpperInvariant(), DateTime.UtcNow.Add(Validade));
        return token;
    }

    public PasseReuniao? Validar(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_passes.TryGetValue(token, out var passe))
            return null;

        if (passe.ExpiraEm < DateTime.UtcNow)
        {
            _passes.TryRemove(token, out _);
            return null;
        }

        return passe;
    }

    private void LimparExpirados()
    {
        var agora = DateTime.UtcNow;
        foreach (var item in _passes.Where(x => x.Value.ExpiraEm < agora).ToList())
            _passes.TryRemove(item.Key, out _);
    }
}

public sealed record PasseReuniao(int CodReuniao, int CodEmp, string Usuario, DateTime ExpiraEm);

/// <summary>
/// Quem está dentro de cada sala agora (em memória, sempre correto mesmo se o IIS reiniciar
/// no meio de uma reunião — o banco guarda só o histórico de entrada/saída).
/// </summary>
public sealed class ReuniaoPresencaStore
{
    private readonly ConcurrentDictionary<string, SessaoReuniao> _conexoes = new();

    public void Adicionar(string connectionId, SessaoReuniao sessao) => _conexoes[connectionId] = sessao;

    public SessaoReuniao? Remover(string connectionId)
        => _conexoes.TryRemove(connectionId, out var sessao) ? sessao : null;

    public SessaoReuniao? Obter(string connectionId)
        => _conexoes.TryGetValue(connectionId, out var sessao) ? sessao : null;

    public bool UsuarioNaSala(int codReuniao, string usuario)
        => _conexoes.Values.Any(s => s.CodReuniao == codReuniao && s.Usuario.Equals(usuario.Trim(), StringComparison.OrdinalIgnoreCase));

    public List<string> UsuariosNaSala(int codReuniao)
        => _conexoes.Values
            .Where(s => s.CodReuniao == codReuniao)
            .Select(s => s.Usuario)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed record SessaoReuniao(int CodReuniao, int CodEmp, string Usuario);
