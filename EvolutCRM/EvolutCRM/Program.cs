using EvolutCRM.Abas;
using EvolutCRM.APIEvolutTech.Services;
using EvolutCRM.Blog;
using EvolutCRM.Components;
using EvolutCRM.Data;
using EvolutCRM.Mobile;
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
builder.Services.AddSingleton<ResumoTransferenciaService>();   // resumo da conversa ao transferir o ticket (gpt-4.1-mini)
builder.Services.AddHostedService(sp => sp.GetRequiredService<ResumoTransferenciaService>());
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<RespostaRapidaService>();   // respostas rápidas ("/" + atalho no ticket)
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<TicketNotificationState>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<AssistenteAtendenteService>();
builder.Services.AddScoped<TreinamentoClienteService>();     // histórico de treinamento do cliente   // chat do atendente com a IA dentro do ticket
builder.Services.AddScoped<AiTicketInsightsRepository>();
builder.Services.AddScoped<DocService>();
builder.Services.AddControllers();
// Gasto da IA: toda resposta da OpenAI passa pelo UsoOpenAiHandler, que registra os tokens e o custo estimado
builder.Services.AddSingleton<IaUsoService>();
builder.Services.AddTransient<UsoOpenAiHandler>();
builder.Services.ConfigureHttpClientDefaults(b => b.AddHttpMessageHandler<UsoOpenAiHandler>());
builder.Services.AddHttpClient();
// AtendimentoIA: responde as perguntas do painel "Assistente IA" do ticket (AtendimentoIA:Url e AtendimentoIA:Token no appsettings)
builder.Services.AddHttpClient(AtendimentoIaClient.NomeHttpClient, client => client.Timeout = TimeSpan.FromSeconds(100))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });   // redirecionamento = erro, nao segue para o login
builder.Services.AddSingleton<AtendimentoIaClient>();
builder.Services.AddHttpClient("local", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["App:BaseUrl"]!);
});
builder.Services.AddScoped<BlogRepository>();
builder.Services.AddScoped<System.Data.IDbConnection>(_ =>
    new Microsoft.Data.SqlClient.SqlConnection(
        builder.Configuration.GetConnectionString("Connection")));
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<TicketQueryRepository>();
// NOVO (acrescente)
builder.Services.AddScoped<AiAutoLearningService>();
builder.Services.AddScoped<OpenAiClient>();
builder.Services.AddScoped<EmbeddingIndexerService>();
builder.Services.AddScoped<IaCuradoriaService>();          // curadoria da IA: revisão dos casos, 👎 e lacunas
builder.Services.AddSingleton<DocumentacaoAutomaticaService>(); // documentação interna a partir do insight (gpt-4.1)
builder.Services.AddHostedService(sp => sp.GetRequiredService<DocumentacaoAutomaticaService>());
builder.Services.AddSingleton<InsightAutomaticoService>();  // insight de todo ticket finalizado (fila + varredura às 02:30)
builder.Services.AddHostedService(sp => sp.GetRequiredService<InsightAutomaticoService>());
builder.Services.AddHostedService<FinalizacaoAutomaticaService>(); // finaliza sozinho o ticket parado (TICKET_FINALIZAR_AUTOMATICO)
builder.Services.AddScoped<FinalizacaoConfirmacaoService>();       // aviso "cliente aguardando resposta" (modal no MainLayout)
builder.Services.AddScoped<IaGabaritoService>();            // gabarito fixo de perguntas reais (Curadoria da IA)
builder.Services.AddSingleton<IaGabaritoJob>();             // roda o gabarito em segundo plano
builder.Services.AddSingleton<EmbeddingIndex>();
builder.Services.AddHostedService<IndiceIaManutencaoService>(); // completa o índice da IA todo dia às 03:00
builder.Services.AddSingleton<AvaliacaoIaJob>();                 // prova automática da IA (/api/ia/avaliar)
builder.Services.AddSingleton<RegerarInsightsJob>();             // regera os insights sem solução (/api/ia/regerar-insights)
builder.Services.Configure<OpenAiSettings>(
    builder.Configuration.GetSection("OpenAI"));
builder.Services.AddScoped<ChatInternoService>();
builder.Services.AddScoped<ChatInternoNotificationState>();
builder.Services.AddScoped<AgendaService>();
builder.Services.AddScoped<ComercialFluxoService>();
builder.Services.AddHttpClient<KHelpDeskApiService>();
builder.Services.AddScoped<DocumentacaoService>();
// Imagens de documentações e blog em arquivo (/uploads); no banco só o caminho
builder.Services.AddSingleton<ImagemArmazenamentoService>();
builder.Services.AddHostedService<MigracaoImagensService>(); // converte o que ainda está no banco (só em produção)
builder.Services.AddHostedService<OfflineTicketReassignService>();
builder.Services.AddHostedService<EnvioWhatsAppBackgroundService>();
builder.Services.AddScoped<CurriculoOnlineService>();
builder.Services.AddScoped<AbaService>();
builder.Services.AddScoped<LogAcessoService>();
builder.Services.AddScoped<MonitorBackupService>();
builder.Services.AddScoped<BackupFtpService>();   // tela Backups dos Clientes (lista e baixa do FTP)
builder.Services.AddHostedService<BackupMonitorBackgroundService>();
// Servidor de backup (FTP) ligado? Verifica a cada minuto, avisa no HELP e por WhatsApp
builder.Services.AddSingleton<MonitorServidorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorServidorService>());
builder.Services.AddSingleton<HealthMonitorService>();
builder.Services.AddScoped<BxClienteService>();
builder.Services.AddScoped<LogFinanceiroService>();
builder.Services.AddScoped<BoletoInterService>();   // emissão de boletos pelo Banco Inter (tela Boletos Inter)
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<CadastroUsuarioService>();
builder.Services.AddScoped<ClienteCadastroService>();
builder.Services.AddScoped<ParametroDinamicoService>();
builder.Services.AddScoped<TicketClassificacaoService>();
builder.Services.AddSingleton<UsuarioStatusNotifier>();   // avisa todas as telas quando alguém troca de status
builder.Services.AddSingleton<NotificacaoVistaNotifier>(); // ticket/card aberto por alguém some do painel de notificações de todos
builder.Services.AddScoped<UsuarioStatusService>();
builder.Services.AddScoped<EquipeStatusState>();
builder.Services.AddScoped<MiniChatState>();          // conversa aberta na janelinha de chat da faixa da equipe
builder.Services.AddScoped<ClienteWhatsAppFotoService>();
builder.Services.AddSingleton<DeployNotificacaoState>();
builder.Services.AddHostedService<DeployNotificacaoService>();

// Reuniões (vídeo/áudio/tela entre usuários do HELP) — sinalização WebRTC via SignalR
builder.Services.AddScoped<ReuniaoService>();
builder.Services.AddSingleton<ReuniaoAcessoStore>();
builder.Services.AddSingleton<ReuniaoPresencaStore>();     // quem está em cada sala agora
builder.Services.AddSingleton<ReuniaoConviteNotifier>();   // convites chegam na hora em todas as telas

// Indique e Ganhe (página pública do cliente + tela interna)
builder.Services.AddScoped<IndicacaoService>();
builder.Services.AddSignalR();

// HELP Mobile (API + notificações push)
builder.Services.AddScoped<MobileRepository>();
builder.Services.AddSingleton<MobileTokenService>();
builder.Services.AddHostedService<MobilePushWorker>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSite", policy =>
    {
        // Site + HELP Mobile (os endereços do app ficam no appsettings → Mobile:Origens)
        var origens = new List<string>
        {
            "http://localhost:4321",
            "https://www.evoluttech.com.br/"  // adicionar o domínio de produção depois
        };
        origens.AddRange(builder.Configuration.GetSection("Mobile:Origens").Get<string[]>() ?? Array.Empty<string>());

        // Origem não pode terminar com "/" (senão o navegador bloqueia)
        var permitidas = origens.Select(o => o.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (builder.Environment.IsDevelopment())
        {
            // Desenvolvimento: libera também a rede local (testar o HELP Mobile no celular pelo IP do PC)
            policy.SetIsOriginAllowed(origem =>
                permitidas.Contains(origem, StringComparer.OrdinalIgnoreCase) || OrigemDaRedeLocal(origem));
        }
        else
        {
            policy.WithOrigins(permitidas);
        }

        policy.AllowAnyMethod()
              .AllowAnyHeader()
              // O navegador guarda a liberação do CORS por 2h (sem isso o Chrome guarda só 5s
              // e o HELP Mobile fazia 2 viagens — OPTIONS + chamada — em quase toda requisição).
              .SetPreflightMaxAge(TimeSpan.FromHours(2));
    });
});




AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.AcessoRemoto), "Acesso Remoto", "fas fa-desktop", "admin/acesso-remoto");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.AdminLogs), "Admin Logs", "fas fa-file-shield", "admin/logs");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Agendas), "Agendas", "fas fa-calendar-days", "agendas");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.BaixaReceber), "Financeiro", "fas fa-money-bill", "admin/bx-pagar");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.BoletosInter), "Boletos Inter", "fas fa-barcode", "admin/boletos-inter");
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
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CuradoriaIA), "Curadoria da IA", "fas fa-wand-magic-sparkles", "admin/ia-curadoria");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.MonitorBackups), "Monitor de Backups", "fas fa-database", "admin/monitor-backups");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.BackupsClientes), "Backups dos Clientes", "fas fa-cloud-arrow-down", "admin/backups-clientes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.MonitorHealth), "Monitor HELP", "fas fa-heart-pulse", "admin/monitor");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CadastroUsuario), "Cadastro de Usuário", "fas fa-user-plus", "cadastro-usuario");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.CadastroCliente), "Cadastro de Cliente", "fas fa-users", "cadastro-cliente");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Blog.BlogList), "Blog", "fas fa-pen-to-square", "blog");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Blog.BlogEditor), "Blog Editor", "fas fa-pen-to-square", "blog/editar");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Blog.BlogEditor), "Novo Post", "fas fa-pen-to-square", "blog/novo");

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
    typeof(EvolutCRM.Components.Pages.RespostasRapidas),
    "Respostas rápidas",
    "fas fa-bolt",
    "respostas-rapidas"
);

AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroTicketTipo),
    "Tipos do Ticket",
    "fas fa-tags",
    "cadastro-ticket-tipo"
);

AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroTreinamentoTipo),
    "Treinamentos",
    "fas fa-graduation-cap",
    "cadastro-treinamentos"
);

AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Reunioes), "Reuniões", "fas fa-video", "reunioes");
AbaService.RegistrarMeta(typeof(EvolutCRM.Components.Pages.Indicacoes), "Indicações", "fas fa-handshake", "indicacoes");

AbaService.RegistrarMeta(
    typeof(EvolutCRM.Components.Pages.CadastroUsuarioStatus),
    "Status do Usuário",
    "fas fa-user-clock",
    "cadastro-usuario-status"
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
// Links assinados dos anexos dos tickets e do aviso de saída (marcar-offline)
AssinaturaHelp.Configurar(jwt["Key"]!);

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

app.UseCors("AllowSite");

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
var uploadsPath = builder.Configuration["Uploads:PastaFisica"] ?? @"C:\FTP\Site\Help\uploads";

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
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        // Imagens das documentações têm o hash do conteúdo no nome: nunca mudam, cache de 1 ano
        if (ctx.Context.Request.Path.StartsWithSegments("/uploads/documentacoes"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }
});


// Blazor
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/test-mysql-docs", async (DocService docs) =>
{
    var result = await docs.TestConnectionAsync();
    return Results.Ok(result);
});

app.MapGet("/api/test-busca", async (string q, string? key, EmbeddingIndex idx, OpenAiClient ai) =>
{
    // Exige a chave de admin: aberto, gastava IA e mostrava insights/documentações para qualquer um
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
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
// /api/reindexar?key=...                -> só o que está faltando (rápido)
// /api/reindexar?key=...&completo=true  -> apaga e refaz todas as documentações
app.MapGet("/api/reindexar", async (string? key, bool? completo, EmbeddingIndexerService indexer, EmbeddingIndex idx) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();

    string resumo;
    if (completo == true)
    {
        await indexer.IndexarDocsAsync();
        var insights = await indexer.IndexarInsightsAsync();
        resumo = $"Completo: todas as documentações refeitas | insights novos: {insights}";
    }
    else
    {
        var (removidas, indexadas, insights) = await indexer.IndexarPendentesAsync();
        resumo = $"Pendentes: docs removidas {removidas} | docs indexadas {indexadas} | insights novos {insights}";
    }

    await idx.ReloadAsync();
    return Results.Text("Reindexado e recarregado. " + resumo);
});

// Regera em segundo plano os insights dos tickets que ficaram sem solução
// /api/ia/regerar-insights?key=...&limite=500   e   /api/ia/regerar-insights/status?key=...
app.MapGet("/api/ia/regerar-insights", (string? key, int? limite, RegerarInsightsJob job) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    var iniciou = job.Iniciar(Math.Clamp(limite ?? 500, 1, 2000));
    return Results.Text(iniciou
        ? "Regeneração iniciada em segundo plano. Acompanhe em /api/ia/regerar-insights/status?key=..."
        : "Já está rodando.\n" + job.Status());
});

app.MapGet("/api/ia/regerar-insights/status", (string? key, RegerarInsightsJob job) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    return Results.Text(job.Status());
});

// Prova automática: faz as perguntas dos casos confirmados e mede a % de acerto
// /api/ia/avaliar?key=...&limite=50   e   /api/ia/avaliar/status?key=...
app.MapGet("/api/ia/avaliar", (string? key, int? limite, AvaliacaoIaJob job) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    var iniciou = job.Iniciar(Math.Clamp(limite ?? 50, 1, 500));
    return Results.Text(iniciou
        ? "Avaliação iniciada em segundo plano. Acompanhe em /api/ia/avaliar/status?key=..."
        : "Já está rodando.\n" + job.Status());
});

app.MapGet("/api/ia/avaliar/status", (string? key, AvaliacaoIaJob job) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    return Results.Text(job.Status());
});

// Uso real no dia a dia: quanto os atendentes aproveitam a sugestão
// /api/ia/relatorio?key=...&dias=30&emp=0
// Uso de hoje das travas de gasto da IA: /api/ia/limites?key=...
app.MapGet("/api/ia/limites", (string? key) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    return Results.Text(LimiteDiarioIa.Resumo());
});

app.MapGet("/api/ia/relatorio", async (string? key, int? dias, int? emp, AiService ai) =>
{
    if (string.IsNullOrWhiteSpace(key) || key != builder.Configuration["AdminKey"]) return Results.Unauthorized();
    return Results.Json(await ai.RelatorioAsync(dias ?? 30, emp ?? 0));
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

app.MapBlogEndpoints();
app.MapMobileEndpoints();
app.MapIndicacaoEndpoints();   // botão "Indique e ganhe" do sistema desktop

// Download de backup dos clientes: link assinado gerado pela tela Backups dos Clientes (vale 2h).
// O arquivo vem do FTP direto para o navegador, passando pelo HELP; a senha do FTP nunca sai do servidor.
app.MapGet("/api/backups-clientes/download", async (HttpContext http, string? p, long? e, string? u, string? s,
    BackupFtpService ftp, AdminService admin) =>
{
    if (!BackupFtpService.LinkValido(p, e ?? 0, u, s))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        await http.Response.WriteAsync("Link inválido ou vencido. Atualize a tela Backups dos Clientes e clique em Baixar de novo.");
        return;
    }
    var usuario = (u ?? "").Trim().ToUpperInvariant();
    var permissoes = await admin.GetPermissoesUsuarioAsync(usuario);
    if (!permissoes.Contains("ADMIN_ACESSAR"))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        await http.Response.WriteAsync("Usuário sem permissão.");
        return;
    }
    try
    {
        LogHelper.Log($"[BACKUP-FTP] {usuario} baixou {p}");
        await ftp.BaixarParaAsync(p!, http.Response, http.RequestAborted);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        LogHelper.Log($"[BACKUP-FTP] Falha ao baixar {p}: {ex.GetType().Name}: {ex.Message}");
        if (!http.Response.HasStarted)
        {
            http.Response.StatusCode = ex is FileNotFoundException ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway;
            http.Response.ContentType = "text/plain; charset=utf-8";
            await http.Response.WriteAsync(ex is FileNotFoundException ? "Arquivo não encontrado no FTP." : "Não foi possível baixar do servidor de backup.");
        }
        else http.Abort();
    }
});
app.MapHub<ReuniaoHub>("/hubs/reuniao");

app.Run();

// Origem da rede local (localhost, 127.x, 10.x, 172.16-31.x, 192.168.x) — usado só em desenvolvimento
static bool OrigemDaRedeLocal(string origem)
{
    if (!Uri.TryCreate(origem, UriKind.Absolute, out var uri)) return false;
    if (uri.IsLoopback) return true;
    if (!System.Net.IPAddress.TryParse(uri.Host, out var ip)) return false;
    var b = ip.GetAddressBytes();
    if (b.Length != 4) return false;
    return b[0] == 10
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
        || (b[0] == 192 && b[1] == 168);
}
