namespace EvolutTech.IA.Painel.Services;

public enum SimulationMode { CustomerSupport, Technician, Commercial }
public enum ExplanationFormat { Text, Flow, Image }
public sealed record SimulationTurn(string Question, string Answer, ExplanationFormat Format = ExplanationFormat.Text, string Topic = "");
public sealed record SimulationRequest(string Question, SimulationMode Mode, int? TicketId = null,
    int? CrmId = null, string Segment = "", IReadOnlyList<SimulationTurn>? History = null, bool IncludeHistory = false,
    // Prints do cliente (data URIs já validadas) e o que foi lido neles. Não são gravados em auditoria.
    IReadOnlyList<string>? CustomerImages = null, string ImageSummary = "", string ImageSearchText = "",
    // Termos de busca sugeridos pelo modelo auxiliar (só para achar fontes; nunca é evidência).
    string SearchHint = "");
public sealed record KnowledgeEvidence(string Id, string Kind, string Title, string Locator,
    string Text, bool CustomerSafe, string Version = "", string Module = "",IReadOnlyList<NavigationAction>? Actions=null,ParameterDefinition? Parameter=null);
public sealed record ParameterDefinition(string Name,string Group,string Description);
public sealed record NavigationAction(string Label,string Path,string Shortcut,string TargetForm="");
public sealed record RetrievalContext(string Segment, string Version, List<KnowledgeEvidence> Sources,
    List<string> Warnings,InterfaceReview? InterfaceReview=null,List<InspectionImage>? Images=null,List<SourceCheck>? Checks=null);
public sealed record SourceCheck(string Source,string State,int Matches,string Detail);
public sealed record InterfaceReview(bool Checked,bool LayoutAvailable,int InterfaceSources,string Summary);
public sealed record InspectionImage(string Title,string DataUri,string Commit);
// Model: resposta final (onde a IA cruza as fontes). AuxModel: tarefas auxiliares baratas (reescrever a busca, ler o print).
public sealed record AiSettings(string Model = "gpt-4.1-mini", string AuxModel = "gpt-4.1-mini");
public sealed record KnowledgeSettings(Dictionary<string,string> ModuleProjects);
public sealed record GeneratedAnswer(string Answer, string TechnicalNotes, bool SufficientEvidence,
    string[] SourceIds, string FollowUp, int InputTokens = 0, int OutputTokens = 0, string[]? Steps = null);
public sealed record SimulationAnswer(Guid Id, string Answer, string TechnicalNotes, bool SufficientEvidence,
    string FollowUp, List<KnowledgeEvidence> Sources, List<string> Warnings, string Segment, string Version,
    string Model, int InputTokens, int OutputTokens, ExplanationFormat Format = ExplanationFormat.Text, string[]? Steps = null,
    string? ScreenImage = null, string? ScreenNotice = null,InterfaceReview? InterfaceReview=null,List<SourceCheck>? Checks=null,List<VideoReference>? Videos=null,
    // A orientação se apoia só em atendimento anterior resolvido (insight aprovado): o atendente deve conferir a versão.
    bool ResolvedCaseOnly=false);
public interface IKnowledgeRetriever
{
    Task<RetrievalContext> RetrieveAsync(SimulationRequest request, CancellationToken token);
}
public interface IAnswerProvider
{
    bool IsConfigured { get; }
    Task<GeneratedAnswer> GenerateAsync(SimulationRequest request, RetrievalContext context,
        AiSettings settings, CancellationToken token);
}
public class SimulationException(string message) : Exception(message);
public sealed class SupportTicketNotFoundException()
    : SimulationException("Chamado não encontrado para a empresa configurada.");
public sealed class CrmCardNotFoundException()
    : SimulationException("Card comercial não encontrado para a empresa configurada.");
