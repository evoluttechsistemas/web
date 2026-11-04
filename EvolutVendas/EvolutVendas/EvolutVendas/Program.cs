using Blazored.LocalStorage;
using EvolutVendas.Components;
using EvolutVendas.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Servicos da aplicacao
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<BuscaVendasService>();
builder.Services.AddScoped<VendaService>();
builder.Services.AddScoped<BuscaEstatisticasService>();
builder.Services.AddScoped<BuscaProdutosService>();
builder.Services.AddScoped<BuscaClientesService>();

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(2);
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(5);
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

builder.Services.Configure<HubOptions>(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
