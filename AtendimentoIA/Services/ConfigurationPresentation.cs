namespace EvolutTech.IA.Painel.Services;

public static class ConfigurationPresentation
{
    public static SimulationAnswer Complete(SimulationAnswer answer,IReadOnlyList<KnowledgeEvidence> available,string question)
    {
        if(!answer.SufficientEvidence||!ApplicationSearchIntent.IsConfiguration(question)) return answer;
        var terms=KnowledgeSearch.FocusTerms(question);var sources=answer.Sources.ToList();
        var relevant=available.Where(s=>s.Kind=="parameter"&&s.CustomerSafe&&s.Parameter is not null&&
            KnowledgeSearch.Terms(s.Parameter.Group).Any(terms.Contains)).DistinctBy(s=>s.Parameter!.Name+"|"+s.Parameter.Group).ToList();
        var missing=relevant.Where(s=>!answer.Answer.Contains(s.Parameter!.Name,StringComparison.OrdinalIgnoreCase)).ToList();
        var text=answer.Answer;
        if(missing.Count>0)
            text+="\n\nPeça ao suporte da EvolutTech para avaliar também estas parametrizações encontradas para essa configuração:\n"+
                string.Join("\n",missing.Select(s=>$"• {ExternalText.CustomerAnswer(s.Parameter!.Name)} (grupo {ExternalText.CustomerAnswer(s.Parameter.Group)}): {ExternalText.CustomerAnswer(s.Parameter.Description)}"))+
                "\nOs valores cadastrados no seu ambiente precisam ser confirmados pelo suporte.";
        foreach(var source in relevant) if(!sources.Any(s=>s.Id==source.Id)) sources.Add(source);
        return answer with {Answer=text,Sources=sources};
    }
}
