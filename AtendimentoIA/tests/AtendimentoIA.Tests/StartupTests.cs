using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class StartupTests
{
    private static (ServiceProvider Provider,PanelStore Store,IndexingService Indexer) Setup()
    {
        var environment=new TestEnvironment { ContentRootPath=Path.Combine(AppContext.BaseDirectory,"startup-fixtures",Guid.NewGuid().ToString("N")) };
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Panel:DataDirectory"]="data" }).Build();
        var store=new PanelStore(config,environment);
        var services=new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sp=>new IndexingService(store,sp.GetRequiredService<IServiceScopeFactory>(),new UnusedAnalyzer(),config,NullLogger<IndexingService>.Instance));
        var provider=services.BuildServiceProvider();
        return(provider,store,provider.GetRequiredService<IndexingService>());
    }
    public static async Task HomeWithoutReady()
    {
        var (provider,_,indexer)=Setup();
        await using(provider)
        await using(var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>()))
        {
            if(indexer.IsReady) throw new InvalidOperationException("Fixture unexpectedly initialized");
            var html=await renderer.Dispatcher.InvokeAsync(async()=> {
                var root=await renderer.RenderComponentAsync<EvolutTech.IA.Painel.Components.Pages.Home>(ParameterView.Empty);
                return root.ToHtmlString();
            }).WaitAsync(TimeSpan.FromSeconds(4));
            if(!html.Contains("Uma base para atender melhor")||!html.Contains("Recuperando"))
                throw new InvalidOperationException("Home did not render its initial content");
        }
    }
    public static async Task CorruptedResult()
    {
        var (provider,store,indexer)=Setup();
        await using(provider)
        {
            var id=Guid.NewGuid();
            await store.SaveRunAsync(new(id,DateTimeOffset.UtcNow,"fixture/repo","main","Completed","Completed"));
            var path=Path.Combine(store.JobDirectory(id),"result.json");
            const string invalid="{broken-result";
            await File.WriteAllTextAsync(path,invalid);
            await indexer.StartAsync(default);
            try
            {
                await indexer.Ready.WaitAsync(TimeSpan.FromSeconds(4));
                if(!indexer.IsAvailable||indexer.Result is not null||string.IsNullOrWhiteSpace(indexer.InitializationWarning))
                    throw new InvalidOperationException("Corrupted cache was not recovered safely");
                if(await File.ReadAllTextAsync(path)!=invalid) throw new InvalidOperationException("Cache file was altered");
            }
            finally { await indexer.StopAsync(default); }
        }
    }
    private sealed class UnusedAnalyzer:ISourceAnalyzer
    {
        public Task<AnalysisResult> AnalyzeAsync(AnalysisOptions options,IProgress<string>? progress=null,CancellationToken cancellationToken=default)
            =>throw new InvalidOperationException("No source analysis should run during startup or rendering");
    }
}
