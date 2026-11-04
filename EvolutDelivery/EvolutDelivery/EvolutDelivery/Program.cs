using EvolutDelivery.Components;
using EvolutDelivery.Models;
using EvolutDelivery.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// ===== SERVIÇOS DA APLICAÇÃO =====

// Nível de log vem do appsettings.json (Logging:LogLevel)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ProdutoService>();
builder.Services.AddScoped<StorageService>();
builder.Services.AddScoped<CarrinhoService>();
builder.Services.AddScoped<LojaStatusService>();

builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<EntregadorService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<FormaPagamentoService>();
builder.Services.AddScoped<EmpresaService>();
builder.Services.AddSingleton<PedidoLinkService>();
builder.Services.AddControllers();

builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        // Detalhes de erro só em desenvolvimento (em produção não expõe o código ao cliente)
        options.DetailedErrors = builder.Environment.IsDevelopment();
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(6);
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(10);
        options.MaxBufferedUnacknowledgedRenderBatches = 20;
    })
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // ← ADICIONA ISSO (10MB)
    });

// AUTENTICAÇÃO
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "EvolutDeliveryAuth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped(sp =>
{
    var navigation = sp.GetRequiredService<NavigationManager>();
    return new HttpClient
    {
        BaseAddress = new Uri(navigation.BaseUri)
    };
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    //app.UseHsts();
}

//app.UseHttpsRedirection();
app.UsePathBase("/EvolutDelivery");
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ===== IMAGENS DOS PRODUTOS =====
// GET /EvolutDelivery/img/{slug}/{codigo}
// Entrega a imagem por HTTP com cache (o navegador guarda e só baixa o que aparece na tela),
// em vez de mandar base64 pelo SignalR.
app.MapGet("/img/{slug}/{codigo:int}", async (
    string slug,
    int codigo,
    HttpContext httpContext,
    EmpresaService empresaService,
    ProdutoService produtoService,
    IMemoryCache cache,
    ILogger<Program> logger) =>
{
    if (codigo <= 0 || string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
        return Results.NotFound();

    try
    {
        // slug -> CNPJ (cache de 30 min para não consultar a empresa a cada imagem)
        var chave = "cnpj-por-slug:" + slug.ToLowerInvariant();
        if (!cache.TryGetValue(chave, out string? cnpj))
        {
            cnpj = (await empresaService.BuscarPorSlugAsync(slug))?.CNPJ;
            if (!string.IsNullOrWhiteSpace(cnpj))
                cache.Set(chave, cnpj, TimeSpan.FromMinutes(30));
        }

        if (string.IsNullOrWhiteSpace(cnpj))
            return Results.NotFound();

        var imagem = await produtoService.BuscarImagemAsync(codigo, cnpj);

        if (imagem is null)
        {
            // Produto sem imagem: deixa o navegador lembrar por 10 min
            httpContext.Response.Headers.CacheControl = "public, max-age=600";
            return Results.NotFound();
        }

        var (bytes, contentType) = imagem.Value;
        var etag = new EntityTagHeaderValue("\"" + Convert.ToHexString(SHA256.HashData(bytes))[..32] + "\"");

        // 1 hora de cache; depois disso o navegador revalida pelo ETag (resposta 304, sem baixar de novo)
        httpContext.Response.Headers.CacheControl = "public, max-age=3600";

        return Results.File(bytes, contentType, entityTag: etag);
    }
    catch (Exception ex)
    {
        // Banco fora do ar etc.: não derruba a página, só a imagem não aparece
        logger.LogError(ex, "Erro ao buscar imagem {Codigo} da loja {Slug}", codigo, slug);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});


// Aceita só endereços locais (ex.: "/EvolutDelivery/admin-dashboard").
// Bloqueia redirecionamento para sites externos ("https://...", "//site", "/\\site").
static string UrlLocalOuPadrao(string? url, string padrao)
{
    if (string.IsNullOrWhiteSpace(url))
        return padrao;

    url = url.Trim();

    bool local = url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\")
        && Uri.IsWellFormedUriString(url, UriKind.Relative);

    return local ? url : padrao;
}

app.MapPost("/auth/login", async (HttpContext httpContext, LoginService loginService, ILogger<Program> logger) =>
{
    var pathBase = httpContext.Request.PathBase.Value ?? "";
    var destinoPadrao = $"{pathBase}/admin-dashboard";

    var form = await httpContext.Request.ReadFormAsync();

    var cnpj = form["CNPJ"].ToString().Trim();
    var usuario = form["Usuario"].ToString().Trim();
    var senha = form["Senha"].ToString();
    var rememberMe = form["RememberMe"].ToString() == "true";
    var returnUrl = UrlLocalOuPadrao(form["ReturnUrl"].ToString(), destinoPadrao);
    var codEmpTexto = form["CodEmp"].ToString();

    var destinoErro = $"{pathBase}/login?error=1&ReturnUrl={Uri.EscapeDataString(returnUrl)}";

    if (!int.TryParse(codEmpTexto, out var codEmp) || codEmp <= 0)
        return Results.LocalRedirect(destinoErro);

    UsuarioLoginModel? usuarioLogado;
    try
    {
        usuarioLogado = await loginService.ValidarLoginAsync(cnpj, usuario, senha, codEmp);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao validar login");
        usuarioLogado = null;
    }

    if (usuarioLogado is null)
        return Results.LocalRedirect(destinoErro);

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, usuarioLogado.Codigo.ToString()),
        new Claim(ClaimTypes.Name, usuarioLogado.Usuario ?? string.Empty),
        new Claim("CNPJ", usuarioLogado.CNPJ ?? string.Empty),
        new Claim("CodEmp", codEmp.ToString()),
        new Claim("CodVendedor", usuarioLogado.CodVendedor?.ToString() ?? string.Empty),
        new Claim(ClaimTypes.Role, "Admin")
    };

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme);

    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(rememberMe ? 24 : 8)
        });

    // LocalRedirect recusa qualquer URL externa
    return Results.LocalRedirect(returnUrl);
});

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    var pathBase = httpContext.Request.PathBase.Value ?? "";
    return Results.LocalRedirect($"{pathBase}/login");
});
app.MapControllers();
app.Run();