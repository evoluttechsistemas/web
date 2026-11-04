using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.WebUtilities;
using System.Threading.RateLimiting;

namespace EvolutTech.IA.Painel.Services;

public static class PanelAuthentication
{
    public static IServiceCollection AddPanelAuthentication(this IServiceCollection services,IHostEnvironment environment)
    {
        services.AddScoped<IPanelAccountStore,SqlPanelAccountStore>();
        services.AddScoped<PanelLoginService>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options=>
        {
            options.LoginPath="/login";options.AccessDeniedPath="/login";
            options.Cookie.Name="AtendimentoIA.Session";options.Cookie.HttpOnly=true;options.Cookie.SameSite=SameSiteMode.Lax;
            options.Cookie.SecurePolicy=environment.IsDevelopment()?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
            options.ExpireTimeSpan=PanelLoginService.SessionLifetime;options.SlidingExpiration=false;
            options.Events.OnValidatePrincipal=async context=>
            {
                if(!await context.HttpContext.RequestServices.GetRequiredService<PanelLoginService>().ValidateAsync(context.Principal!,context.HttpContext.RequestAborted))
                {context.RejectPrincipal();await context.HttpContext.SignOutAsync();}
            };
            options.Events.OnRedirectToLogin=context=>
            {
                if(context.Request.Path.StartsWithSegments("/_blazor")) context.Response.StatusCode=401;
                else context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization(options=>
        {
            options.DefaultPolicy=new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("company_id","2").Build();
            options.FallbackPolicy=options.DefaultPolicy;
        });
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider,PanelAuthenticationStateProvider>();
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options=>
            {options.ValueLengthLimit=4096;options.ValueCountLimit=16;options.KeyLengthLimit=128;});
        services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=429;
            options.AddPolicy("panel-login",context=>RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions
                {PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
            options.OnRejected=async (context,token)=>await context.HttpContext.Response.WriteAsync("Muitas tentativas. Aguarde um minuto e tente novamente.",token);
        });
        return services;
    }
    public static string SafeReturnUrl(string? value)=>value is {Length:>0 and <=2000}&&value.StartsWith('/')&&
        !value.StartsWith("//")&&!value.Contains('\\')&&!value.Any(char.IsControl)&&
        !value.StartsWith("/login",StringComparison.OrdinalIgnoreCase)&&!value.StartsWith("/auth",StringComparison.OrdinalIgnoreCase)?value:"/";
    private static IResult LoginError(string code,string returnUrl)=>Results.LocalRedirect(QueryHelpers.AddQueryString("/login",
        new Dictionary<string,string?> {{"error",code},{"returnUrl",SafeReturnUrl(returnUrl)}}));
    private static async Task<bool> ValidateForm(HttpContext context,IAntiforgery antiforgery)
    {
        if(!context.Request.HasFormContentType||context.Request.ContentLength>8192) return false;
        try {await antiforgery.ValidateRequestAsync(context);return true;}
        catch(AntiforgeryValidationException) {return false;}
    }
    private static async Task SignIn(HttpContext context,PanelAccount account)
    {
        var now=DateTimeOffset.UtcNow;
        await context.SignInAsync(PanelLoginService.Principal(account,now),new AuthenticationProperties
            {IsPersistent=false,IssuedUtc=now,ExpiresUtc=now.Add(PanelLoginService.SessionLifetime),AllowRefresh=false});
    }
    public static void MapPanelAuthentication(this WebApplication app)
    {
        app.MapPost("/auth/login",async (HttpContext context,IAntiforgery antiforgery,PanelLoginService login)=>
        {
            if(!await ValidateForm(context,antiforgery)) return Results.BadRequest("Formulário expirado. Recarregue a página e tente novamente.");
            var form=await context.Request.ReadFormAsync(context.RequestAborted);var returnUrl=SafeReturnUrl(form["returnUrl"]);
            context.Response.Cookies.Delete("AtendimentoIA.LoginChallenge");
            var result=await login.VerifyPasswordAsync(form["username"].ToString(),form["password"].ToString(),context.RequestAborted);
            if(!result.Success) return LoginError(result.Error,returnUrl);
            var account=result.Account!;
            await SignIn(context,account);return Results.LocalRedirect(returnUrl);
        }).AllowAnonymous().RequireRateLimiting("panel-login");
        app.MapPost("/auth/logout",async (HttpContext context,IAntiforgery antiforgery)=>
        {
            if(!await ValidateForm(context,antiforgery)) return Results.BadRequest();
            await context.SignOutAsync();return Results.LocalRedirect("/login");
        }).RequireAuthorization();
    }
}
public sealed class PanelAuthenticationStateProvider(ILoggerFactory logger,IServiceScopeFactory scopes):RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval=>TimeSpan.FromMinutes(5);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state,CancellationToken cancellationToken)
    {
        await using var scope=scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PanelLoginService>().ValidateAsync(state.User,cancellationToken);
    }
}
