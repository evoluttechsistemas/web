using EvolutCRM.Models;
using Microsoft.AspNetCore.SignalR;

namespace EvolutCRM.Services;

/// <summary>
/// Sinalização das reuniões: troca de oferta/resposta/ICE do WebRTC, chat,
/// estado de mídia (microfone, câmera, tela, mão levantada) e reações.
/// O áudio e o vídeo NÃO passam pelo servidor (vão direto entre os navegadores).
/// </summary>
public sealed class ReuniaoHub : Hub
{
    private readonly ReuniaoService _reuniaoService;
    private readonly ReuniaoAcessoStore _acessos;
    private readonly ReuniaoPresencaStore _presenca;
    private readonly ReuniaoConviteNotifier _notifier;

    public ReuniaoHub(ReuniaoService reuniaoService, ReuniaoAcessoStore acessos,
        ReuniaoPresencaStore presenca, ReuniaoConviteNotifier notifier)
    {
        _reuniaoService = reuniaoService;
        _acessos = acessos;
        _presenca = presenca;
        _notifier = notifier;
    }

    public static string Sala(int codReuniao) => $"reuniao-{codReuniao}";

    public async Task EntrarSala(string token, object? estado)
    {
        var passe = _acessos.Validar(token)
            ?? throw new HubException("Acesso à reunião expirado. Abra a reunião novamente.");

        if (!await _reuniaoService.PodeEntrarAsync(passe.CodReuniao, passe.CodEmp, passe.Usuario))
            throw new HubException("Esta reunião foi encerrada ou você não faz parte dela.");

        var sala = Sala(passe.CodReuniao);
        await Groups.AddToGroupAsync(Context.ConnectionId, sala);
        _presenca.Adicionar(Context.ConnectionId, new SessaoReuniao(passe.CodReuniao, passe.CodEmp, passe.Usuario));
        await _reuniaoService.AtualizarPresencaAsync(passe.CodReuniao, passe.CodEmp, passe.Usuario, true);

        // Avisa só os outros: quem já está na sala cria a oferta WebRTC para o novo participante
        await Clients.OthersInGroup(sala).SendAsync("participanteEntrou", Context.ConnectionId, passe.Usuario, estado);
        AvisarPresenca(passe.CodEmp, passe.CodReuniao);
    }

    public async Task SairSala()
    {
        var sessao = _presenca.Remover(Context.ConnectionId);
        if (sessao is null) return;

        var sala = Sala(sessao.CodReuniao);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sala);
        await Clients.Group(sala).SendAsync("participanteSaiu", Context.ConnectionId, sessao.Usuario);
        await AtualizarPresencaSeUltimaConexaoAsync(sessao);
    }

    public Task EnviarSinal(string destinoConnectionId, object sinal)
    {
        var sessao = SessaoAtual();
        var destino = _presenca.Obter(destinoConnectionId);
        if (destino is null || destino.CodReuniao != sessao.CodReuniao)
            throw new HubException("Participante indisponível nesta sala.");

        return Clients.Client(destinoConnectionId).SendAsync("receberSinal", Context.ConnectionId, sessao.Usuario, sinal);
    }

    public Task EnviarEstado(object estado)
    {
        var sessao = SessaoAtual();
        return Clients.OthersInGroup(Sala(sessao.CodReuniao))
            .SendAsync("estadoParticipante", Context.ConnectionId, sessao.Usuario, estado);
    }

    public Task EnviarReacao(string emoji)
    {
        var sessao = SessaoAtual();
        var permitido = new[] { "👍", "👏", "😂", "❤️", "🎉", "😮" };
        if (!permitido.Contains(emoji)) return Task.CompletedTask;

        return Clients.Group(Sala(sessao.CodReuniao))
            .SendAsync("reacao", Context.ConnectionId, sessao.Usuario, emoji);
    }

    public Task EnviarChat(string mensagem)
    {
        var sessao = SessaoAtual();
        var texto = (mensagem ?? "").Trim();
        if (texto.Length == 0) return Task.CompletedTask;
        if (texto.Length > 2000) texto = texto[..2000];

        // O nome do remetente vem do servidor, não do navegador
        return Clients.OthersInGroup(Sala(sessao.CodReuniao)).SendAsync("receberChat", new
        {
            usuario = sessao.Usuario,
            mensagem = texto,
            dataHora = DateTime.Now
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessao = _presenca.Remover(Context.ConnectionId);
        if (sessao is not null)
        {
            await Clients.Group(Sala(sessao.CodReuniao)).SendAsync("participanteSaiu", Context.ConnectionId, sessao.Usuario);
            await AtualizarPresencaSeUltimaConexaoAsync(sessao);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Se o usuário ainda estiver na sala por outra conexão (ex.: reconectou), não marca como "Saiu"
    private async Task AtualizarPresencaSeUltimaConexaoAsync(SessaoReuniao sessao)
    {
        if (!_presenca.UsuarioNaSala(sessao.CodReuniao, sessao.Usuario))
            await _reuniaoService.AtualizarPresencaAsync(sessao.CodReuniao, sessao.CodEmp, sessao.Usuario, false);

        AvisarPresenca(sessao.CodEmp, sessao.CodReuniao);
    }

    private void AvisarPresenca(int codEmp, int codReuniao)
        => _notifier.Notificar(new ReuniaoEvento { Tipo = "Presenca", CodEmp = codEmp, CodReuniao = codReuniao });

    private SessaoReuniao SessaoAtual()
        => _presenca.Obter(Context.ConnectionId) ?? throw new HubException("Conexão sem acesso à sala.");
}
