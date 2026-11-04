using System.Threading.Channels;
using EvolutTech.IA.Core;
using System.Text.Json;

namespace EvolutTech.IA.Painel.Services;

public sealed class IndexingService(PanelStore store, IServiceScopeFactory scopeFactory,
    ISourceAnalyzer analyzer, IConfiguration configuration, ILogger<IndexingService> logger) : BackgroundService
{
    private readonly Channel<(IndexRun Run, SourceSettings Source)> queue = Channel.CreateBounded<(IndexRun, SourceSettings)>(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object cancellationLock = new();
    private CancellationTokenSource? currentCancellation;
    private IndexRun[] history = [];
    private IndexRun? current;
    private AnalysisResult? result;
    private string? initializationWarning;
    private volatile bool available = true;
    public Task Ready => ready.Task;
    public bool IsReady => ready.Task.IsCompletedSuccessfully;
    public bool IsAvailable => available;
    public string? InitializationWarning => Volatile.Read(ref initializationWarning);
    public IndexRun? Current => Volatile.Read(ref current);
    public IReadOnlyList<IndexRun> History => Volatile.Read(ref history);
    public AnalysisResult? Result => Volatile.Read(ref result);
    public bool IsRunning => Current?.State is "Queued" or "Running" or "CancelRequested";

    public async Task<(bool Accepted, string Message)> StartAsync()
    {
        await Ready;
        if (!available) return (false, "O serviço de indexação está indisponível. O painel continua disponível; confira os logs e reinicie a aplicação.");
        await gate.WaitAsync();
        try
        {
            if (IsRunning) return (false, "Já existe uma indexação em andamento.");
            var source = await store.GetSourceAsync(configuration);
            GitHubSource.Validate(source);
            var run = new IndexRun(Guid.NewGuid(), DateTimeOffset.UtcNow, source.Repository, source.Reference,
                "Queued", "Aguardando download do GitHub.");
            await store.SaveRunAsync(run);
            Volatile.Write(ref current, run);
            Volatile.Write(ref history, new[] { run }.Concat(history).ToArray());
            if (!queue.Writer.TryWrite((run, source))) throw new InvalidOperationException("Fila indisponível.");
            logger.LogInformation("Indexação {RunId} solicitada.", run.Id);
            return (true, "Indexação iniciada. Você pode navegar pelo painel enquanto ela é processada.");
        }
        finally { gate.Release(); }
    }
    public void Cancel()
    {
        // A queued request may not have a CTS yet; remember cancellation in its state.
        var run = Current;
        if (run is null || !IsRunning) return;
        lock (cancellationLock)
        {
            currentCancellation?.Cancel();
            if (run.State == "Queued") Volatile.Write(ref current, run with { State = "CancelRequested", Message = "Cancelamento solicitado." });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initialization must not block host startup or the first HTTP response.
        await Task.Yield();
        try
        {
            try
            {
            var previous = await store.GetRunsAsync();
            foreach (var run in previous.Where(r => r.State is "Queued" or "Running" or "CancelRequested").ToArray())
            {
                var interrupted = run with { State = "Interrupted", Message = "O processo foi reiniciado. Inicie uma nova indexação." };
                previous[previous.IndexOf(run)] = interrupted;
                await store.SaveRunAsync(interrupted);
            }
            Volatile.Write(ref history, previous.ToArray());
            Volatile.Write(ref current, previous.FirstOrDefault());
            var last = previous.FirstOrDefault(r => r.State is "Completed" or "CompletedWithLimitations");
            if (last is not null)
                Volatile.Write(ref result, await store.ReadAsync<AnalysisResult>(Path.Combine("runs", last.Id.ToString("N"), "result.json")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Volatile.Write(ref initializationWarning, "Não foi possível recuperar uma análise anterior. Os arquivos foram preservados e você pode iniciar uma nova indexação.");
                logger.LogWarning("Recuperação do índice indisponível: {ErrorType}. O painel permanece ativo.", ex.GetType().Name);
            }
            ready.TrySetResult();
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            {
                lock (cancellationLock) currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var run = request.Run;
                using var heartbeatCancellation = new CancellationTokenSource();
                Task? heartbeat = null;
                try
                {
                    if (Current?.Id == run.Id && Current.State == "CancelRequested") currentCancellation.Cancel();
                    var token = currentCancellation.Token;
                    token.ThrowIfCancellationRequested();
                    run = run with { State = "Running", Message = "Obtendo a versão do código no GitHub…" };
                    await UpdateAsync(run);
                    heartbeat = PersistProgressAsync(run.Id, heartbeatCancellation.Token);
                    var progress = new InlineProgress<ProcessingProgress>(value => ReportProgress(run.Id, value));
                    using var scope = scopeFactory.CreateScope();
                    var source = scope.ServiceProvider.GetRequiredService<GitHubSource>();
                    var commit = await source.DownloadAsync(request.Source, store.JobDirectory(run.Id), token, progress);
                    run = run with { Commit = commit, Message = "Analisando os arquivos com Roslyn…" };
                    await UpdateAsync(run);
                    var analyzed = await analyzer.AnalyzeAsync(new(Path.Combine(store.JobDirectory(run.Id), "source"),
                        configuration["Indexing:ReferenceDirectory"], configuration.GetSection("Indexing:Defines").Get<string[]>(), progress), cancellationToken: token);
                    if (analyzed.Files.Count == 0) throw new InvalidDataException("O repositório não contém arquivos C# analisáveis.");
                    token.ThrowIfCancellationRequested();
                    ReportProgress(run.Id, new("save", 0, null, "índice"));
                    await store.WriteAsync(Path.Combine("runs", run.Id.ToString("N"), "result.json"), analyzed);
                    run = run with
                    {
                        State = analyzed.Issues.Count == 0 ? "Completed" : "CompletedWithLimitations",
                        Message = analyzed.Issues.Count == 0 ? "Análise concluída." : "Análise concluída com limitações. Consulte os diagnósticos.",
                        Files = analyzed.Files.Count, Classes = analyzed.Classes.Count, Methods = analyzed.MethodCount,
                        Forms = analyzed.Forms.Count(f => f.FormEvidence == "semantic-inheritance"), Issues = analyzed.Issues.Count
                    };
                    Volatile.Write(ref result, analyzed);
                }
                catch (OperationCanceledException)
                { run = run with { State = "Cancelled", Message = "Indexação cancelada. O último resultado concluído foi preservado." }; }
                catch (Exception ex)
                {
                    logger.LogWarning("Indexação {RunId} falhou: {ErrorType}.", run.Id, ex.GetType().Name);
                    var safeMessage = ex is HttpRequestException ? "Não foi possível obter o código. Verifique acesso ao GitHub, referência e token no servidor." :
                        "Não foi possível concluir. Verifique armazenamento, limites do arquivo e configuração do servidor.";
                    run = run with { State = "Failed", Message = safeMessage };
                }
                finally
                {
                    heartbeatCancellation.Cancel();
                    if (heartbeat is not null) await heartbeat;
                    lock (cancellationLock) { currentCancellation.Dispose(); currentCancellation = null; }
                }
                run = run with { FinishedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
                await UpdateAsync(run);
                logger.LogInformation("Indexação {RunId} finalizada: {State}.", run.Id, run.State);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            available = false;
            Volatile.Write(ref initializationWarning, "O serviço de indexação ficou indisponível. As configurações e as demais telas continuam acessíveis; consulte os logs do aplicativo.");
            ready.TrySetResult();
            logger.LogCritical("Serviço de indexação indisponível: {ErrorType}.", ex.GetType().Name);
        }
    }
    private async Task UpdateAsync(IndexRun run)
    {
        run = run with { UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveRunAsync(run);
        Volatile.Write(ref current, run);
        Volatile.Write(ref history, history.Where(r => r.Id != run.Id).Prepend(run).ToArray());
    }
    private void ReportProgress(Guid runId, ProcessingProgress value)
    {
        var live = Current;
        if (live?.Id != runId) return;
        Volatile.Write(ref current, live with { Stage = value.Stage, Processed = value.Completed,
            Total = value.Total, Unit = value.Unit, UpdatedAt = DateTimeOffset.UtcNow, Message = DescribeProgress(value) });
    }
    public static string DescribeProgress(ProcessingProgress progress)
    {
        if (progress.Stage == "download") return progress.Total is > 0
            ? $"Baixando código: {progress.Completed / 1048576d:N1} de {progress.Total.Value / 1048576d:N1} MiB."
            : $"Baixando código: {progress.Completed / 1048576d:N1} MiB recebidos (tamanho total não informado pelo GitHub).";
        var stage = progress.Stage switch { "resolve" => "Identificando o commit no GitHub", "extract" => "Extraindo fontes",
            "scan" => "Localizando projetos e arquivos", "parse" => "Lendo arquivos C#", "semantic" => "Resolvendo símbolos e diagnósticos",
            "symbols" => "Mapeando classes, métodos e Forms", "analyzed" => "Análise concluída", "save" => "Gravando o índice", _ => "Processando" };
        return progress.Total is > 0 ? $"{stage}: {progress.Completed:N0} de {progress.Total:N0} {progress.Unit}." : stage + "…";
    }
    private async Task PersistProgressAsync(Guid runId, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var live = Current;
                if (live?.Id != runId || live.State is not ("Running" or "Queued" or "CancelRequested")) continue;
                try { await store.SaveRunAsync(live); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { logger.LogWarning("Não foi possível persistir progresso de {RunId}: {ErrorType}.", runId, ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    { public void Report(T value) => action(value); }
}
