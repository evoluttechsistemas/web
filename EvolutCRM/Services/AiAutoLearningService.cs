using EvolutCRM.Data;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

public sealed class AiAutoLearningService
{
    private readonly TicketQueryRepository _ticketRepo;
    private readonly AiTicketInsightsRepository _insightRepo;
    private readonly OpenAiClient _openAi;
    private readonly EmbeddingIndexerService _indexer;
    private readonly EmbeddingIndex _index;

    public AiAutoLearningService(
        TicketQueryRepository ticketRepo,
        AiTicketInsightsRepository insightRepo,
        OpenAiClient openAi,
        EmbeddingIndexerService indexer,
        EmbeddingIndex index)
    {
        _ticketRepo = ticketRepo;
        _insightRepo = insightRepo;
        _openAi = openAi;
        _indexer = indexer;
        _index = index;
    }

    /// <summary>
    /// Gera e salva os insights do ticket (1 por assunto).
    /// Se já existir insight e force=false, não faz nada.
    /// </summary>
    public async Task<List<TicketInsight>> GenerateAndSaveManyAsync(int CodTicketChamadoC, bool force = false, int codEmp = 0)
    {
        if (!force)
        {
            var existing = await _insightRepo.ListByCodTicketChamadoCAsync(CodTicketChamadoC);
            if (existing.Count > 0) return existing;
        }

        // Ticket inteiro (Cliente / Atendente / Nota interna, áudios transcritos) + até 3 prints
        var ticket = await _ticketRepo.MontarParaInsightAsync(CodTicketChamadoC, codEmp);

        var insights = await _openAi.GenerateInsightsAsync(ticket.Texto, ticket.Imagens);

        await _insightRepo.ReplaceAllForTicketAsync(CodTicketChamadoC, insights);

        // Já entra no índice da IA: não precisa esperar o reindexar
        try
        {
            await _indexer.IndexarInsightsDoTicketAsync(CodTicketChamadoC);
            await _index.AtualizarInsightsDoTicketAsync(CodTicketChamadoC);
        }
        catch (Exception ex)
        {
            // O insight já está salvo; se falhar aqui, a rotina da madrugada coloca no índice
            LogHelper.Log($"[INSIGHT] Ticket #{CodTicketChamadoC}: erro ao colocar no índice: {ex.Message}");
        }

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