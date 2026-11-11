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
        // Até 2 prints de 4 MB (em base64) + pergunta e continuação.
        tickets.MapPost("/respostas", AnswerAsync).WithMetadata(new RequestSizeLimitAttribute(12_000_000));

        // Cards do CRM (comercial): mesma resposta, no modo Comercial (funcionalidades e condições, sem preços inventados).
        var cards = app.MapGroup("/api/v1/help/cards/{cardId:int:min(1)}")
            .RequireAuthorization(Scheme)
            .RequireRateLimiting(Scheme);
        cards.MapPost("/respostas", CardAnswerAsync).WithMetadata(new RequestSizeLimitAttribute(12_000_000));
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

    private static Task<IResult> AnswerAsync(int chamadoId, HelpQuestion input, SimulationService simulation,
        [FromServices] CustomerImageReader imageReader, IDataProtectionProvider protection, ILoggerFactory loggers, CancellationToken token) =>
        RespondAsync(chamadoId, SimulationMode.CustomerSupport, input, simulation, imageReader, protection, loggers, token);

    private static Task<IResult> CardAnswerAsync(int cardId, HelpQuestion input, SimulationService simulation,
        [FromServices] CustomerImageReader imageReader, IDataProtectionProvider protection, ILoggerFactory loggers, CancellationToken token) =>
        RespondAsync(cardId, SimulationMode.Commercial, input, simulation, imageReader, protection, loggers, token);

    private static async Task<IResult> RespondAsync(int chamadoId, SimulationMode mode, HelpQuestion input, SimulationService simulation,
        CustomerImageReader imageReader, IDataProtectionProvider protection, ILoggerFactory loggers, CancellationToken token)
    {
        var commercial = mode == SimulationMode.Commercial;
        var images = new List<string>();
        try
        {
            if (input.Imagens is { Count: > CustomerImageReader.MaxImages })
                return Results.BadRequest(new { erro = $"Envie no máximo {CustomerImageReader.MaxImages} imagens por pergunta." });
            foreach (var image in input.Imagens ?? []) images.Add(CustomerImageReader.Validate(image));
        }
        catch (SimulationException ex)
        {
            return Results.BadRequest(new { erro = ex.Message });
        }
        var question = (input.Pergunta ?? "").Trim();
        if (question.Length == 0 && images.Count > 0) question = "O que aparece nesta imagem e como resolvo?";
        if (question.Length == 0 || question.Length > 2000)
            return Results.BadRequest(new { erro = "Informe uma pergunta de até 2.000 caracteres." });

        // Conversa de card e de chamado nunca se misturam (propósitos diferentes na proteção).
        var protector = protection.CreateProtector(commercial ? "AtendimentoIA.HelpCardConversation.v1" : "AtendimentoIA.HelpConversation.v1",
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

        try
        {
            // Lê o print antes da busca: o texto da tela e do erro é o que mais ajuda a achar a fonte certa.
            var reading = new CustomerImageReader.ImageReading("", "");
            if (images.Count > 0)
            {
                try { reading = await imageReader.ReadAsync(images, question, token); }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is SimulationException or HttpRequestException
                                           or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException)
                {
                    // Sem a leitura, a resposta ainda vê o print na geração; só a busca fica sem o texto da tela.
                    loggers.CreateLogger("HelpApi").LogWarning("Leitura do print falhou no chamado {Chamado}: {Tipo}.", chamadoId, ex.GetType().Name);
                }
            }
            var request = new SimulationRequest(question, mode,
                TicketId: commercial ? null : chamadoId, CrmId: commercial ? chamadoId : null, History: history, CustomerImages: images,
                ImageSummary: reading.Summary, ImageSearchText: reading.SearchText);
            var answer = await simulation.AskAsync(request, token);
            var previousAnswer = ExternalText.Clean(answer.Answer, 1250) + "\n" + ExternalText.Clean(answer.FollowUp, 500);
            // A próxima pergunta ("e agora?") continua sabendo o que estava no print, sem reenviar a imagem.
            var turnQuestion = reading.Summary.Length > 0 ? ExternalText.Clean(request.Question + "\n[Print do cliente] " + reading.Summary, 1900) : request.Question;
            var turn = new SimulationTurn(turnQuestion, previousAnswer, answer.Format, ExplanationPolicy.Topic(request));
            var continuation = protector.Protect(JsonSerializer.Serialize(new[] { turn }),
                TimeSpan.FromHours(8));
            return Results.Ok(new
            {
                answer.Id,
                chamadoId,
                resposta = answer.Answer,
                perguntaDeAcompanhamento = answer.FollowUp,
                evidenciasSuficientes = answer.SufficientEvidence,
                baseadoEmCasoAnterior = answer.ResolvedCaseOnly,
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
        catch (CrmCardNotFoundException)
        {
            return Results.NotFound(new { erro = "Card não encontrado." });
        }
        catch (Exception ex) when (ex is SimulationException or CryptographicException)
        {
            return Results.Problem("Não foi possível gerar a resposta. Tente novamente em instantes.", statusCode: 503);
        }
    }

    private static IResult InvalidContinuation() =>
        Results.BadRequest(new { erro = "Continuação inválida ou expirada. Inicie uma nova conversa para este chamado." });
}

public sealed record HelpQuestion(string? Pergunta, string? Continuacao, IReadOnlyList<string>? Imagens = null);

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
