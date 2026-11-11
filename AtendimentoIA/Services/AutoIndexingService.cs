namespace EvolutTech.IA.Painel.Services;

/// <summary>
/// Indexação automática do código do ERP: uma vez por dia, a partir da hora configurada (padrão 3h, horário de Brasília),
/// confere o último commit do GitHub e só indexa se houver commit novo. Se o site estiver parado nesse horário
/// (IIS sem "AlwaysRunning"), recupera na primeira verificação depois. Também limpa execuções antigas do disco.
/// Desligar: appsettings Indexing:Auto=false. Hora: Indexing:AutoHour.
/// </summary>
public sealed class AutoIndexingService(IndexingService indexing, PanelStore store, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<AutoIndexingService> logger) : BackgroundService
{
    public const string StateFile = "auto-index.json";
    public sealed record AutoIndexState(DateOnly? LastCheck, string? Commit, string Message, DateTimeOffset UpdatedAt);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Indexing:Auto", true)) return;
        await Task.Yield();
        try { await indexing.Ready.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try { await CheckAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Indexação automática: verificação falhou ({ErrorType}).", ex.GetType().Name);
                await SaveAsync(null, "Verificação automática falhou: " + (ex is HttpRequestException ? "GitHub indisponível ou sem acesso." : ex.GetType().Name), keepDay: false);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static DateTimeOffset NowBrasilia()
    {
        try { return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time")); }
        catch (TimeZoneNotFoundException) { return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)); }
    }

    private async Task CheckAsync(CancellationToken token)
    {
        if (!indexing.IsAvailable || indexing.IsRunning) return;
        var now = NowBrasilia();
        var today = DateOnly.FromDateTime(now.DateTime);
        var state = await store.ReadAsync<AutoIndexState>(StateFile);
        if (state?.LastCheck == today) return;

        var last = indexing.History.FirstOrDefault(r => r.State is "Completed" or "CompletedWithLimitations");
        var stale = last is null || DateTimeOffset.UtcNow - (last.FinishedAt ?? last.StartedAt) > TimeSpan.FromHours(36);
        if (now.Hour < configuration.GetValue("Indexing:AutoHour", 3) && !stale) return;

        using var scope = scopes.CreateScope();
        var github = scope.ServiceProvider.GetRequiredService<GitHubSource>();
        var source = await store.GetSourceAsync(configuration);
        var commit = await github.LatestCommitAsync(source, token);
        string message;
        if (last?.Commit is { } indexed && indexed.Equals(commit, StringComparison.OrdinalIgnoreCase))
            message = $"Sem commit novo ({commit[..8]}). Índice mantido.";
        else
        {
            var (accepted, startMessage) = await indexing.StartAsync();
            message = accepted ? $"Commit novo {commit[..8]}: indexação automática iniciada." : startMessage;
            logger.LogInformation("Indexação automática: {Message}", message);
        }
        await SaveAsync(commit, message, keepDay: true, today);
        CleanupRuns();
    }

    private async Task SaveAsync(string? commit, string message, bool keepDay, DateOnly? day = null)
    {
        try
        {
            var previous = await store.ReadAsync<AutoIndexState>(StateFile);
            await store.WriteAsync(StateFile, new AutoIndexState(keepDay ? day : previous?.LastCheck, commit ?? previous?.Commit, message, DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Mantém no disco as 4 execuções mais recentes e sempre a última concluída (a que está em uso).</summary>
    private void CleanupRuns()
    {
        try
        {
            var runs = indexing.History;
            var inUse = runs.FirstOrDefault(r => r.State is "Completed" or "CompletedWithLimitations")?.Id;
            foreach (var run in runs.Skip(4))
            {
                if (run.Id == inUse || run.State is "Queued" or "Running" or "CancelRequested") continue;
                var folder = store.JobDirectory(run.Id);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Indexação automática: limpeza de execuções antigas falhou ({ErrorType}).", ex.GetType().Name);
        }
    }
}
