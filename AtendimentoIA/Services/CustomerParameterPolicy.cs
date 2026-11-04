using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class CustomerParameterPolicy
{
    public static string Apply(string answer,IReadOnlyList<KnowledgeEvidence> sources,bool sufficient,bool includeHandoff=true)
    {
        var normalized=KnowledgeSearch.Normalize(answer);
        var hasParameter=sources.Any(s=>s.Kind=="parameter");
        const string directPattern=@"\b(?:acesse|abra|altere|ative|habilite|desative|configure|modifique|ajuste|verifique|confira|consulte|veja)\s+(?:se\s+)?(?:(?:o|a|os|as|esse|essa|este|esta|um|uma)\s+)?(?:par[aâ]metro\w*|configura[cç][aã]o\w*)\b[^.!?\n]*(?:[.!?]|$)";
        var directInstruction=Regex.IsMatch(answer,directPattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        if(directInstruction)
        {
            var handoff=sufficient&&hasParameter
                ? $"Peça ao suporte da EvolutTech para avaliar o parâmetro {string.Join(", ",sources.Where(s=>s.Kind=="parameter").Select(s=>ExternalText.CustomerAnswer(s.Title.Split('·')[0].Trim())).Distinct())} e fazer o ajuste necessário no seu ambiente."
                : "Peça ao suporte da EvolutTech a avaliação da parametrização necessária para essa opção.";
            answer=Regex.Replace(answer,directPattern,_=>handoff,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        }
        answer=ExternalText.CustomerAnswer(answer);
        var missingNames=sources.Where(s=>s.Kind=="parameter").Select(s=>ExternalText.CustomerAnswer(s.Title.Split('·')[0].Trim())).Where(name=>name.Length>0&&!answer.Contains(name,StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        if(includeHandoff&&hasParameter&&sufficient&&missingNames.Length>0)
            answer+=$"\n\nEncontrei o parâmetro {string.Join(", ",missingNames)} relacionado a essa possibilidade. Peça ao suporte da EvolutTech para confirmar se a opção está liberada no seu ambiente e fazer o ajuste necessário.";
        else if(includeHandoff&&hasParameter&&sufficient&&!KnowledgeSearch.Normalize(answer).Contains("suporte"))
            answer+="\n\nEsse ajuste é realizado pelo suporte da EvolutTech. Solicite a avaliação e a configuração necessária para o seu ambiente.";
        return answer;
    }
}
