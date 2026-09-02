using EvolutCRM.Abas;
using EvolutCRM.APIEvolutTech.Services;
using EvolutCRM.Components;
using EvolutCRM.Data;
using EvolutCRM.Models;
using EvolutCRM.Services;
using EvolutTech.CRM.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Aumenta limite para uploads de mídia (vídeos do WhatsApp via form-urlencoded)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024; // 200MB
    options.ValueLengthLimit = 200 * 1024 * 1024;
    options.MemoryBufferThreshold = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
});

/* =========================
   SERVICES
========================= */

/* =========================
   SERVICES
========================= */

// CRM services
builder.Services.AddScoped<UserState>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<MenuCRMService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<TicketNotificationState>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<AiTicketInsightsRepository>();
builder.Services.AddScoped<DocService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<TicketQueryRepository>();
// NOVO (acrescente)
builder.Services.AddScoped<AiAutoLearningService>();
builder.Services.AddScoped<OpenAiClient>();
builder.Services.AddScoped<EmbeddingIndexerService>();
builder.Services.AddSingleton<EmbeddingIndex>();
builder.Services.Configure<OpenAiSettings>(
    builder.Configuration.GetSection("OpenAI"));
builder.Services.AddScoped<ChatInternoService>();
builder.Services.AddScoped<ChatInternoNotificationState>();
builder.Services.AddScoped<AgendaService>();
builder.Services.AddScoped<ComercialFluxoService>();
builder.Services.AddHttpClient<KHelpDeskApiService>();
builder.Services.AddScoped<DocumentacaoService>();
builder.Services.AddHostedService<OfflineTicketReassignService>();
builder.Services.AddHostedService<EnvioWhatsAppBackgroundService>();
builder.Services.AddScoped<CurriculoOnlineService>();
builder.Services.AddScoped<AbaService>();
builder.Services.AddScoped<LogAcessoService>();
builder.Services.AddScoped<MonitorBackupService>();
builder.Services.AddHostedService<BackupMonitorBackgroundService>();
builder.Services.AddSingleton<HealthMonitorService>();
builder.Services.AddScoped<BxClienteService>();
builder.Services.AddScoped<LogFinanceiroService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<CadastroUsuarioService>();
builder.Services.AddScoped<ClienteCadastroService>();
builder.Services.AddScoped<ParametroDinamicoService>();
builder.Services.AddScoped<TicketClassificacaoService>();
builder.Services.AddScoped<ClienteWhatsAppFotoService>();

AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.AcessoRemoto), "Acesso Remoto", "fas fa-desktop", "admin/acesso-remoto");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.AdminLogs), "Admin Logs", "fas fa-file-shield", "admin/logs");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Agendas), "Agendas", "fas fa-calendar-days", "agendas");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.BaixaReceber), "Baixa a Receber", "fas fa-money-bill", "admin/bx-pagar");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.BancoTalentos), "Banco de Talentos", "fas fa-users", "banco-talentos");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Card), "Card", "fas fa-id-card", "card");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Cards), "CRM", "fas fa-table-columns", "cards");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.ChatInterno), "Chat Interno", "fas fa-comments", "chat-interno");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.ComercialFluxo), "Comercial", "fas fa-chart-line", "comercial-fluxo");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Comissoes), "Comissões", "fas fa-hand-holding-dollar", "admin/comissoes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CurriculoOnline), "Currículo Online", "fas fa-file-user", "curriculo-online");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CursoTutorial), "Tutoriais", "fas fa-graduation-cap", "curso-tutorial");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Dashboard), "Dashboard", "fas fa-chart-pie", "dashboard");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.DocumentacaoEditor), "Editor de Doc", "fas fa-pen-to-square", "documentacoes/novo");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Documentacoes), "Documentações", "fas fa-book", "documentacoes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Error), "Erro", "fas fa-triangle-exclamation", "error");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.MenuCRM), "Menu CRM", "fas fa-bars", "menu");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Permissoes), "Permissões", "fas fa-lock", "admin/permissoes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Tarefas), "Tarefas", "fas fa-list-check", "admin/tarefas");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Ticket), "Ticket", "fas fa-headset", "ticket");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Tickets), "Tickets", "fas fa-list", "tickets");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.VersoesCliente), "Versões do Cliente", "fas fa-code-branch", "admin/vs-clientes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Whatsappconexoes), "WhatsApp Conexões", "fas fa-plug", "whatsapp-conexoes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.LogRemoto), "Log Remoto", "fas fa-desktop-arrow-down", "admin/log-remoto");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.LogAcesso), "Log de Acesso", "fas fa-right-to-bracket", "admin/log-acesso");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.LogFinanceiro), "Log Financeiro", "fas fa-money-bill-transfer", "admin/log-financeiro");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.MonitorBackups), "Monitor de Backups", "fas fa-database", "admin/monitor-backups");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.MonitorHealth), "Monitor HELP", "fas fa-heart-pulse", "admin/monitor");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CadastroUsuario), "Cadastro de Usuário", "fas fa-user-plus", "cadastro-usuario");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CadastroCliente), "Cadastro de Cliente", "fas fa-users", "cadastro-cliente");
AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.ParametroDinamico),
    "Parametro Dinamico",
    "fas fa-sliders-h",
    "parametro-dinamico"
);
AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroTicketSetor),
    "Setores do Ticket",
    "fas fa-layer-group",
    "cadastro-ticket-setor"
);

AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroTicketSituacao),
    "Situações do Ticket",
    "fas fa-list-check",
    "cadastro-ticket-situacao"
);

AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroTicketTipo),
    "Tipos do Ticket",
    "fas fa-tags",
    "cadastro-ticket-tipo"
);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<EmailNotificacaoService>();

builder.Services.AddScoped<EvolutCRM.Services.ChangelogService>(sp =>
    new EvolutCRM.Services.ChangelogService(
        builder.Configuration.GetConnectionString("Connection")!));


builder.Services.AddHttpClient("openai", (sp, client) =>
{
    var settings = builder.Configuration.GetSection("OpenAI");
    var apiKey = settings["ApiKey"];

    client.BaseAddress = new Uri("https://api.openai.com/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

// Controllers (API)
builder.Services.AddControllers();

// JWT configuration
var jwt = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwt["Key"]!);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine($"[JWT] Authentication FAILED: {ctx.Exception}");
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                Console.WriteLine($"[JWT] Token VALID -> {ctx.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                Console.WriteLine($"[JWT] Challenge -> {ctx.Error} | {ctx.ErrorDescription}");
                return Task.CompletedTask;
            }
        };

        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "EvolutCRM",
            ValidAudience = "evolut-api",
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });


builder.Services.AddAuthorization();

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 🔧 Circuit Options - timeout e reconexão
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DetailedErrors = true;
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5); // mantém circuit por 5min
        options.DisconnectedCircuitMaxRetained = 100;
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(3); // timeout JS Interop
    })
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 100 * 1024 * 1024;
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(2); // espera 2min antes de desconectar
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15); // ping a cada 15s
    });

/* =========================
   BUILD
========================= */

var app = builder.Build();

/* =========================
   PIPELINE
========================= */

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// API
app.MapControllers();
app.MapControllerRoute(
    name: "api",
    pattern: "api/{controller}/{action}/{id?}"
);

// =========================
// STATIC FILES (Uploads)
// =========================
var uploadsPath = @"C:\FTP\Site\Help\uploads";

// garante que a pasta exista
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// arquivos estáticos padrão (wwwroot)
app.UseStaticFiles();

// uploads (fora do wwwroot)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});


// Blazor
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/test-mysql-docs", async (DocService docs) =>
{
    var result = await docs.TestConnectionAsync();
    return Results.Ok(result);
});

app.MapGet("/api/test-busca", async (string q, EmbeddingIndex idx, OpenAiClient ai) =>
{
    var v = await ai.GetEmbeddingAsync(q);
    var ins = idx.SearchInsights(v, q, 5);
    var docs = idx.SearchDocs(v, q, 5);

    var sb = new StringBuilder();
    sb.AppendLine("== INSIGHTS ==");
    foreach (var h in ins) sb.AppendLine($"{h.Score:F2}  {h.Item.Resumo}  ||  {h.Item.Solucao}");
    sb.AppendLine();
    sb.AppendLine("== DOCS ==");
    foreach (var h in docs) sb.AppendLine($"{h.Score:F2}  {h.Item.Titulo}");
    return Results.Text(sb.ToString());
});

// reindexar volta ao normal
app.MapGet("/api/reindexar", async (string? key, EmbeddingIndexerService indexer, EmbeddingIndex idx) =>
{
    if (key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    await indexer.IndexarDocsAsync();
    await indexer.IndexarInsightsAsync();
    await idx.ReloadAsync();
    return Results.Text("Reindexado e recarregado.");
});

// carrega o índice em segundo plano, sem travar o start do app
_ = Task.Run(async () =>
{
    try
    {
        await app.Services.GetRequiredService<EmbeddingIndex>().ReloadAsync();
        Console.WriteLine("[EmbeddingIndex] Índice carregado.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("[EmbeddingIndex] Erro ao carregar índice: " + ex);
    }
});

app.Run();
