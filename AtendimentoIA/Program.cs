using EvolutTech.IA.Core;
using EvolutTech.IA.Indexador;
using EvolutTech.IA.Painel.Components;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var secretSetup = args.Contains("--configure-sql");
var inspectSql = args.Contains("--inspect-sql");
var storageRoot = Path.GetFullPath(builder.Configuration["Panel:DataDirectory"] ?? "App_Data", builder.Environment.ContentRootPath);
var protection = builder.Services.AddDataProtection().SetApplicationName("AtendimentoIA")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageRoot, "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
else throw new PlatformNotSupportedException("Esta configuração de segredos requer Windows e DPAPI.");
builder.Services.AddPanelAuthentication(builder.Environment);
builder.Services.AddHelpApi();
builder.Services.AddSingleton<PanelStore>();
builder.Services.AddSingleton<SecretStore>();
builder.Services.AddSingleton<SqlCatalogService>();
builder.Services.AddSingleton<SupportContextService>();
builder.Services.AddScoped<IKnowledgeRetriever, KnowledgeRetriever>();
builder.Services.AddHttpClient<IAnswerProvider, OpenAiAnswerProvider>(client => client.Timeout = TimeSpan.FromSeconds(90))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<SimulationService>();
// Modelo auxiliar: traduz a mensagem do cliente para termos de busca do ERP antes da consulta.
builder.Services.AddHttpClient<QueryRewriter>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
// Leitura dos prints que o HELP envia junto com a pergunta do chamado.
builder.Services.AddHttpClient<CustomerImageReader>(client => client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<DesignerScreenService>();
builder.Services.AddSingleton<ApplicationInspectionService>();
builder.Services.AddHttpClient<VideoCatalogService>(client=>client.Timeout=TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler {AllowAutoRedirect=false});
builder.Services.AddSingleton<ISourceAnalyzer, SourceAnalyzer>();
builder.Services.AddHttpClient<GitHubSource>(client => client.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IndexingService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<IndexingService>());
// Indexa o GitHub sozinho uma vez por dia quando há commit novo (Indexing:Auto / Indexing:AutoHour).
builder.Services.AddHostedService<AutoIndexingService>();

var app = builder.Build();
if (secretSetup)
{
    // Read stdin only: credentials never appear in process arguments, config source or logs.
    Console.WriteLine("Aguardando conexão SQL pela entrada protegida; o valor não será exibido.");
    string? connection;
    if (Console.IsInputRedirected) connection = Console.ReadLine();
    else
    {
        var input = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (input.Length > 0) input.Length--; }
            else if (!char.IsControl(key.KeyChar)) input.Append(key.KeyChar);
        }
        connection = input.ToString();
    }
    if (string.IsNullOrWhiteSpace(connection)) { Console.Error.WriteLine("Conexão não recebida."); return; }
    await app.Services.GetRequiredService<SecretStore>().SaveSqlAsync(connection);
    Console.WriteLine("Conexão armazenada com proteção Windows para este usuário.");
    return;
}
if (inspectSql)
{
    try
    {
        var schema = await app.Services.GetRequiredService<SqlCatalogService>().ReadAsync();
        Console.WriteLine($"Objetos={schema.Objects.Count}; Colunas={schema.Columns.Count}; Relações={schema.Relations.Count}");
        foreach (var table in schema.Objects.Where(o => o.Name.StartsWith("Documentacao", StringComparison.OrdinalIgnoreCase) || new[] { "TicketChamadoC", "TicketChamadoD", "Cliente", "TicketAssistenteIA" }.Contains(o.Name, StringComparer.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"{table.Schema}.{table.Name}");
            foreach (var column in schema.Columns.Where(c => c.Schema == table.Schema && c.Table == table.Name))
                Console.WriteLine($"  {column.Name}: {column.Type}{(schema.Indexes.Any(i => i.Table == table.Name && i.Schema == table.Schema && i.Column == column.Name && i.PrimaryKey) ? " PK" : "")}");
        }
    }
    catch (Microsoft.Data.SqlClient.SqlException ex) { Console.Error.WriteLine($"SQL indisponível: código {ex.Number}. Nenhum dado foi alterado."); Environment.ExitCode = 1; }
    return;
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro");
    app.UseHsts();
    app.UseHttpsRedirection();
}
// Static assets contain no data. App_Data must never be placed under wwwroot.
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapPanelAuthentication();
app.MapHelpApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/erro", () => Results.Problem("Não foi possível concluir a operação. Consulte o administrador.")).AllowAnonymous();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
