using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace EvolutTech.IA.Painel.Services;

public static class HelpApi
{
    internal const string Scheme = "HelpIntegration";

    public static void AddHelpApi(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HelpApiAuthentication>(Scheme, _ => { });
        services.AddAuthorizationBuilder().AddPolicy(Scheme, policy =>
            policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
        services.Configure<RateLimiterOptions>(options => options.AddConcurrencyLimiter(Scheme, limiter =>
        {
            limiter.PermitLimit = 4;
            limiter.QueueLimit = 0;
        }));
    }

    public static void MapHelpApi(this WebApplication app)
    {
        var tickets = app.MapGroup("/api/v1/help/chamados/{chamadoId:int:min(1)}")
            .RequireAuthorization(Scheme)
            .RequireRateLimiting(Scheme);
        tickets.MapGet("/contexto", GetContextAsync);
        tickets.MapPost("/respostas", AnswerAsync).WithMetadata(new RequestSizeLimitAttribute(65_536));
    }

    private static async Task<IResult> GetContextAsync(int chamadoId, SupportContextService support, CancellationToken token)
    {
        try
        {
            var context = await support.GetAsync(chamadoId, token);
            return context is null
                ? Results.NotFound(new { erro = "Chamado não encontrado." })
                : Results.Ok(new { chamadoId, segmento = context.Segment, versaoErp = context.Version });
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException or
            UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return Results.Problem("Não foi possível consultar o chamado.", statusCode: 503);
        }
    }

    private static async Task<IResult> AnswerAsync(int chamadoId, HelpQuestion input, SimulationService simulation,
        IDataProtectionProvider protection, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Pergunta) || input.Pergunta.Length > 2000)
            return Results.BadRequest(new { erro = "Informe uma pergunta de até 2.000 caracteres." });

        var protector = protection.CreateProtector("AtendimentoIA.HelpConversation.v1",
            chamadoId.ToString(CultureInfo.InvariantCulture)).ToTimeLimitedDataProtector();
        SimulationTurn[] history = [];
        if (!string.IsNullOrEmpty(input.Continuacao))
        {
            if (input.Continuacao.Length > 48_000)
                return InvalidContinuation();
            try
            {
                history = JsonSerializer.Deserialize<SimulationTurn[]>(protector.Unprotect(input.Continuacao)) ?? [];
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
            {
                return InvalidContinuation();
            }
        }

        var request = new SimulationRequest(input.Pergunta.Trim(), SimulationMode.CustomerSupport,
            TicketId: chamadoId, History: history);
        try
        {
            var answer = await simulation.AskAsync(request, token);
            var previousAnswer = ExternalText.Clean(answer.Answer, 1250) + "\n" + ExternalText.Clean(answer.FollowUp, 500);
            var turn = new SimulationTurn(request.Question, previousAnswer, answer.Format, ExplanationPolicy.Topic(request));
            var continuation = protector.Protect(JsonSerializer.Serialize(new[] { turn }),
                TimeSpan.FromHours(8));
            return Results.Ok(new
            {
                answer.Id,
                chamadoId,
                resposta = answer.Answer,
                perguntaDeAcompanhamento = answer.FollowUp,
                evidenciasSuficientes = answer.SufficientEvidence,
                segmento = answer.Segment,
                versaoErp = answer.Version,
                formato = answer.Format switch { ExplanationFormat.Flow => "fluxo", ExplanationFormat.Image => "imagem", _ => "texto" },
                passos = answer.Steps ?? [],
                imagem = answer.ScreenImage,
                avisoDaImagem = answer.ScreenNotice,
                videos = answer.Videos?.Select(video => new { titulo = video.Title, url = video.Url }),
                continuacao = continuation
            });
        }
        catch (SupportTicketNotFoundException)
        {
            return Results.NotFound(new { erro = "Chamado não encontrado." });
        }
        catch (Exception ex) when (ex is SimulationException or CryptographicException)
        {
            return Results.Problem("Não foi possível gerar a resposta. Tente novamente em instantes.", statusCode: 503);
        }
    }

    private static IResult InvalidContinuation() =>
        Results.BadRequest(new { erro = "Continuação inválida ou expirada. Inicie uma nova conversa para este chamado." });
}

public sealed record HelpQuestion(string? Pergunta, string? Continuacao);

internal sealed class HelpApiAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IConfiguration configuration, IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredToken = configuration["HelpApi:Token"];
        var authorization = Request.Headers.Authorization.ToString();
        if ((!environment.IsDevelopment() && !Request.IsHttps) || string.IsNullOrWhiteSpace(configuredToken) ||
            configuredToken.Length is < 32 or > 500 || authorization.Length > 512 ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedToken = authorization["Bearer ".Length..];
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken)),
                SHA256.HashData(Encoding.UTF8.GetBytes(providedToken))))
            return Task.FromResult(AuthenticateResult.Fail("Credencial do Help inválida."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Help")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
