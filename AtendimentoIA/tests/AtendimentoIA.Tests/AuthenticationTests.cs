using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AuthenticationTests
{
    private const string Password="Fixture-Only#123";
    private static PanelAccount Account()=>new(10,"OPERADOR",BCrypt.Net.BCrypt.HashPassword(Password,4),"outra senha",false,"v1");
    public static async Task Eligibility()
    {
        var store=new FakeAccounts(Account());var login=new PanelLoginService(store,NullLogger<PanelLoginService>.Instance);
        if(!(await login.VerifyPasswordAsync(" operador ",Password,default)).Success||store.LastUsername!="OPERADOR") throw new Exception("The HELP login was not normalized");
        if((await login.VerifyPasswordAsync("OPERADOR","outra senha",default)).Success) throw new Exception("A stored hash was bypassed using the legacy password");
        foreach(var account in new[]{Account() with {CompanyId=3},Account() with {Active=false},Account() with {Help=false},Account() with {Locked=true}})
        {
            store.Account=account;
            if((await login.VerifyPasswordAsync("OPERADOR",Password,default)).Success) throw new Exception("An ineligible account signed in");
        }
        store.Account=Account() with {PasswordHash="",LegacyPassword=" Case Sensitive "};
        if(!(await login.VerifyPasswordAsync("OPERADOR"," Case Sensitive ",default)).Success||(await login.VerifyPasswordAsync("OPERADOR","case sensitive",default)).Success) throw new Exception("Legacy passwords lost their exact semantics");
        store.Account=Account() with {PasswordHash="invalid",LegacyPassword=Password};
        if((await login.VerifyPasswordAsync("OPERADOR",Password,default)).Success) throw new Exception("An invalid hash fell back to plaintext");
        store.Account=Account();
        var principal=PanelLoginService.Principal(store.Account,DateTimeOffset.UtcNow);
        if(!await login.ValidateAsync(principal,default)||principal.Claims.Any(c=>c.Value.Contains(Password)||c.Value.Contains("$2"))) throw new Exception("Invalid identity or secret material in the session");
        store.Account=store.Account with {PasswordVersion="changed"};
        if(await login.ValidateAsync(principal,default)) throw new Exception("Password change did not revoke the session");
        store.Account=Account();
        if(await login.ValidateAsync(PanelLoginService.Principal(store.Account,DateTimeOffset.UtcNow.AddHours(-9)),default)) throw new Exception("Expired circuit remained authorized");
        foreach(var unsafeUrl in new[]{"//evil.example","/\\evil.example","https://evil.example","/auth/logout","/login","/\r\nLocation: evil"})
            if(PanelAuthentication.SafeReturnUrl(unsafeUrl)!="/") throw new Exception("An external/auth redirect was accepted");
        if(PanelAuthentication.SafeReturnUrl("/simulador?x=1")!="/simulador?x=1") throw new Exception("Local destination was lost");
        if(!SqlPanelAccountStore.Query.Contains("CodEmp=@CompanyId")||!SqlPanelAccountStore.Query.Contains("Help,'N')='S'")) throw new Exception("The tenant/HELP predicate was lost");
        if(SqlPanelAccountStore.Query.Contains("Requer2FA")||SqlPanelAccountStore.Query.Contains("EmailVerificado")) throw new Exception("The password-only login still depends on e-mail settings");
    }
    public static async Task HttpSession()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"auth-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Development",ContentRootPath=root});
        builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Services.AddAntiforgery();builder.Services.AddPanelAuthentication(builder.Environment);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root,"keys")));
        const string integrationToken = "fixture-help-token-for-http-tests-only";
        builder.Configuration["HelpApi:Token"] = integrationToken;
        builder.Services.AddHelpApi();
        builder.Services.AddSingleton<PanelStore>();
        builder.Services.AddSingleton<SecretStore>();
        builder.Services.AddSingleton<SupportContextService>();
        builder.Services.AddScoped<IKnowledgeRetriever, HelpFixtureRetriever>();
        builder.Services.AddScoped<IAnswerProvider, HelpFixtureProvider>();
        builder.Services.AddScoped<SimulationService>();
        var accounts=new FakeAccounts(Account());
        builder.Services.RemoveAll<IPanelAccountStore>();
        builder.Services.AddSingleton<IPanelAccountStore>(accounts);
        await using var app=builder.Build();
        app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.UseAntiforgery();app.MapPanelAuthentication();
        app.MapHelpApi();
        app.MapGet("/login",()=>"login").AllowAnonymous();
        app.MapGet("/token",(HttpContext context,IAntiforgery anti)=>Results.Json(new {token=anti.GetAndStoreTokens(context).RequestToken})).AllowAnonymous();
        app.MapGet("/private",()=>"protected").RequireAuthorization();
        await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,CookieContainer=new CookieContainer()}) {BaseAddress=new Uri(address)};
            async Task<Dictionary<string,string>> Form(params (string Key,string Value)[] values)
            {
                using var response=await client.GetAsync("/token");using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var form=values.ToDictionary(p=>p.Key,p=>p.Value);form.Add("__RequestVerificationToken",json.RootElement.GetProperty("token").GetString()!);return form;
            }
            using(var denied=await client.GetAsync("/private"))
                if(denied.StatusCode!=HttpStatusCode.Redirect||!denied.Headers.Location!.ToString().Contains("/login")) throw new Exception("Anonymous protected access was allowed");
            using(var noCsrf=await client.PostAsync("/auth/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username","OPERADOR"},{"password",Password}})))
                if(noCsrf.StatusCode!=HttpStatusCode.BadRequest) throw new Exception("Login without antiforgery token was accepted");
            using(var invalid=await client.PostAsync("/auth/login",new FormUrlEncodedContent(await Form(("username","OPERADOR"),("password","wrong")))))
                if(!invalid.Headers.Location!.ToString().Contains("invalid")) throw new Exception("Wrong password did not fail");
            using(var valid=await client.PostAsync("/auth/login",new FormUrlEncodedContent(await Form(("username","OPERADOR"),("password",Password),("returnUrl","/private")))))
                if(valid.Headers.Location?.ToString()!="/private"||!valid.Headers.GetValues("Set-Cookie").Any(v=>v.Contains("AtendimentoIA.Session=")&&v.Contains("httponly",StringComparison.OrdinalIgnoreCase))) throw new Exception("Server session cookie was not issued");
            using(var authorized=await client.GetAsync("/private")) if(authorized.StatusCode!=HttpStatusCode.OK) throw new Exception("Valid session could not access the panel");
            const string answerRoute = "/api/v1/help/chamados/12/respostas";
            using(var denied = await client.PostAsJsonAsync(answerRoute, new { pergunta = "Como registrar perdas?" }))
                if(denied.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Panel cookie granted Help API access");
            using(var integration = new HttpClient { BaseAddress = client.BaseAddress })
            {
                integration.DefaultRequestHeaders.Authorization = new("Bearer", integrationToken);
                using var first = await integration.PostAsJsonAsync(answerRoute, new { pergunta = "Como registrar perdas?" });
                first.EnsureSuccessStatusCode();
                using var response = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
                var continuation = response.RootElement.GetProperty("continuacao").GetString();
                if(response.RootElement.TryGetProperty("technicalNotes", out _) ||
                   response.RootElement.GetProperty("formato").GetString() != "texto")
                    throw new Exception("Unexpected Help response contract");
                using var next = await integration.PostAsJsonAsync(answerRoute, new { pergunta = "Não entendi", continuacao = continuation });
                next.EnsureSuccessStatusCode();
                using var nextResponse = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
                if(nextResponse.RootElement.GetProperty("formato").GetString() != "fluxo")
                    throw new Exception("Help clarification lost its history");
                using var wrongTicket = await integration.PostAsJsonAsync("/api/v1/help/chamados/13/respostas",
                    new { pergunta = "Não entendi", continuacao = continuation });
                if(wrongTicket.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Continuation crossed ticket boundary");
                using var missing = await integration.PostAsJsonAsync("/api/v1/help/chamados/99/respostas", new { pergunta = "Perdas?" });
                if(missing.StatusCode != HttpStatusCode.NotFound) throw new Exception("Missing ticket did not return 404");
            }
            using(var logout=await client.PostAsync("/auth/logout",new FormUrlEncodedContent(await Form()))) if(logout.StatusCode!=HttpStatusCode.Redirect) throw new Exception("Logout failed");
            using(var denied=await client.GetAsync("/private")) if(denied.StatusCode!=HttpStatusCode.Redirect) throw new Exception("Logout left the panel accessible");
            // There is no mail sender registered: valid credentials alone must issue the session.
            using(var signedIn=await client.PostAsync("/auth/login",new FormUrlEncodedContent(await Form(("username","OPERADOR"),("password",Password),("returnUrl","/private")))))
                if(signedIn.Headers.Location?.ToString()!="/private") throw new Exception("Password login still requires a verification step");
            accounts.Account=accounts.Account with {Active=false};
            using(var revoked=await client.GetAsync("/private")) if(revoked.StatusCode!=HttpStatusCode.Redirect) throw new Exception("Deactivated account kept its session");
        }
        finally {await app.StopAsync();}
    }
    private sealed class FakeAccounts(PanelAccount account):IPanelAccountStore
    {
        public PanelAccount Account {get;set;}=account;
        public string LastUsername {get;private set;}="";
        public Task<PanelAccount?> FindAsync(string username,CancellationToken token) {LastUsername=username;return Task.FromResult<PanelAccount?>(username==Account.Name?Account:null);}
        public Task<PanelAccount?> FindByIdAsync(int id,CancellationToken token)=>Task.FromResult<PanelAccount?>(id==Account.Id?Account:null);
    }
    private sealed class HelpFixtureRetriever : IKnowledgeRetriever
    {
        public Task<RetrievalContext> RetrieveAsync(SimulationRequest request, CancellationToken token)
        {
            if (request.TicketId == 99) throw new SupportTicketNotFoundException();
            return Task.FromResult(new RetrievalContext("Varejo", "26.10.06",
                [new("S1", "guidance", "Perdas", "fixture", "Registrar perdas", true)], []));
        }
    }
    private sealed class HelpFixtureProvider : IAnswerProvider
    {
        public bool IsConfigured => true;
        public Task<GeneratedAnswer> GenerateAsync(SimulationRequest request, RetrievalContext context,
            AiSettings settings, CancellationToken token) => Task.FromResult(
                new GeneratedAnswer("Abra Lançamento de Perdas.", "Nota interna de fixture", true,
                    ["S1"], "", Steps: ["Abra a tela.", "Informe o produto."]));
    }
}
