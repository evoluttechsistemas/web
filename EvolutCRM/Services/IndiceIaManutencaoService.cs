namespace EvolutCRM.Services;

/// <summary>
/// Rotina de segurança do índice da IA: todo dia às 03:00 coloca no índice o que ficou faltando
/// (insight cujo embedding falhou, documentação salva com o HELP fora do ar etc.) e recarrega o índice.
/// </summary>
public sealed class IndiceIaManutencaoService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmbeddingIndex _index;
    private static readonly TimeSpan Horario = new(3, 0, 0);

    public IndiceIaManutencaoService(IServiceScopeFactory scopeFactory, EmbeddingIndex index)
    {
        _scopeFactory = scopeFactory;
        _index = index;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var agora = DateTime.Now;
            var proxima = agora.Date + Horario;
            if (proxima <= agora) proxima = proxima.AddDays(1);

            try { await Task.Delay(proxima - agora, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var indexer = scope.ServiceProvider.GetRequiredService<EmbeddingIndexerService>();
                var (removidas, indexadas, insights) = await indexer.IndexarPendentesAsync();
                await _index.ReloadAsync();
                LogHelper.Log($"[IA MADRUGADA] Docs removidas: {removidas} | Docs indexadas: {indexadas} | Insights: {insights}");
            }
            catch (Exception ex)
            {
                LogHelper.Log("[IA MADRUGADA] Erro: " + ex.Message);
            }
        }
    }
}
