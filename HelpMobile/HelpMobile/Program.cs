using HelpMobile;
using HelpMobile.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Endereço da API do HELP: wwwroot/appsettings.json (desenvolvimento) e appsettings.Production.json (publicado).
// Em desenvolvimento fica vazio: usa o mesmo endereço em que o app foi aberto (localhost ou o IP do PC)
// na porta do HELP — assim funciona no celular pelo IP sem configurar nada.
var apiBase = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase))
{
    var app = new Uri(builder.HostEnvironment.BaseAddress);
    var porta = builder.Configuration["ApiPorta"] ?? "5031";
    apiBase = $"{app.Scheme}://{app.Host}:{porta}";
}
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase.TrimEnd('/') + "/") });

// Singleton: o mesmo login vale para o app todo (e é carregado antes da primeira tela)
builder.Services.AddSingleton<AuthState>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<PushService>();
builder.Services.AddScoped<MenuState>();

var host = builder.Build();

// Carrega o login salvo no aparelho antes de abrir a primeira tela
await host.Services.GetRequiredService<AuthState>().CarregarAsync();

await host.RunAsync();
