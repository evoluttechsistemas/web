using EvolutDelivery.Components;
using EvolutDelivery.Models;
using EvolutDelivery.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ===== SERVIÇOS DA APLICAÇÃO =====
builder.Services.AddScoped<ProdutoService>();
builder.Services.AddScoped<StorageService>();
builder.Services.AddScoped<VendaService>();

builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<EntregadorService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<FormaPagamentoService>();
builder.Services.AddScoped<EmpresaService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddControllers();

builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DetailedErrors = true; // ADICIONE ISSO
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(6);
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(10);
        options.MaxBufferedUnacknowledgedRenderBatches = 20;
    })
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
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
    app.UseDeveloperExceptionPage(); // ADICIONE ISSO
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

// Mapear hub de notificações
app.MapHub<NotificacaoHub>("/notificacoes");


app.MapPost("/auth/login", async (HttpContext httpContext, LoginService loginService) =>
{
    var form = await httpContext.Request.ReadFormAsync();

    var cnpj = form["CNPJ"].ToString().Trim();
    var usuario = form["Usuario"].ToString().Trim();
    var senha = form["Senha"].ToString();
    var rememberMe = form["RememberMe"].ToString() == "true";
    var returnUrl = form["ReturnUrl"].ToString();
    var codEmpTexto = form["CodEmp"].ToString();

    if (!int.TryParse(codEmpTexto, out var codEmp) || codEmp <= 0)
    {
        var destinoErro = $"/login?error=1&ReturnUrl={Uri.EscapeDataString(string.IsNullOrWhiteSpace(returnUrl) ? "/admin-dashboard" : returnUrl)}";
        return Results.Redirect(destinoErro);
    }

    var usuarioLogado = await loginService.ValidarLoginAsync(cnpj, usuario, senha, codEmp);

    if (usuarioLogado is null)
    {
        var destinoErro = $"/login?error=1&ReturnUrl={Uri.EscapeDataString(string.IsNullOrWhiteSpace(returnUrl) ? "/admin-dashboard" : returnUrl)}";
        return Results.Redirect(destinoErro);
    }

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

    var destino = string.IsNullOrWhiteSpace(returnUrl)
        ? "/admin-dashboard"
        : returnUrl;

    return Results.Redirect(destino);
});

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { success = true });
});
app.MapControllers();
app.Run();