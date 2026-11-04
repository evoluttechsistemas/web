using Blazored.LocalStorage;
using EvolutTur;
using EvolutTur.Components;
using EvolutTur.Data;
using EvolutTur.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;

var builder = WebApplication.CreateBuilder(args);

// HttpClient para OpenWeather
builder.Services.AddHttpClient("OpenWeather", client =>
{
    client.BaseAddress = new Uri("https://api.openweathermap.org/data/2.5/");
});

// Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024 * 10; 
    });


// Configura Blazorise
builder.Services
    .AddBlazorise(options => options.Immediate = true)
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();

builder.Services.AddSingleton(new EmailSender(
    smtpHost: "smtp.gmail.com",
    smtpPort: 587,
    smtpUser: "nfe.evoluttech@gmail.com",
    smtpPass: "kezcmuibadkeuysi",
    from: "nfe.evoluttech@gmail.com"
));

// Serviços customizados
builder.Services.AddScoped<WeatherService>();

// Banco de dados
builder.Services.AddDbContext<MasterDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Master")));

// Injeção de string de conexão manual
string connectionString = builder.Configuration.GetConnectionString("Connection");
builder.Services.AddScoped(_ => new SqlConnection(connectionString));

builder.Services.AddScoped<UsuarioService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var conn = new SqlConnection(config.GetConnectionString("Master"));
    return new UsuarioService(conn, config);
});

builder.Services.AddScoped<CadastroService>();
builder.Services.AddScoped<VendedorService>();
builder.Services.AddScoped<EmpresaService>();
builder.Services.AddScoped<FornecedorService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<AutenticacaoService>();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<CustomAuthProvider>();
builder.Services.AddScoped<VendaService>();
builder.Services.AddScoped<ParametrosService>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthProvider>());

builder.Services.AddAuthorizationCore();
builder.Services.AddBlazoredLocalStorage();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

// Aqui já está correto para .NET 8
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
