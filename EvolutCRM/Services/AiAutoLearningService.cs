using EvolutCRM.Data;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

public sealed class AiAutoLearningService
{
    private readonly TicketQueryRepository _ticketRepo;
    private readonly AiTicketInsightsRepository _insightRepo;
    private readonly OpenAiClient _openAi;

    public AiAutoLearningService(
        TicketQueryRepository ticketRepo,
        AiTicketInsightsRepository insightRepo,
        OpenAiClient openAi)
    {
        _ticketRepo = ticketRepo;
        _insightRepo = insightRepo;
        _openAi = openAi;
    }

    /// <summary>
    /// Gera e salva os insights do ticket (1 por assunto).
    /// Se já existir insight e force=false, não faz nada.
    /// </summary>
    public async Task<List<TicketInsight>> GenerateAndSaveManyAsync(int CodTicketChamadoC, bool force = false)
    {
        if (!force)
        {
            var existing = await _insightRepo.ListByCodTicketChamadoCAsync(CodTicketChamadoC);
            if (existing.Count > 0) return existing;
        }

        var baseText = await _ticketRepo.BuildTicketBaseTextAsync(CodTicketChamadoC);

        var insights = await _openAi.GenerateInsightsAsync(baseText);

        await _insightRepo.ReplaceAllForTicketAsync(CodTicketChamadoC, insights);

        return insights;
    }

    /// <summary>
    /// (Opcional) Sugere soluções antigas por categoria e tag.
    /// </summary>
    public async Task<List<(int CodTicketChamadoC, int topicoIndex, string? resumo, string? tags, string? solucao, DateTime criadoEm)>> SuggestSimilarSolutionsAsync(
        string? categoria,
        string? tag,
        int top = 5)
    {
        return await _insightRepo.FindSimilarAsync(categoria, tag, question: null, top);
    }
}