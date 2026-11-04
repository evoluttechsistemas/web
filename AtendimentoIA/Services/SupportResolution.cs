using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class SupportResolution
{
    public const string Question="Conseguiu fazer? O problema foi resolvido?";
    public static bool IsConfirmed(SimulationRequest request)=>request.Mode==SimulationMode.CustomerSupport&&request.History?.LastOrDefault() is { } last&&
        KnowledgeSearch.Normalize(last.Answer).Contains("problema foi resolvido")&&
        Regex.IsMatch(KnowledgeSearch.Normalize(request.Question).Trim(),@"^(sim|sim[,!. ]+(?:resolveu|resolvido|funcionou|deu certo)|resolveu|resolvido|funcionou|deu certo|problema resolvido)[.! ]*$");
    public static SimulationAnswer Ask(SimulationAnswer answer,SimulationMode mode)
    {
        if(mode!=SimulationMode.CustomerSupport) return answer;
        if((answer.Answer+" "+answer.FollowUp).Contains(Question,StringComparison.OrdinalIgnoreCase)) return answer;
        return answer with {FollowUp=(string.IsNullOrWhiteSpace(answer.FollowUp)?"":answer.FollowUp.Trim()+"\n\n")+Question};
    }
}
