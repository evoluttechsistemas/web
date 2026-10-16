using EvolutCRM.Models;

namespace EvolutCRM.Services;

/// <summary>
/// Scoped (um por aba do navegador): guarda a situação da equipe e os status cadastrados,
/// compartilhados pelo seletor de status e pela barra da equipe.
/// Atualiza na hora quando alguém da empresa troca de status (UsuarioStatusNotifier)
/// e a cada 20s para refletir quem entrou/saiu do sistema.
/// </summary>
public class EquipeStatusState : IDisposable
{
    private readonly UsuarioStatusService _service;
    private readonly UsuarioStatusNotifier _notifier;
    private readonly UserState _userState;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _iniciado;
    private bool _recarregarPendente;

    public List<EquipeMembroModel> Membros { get; private set; } = new();
    public List<UsuarioStatusCadastroModel> StatusDisponiveis { get; private set; } = new();

    public event Action? OnChange;

    public EquipeStatusState(UsuarioStatusService service, UsuarioStatusNotifier notifier, UserState userState)
    {
        _service = service;
        _notifier = notifier;
        _userState = userState;
    }

    public EquipeMembroModel? Eu =>
        Membros.FirstOrDefault(m => m.Usuario.Equals(_userState.CurrentUser?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>Chamado pelos componentes; só inicia uma vez por circuito.</summary>
    public async Task IniciarAsync()
    {
        if (_iniciado) return;
        _iniciado = true;

        _notifier.OnMudanca += AoMudarStatus;

        await RecarregarAsync(incluirCadastro: true);

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        _ = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (_timer != null && await _timer.WaitForNextTickAsync(token))
                await RecarregarAsync(incluirCadastro: false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void AoMudarStatus(int codEmp)
    {
        if (codEmp != _userState.CurrentCompanyId) return;
        _ = RecarregarAsync(incluirCadastro: true);
    }

    public async Task RecarregarAsync(bool incluirCadastro)
    {
        if (!_userState.IsReady || _userState.CurrentCompanyId <= 0) return;

        // Se já está carregando, marca para carregar de novo no fim (não perde a atualização)
        if (!await _lock.WaitAsync(0))
        {
            _recarregarPendente = true;
            return;
        }

        try
        {
            do
            {
                _recarregarPendente = false;

                Membros = await _service.ObterEquipeAsync();
                if (incluirCadastro || StatusDisponiveis.Count == 0)
                    StatusDisponiveis = await _service.ListarAsync(false);
            }
            while (_recarregarPendente);
        }
        catch
        {
            // falha momentânea de banco: mantém o último estado e tenta no próximo ciclo
        }
        finally
        {
            _lock.Release();
        }

        OnChange?.Invoke();
    }

    public async Task DefinirMeuStatusAsync(int? codStatus)
    {
        var usuario = _userState.CurrentUser?.Trim();
        if (string.IsNullOrWhiteSpace(usuario)) return;

        if (!_userState.IsOnline)
            _userState.SetOnline(true);

        await _service.DefinirStatusAsync(usuario, codStatus);
        // o notifier dispara a recarga de todas as telas, inclusive esta
    }

    public void Dispose()
    {
        _notifier.OnMudanca -= AoMudarStatus;
        _cts?.Cancel();
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
