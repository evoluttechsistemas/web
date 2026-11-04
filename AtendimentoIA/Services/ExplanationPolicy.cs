using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class ExplanationPolicy
{
    public static bool IsClarification(string question) => Regex.IsMatch(KnowledgeSearch.Normalize(question),
        @"\b(nao entendi|nao consegui entender|nao compreendi|nao ficou claro|nao resolveu|nao funcionou|continua o problema|continuo com duvida|ainda tenho duvida|confuso|confusa|explique melhor|explica melhor|mostre.*(?:fluxo|imagem)|mostrar.*(?:fluxo|imagem)|passo a passo)\b|^(?:nao|ainda nao)[.! ]*$");

    public static ExplanationFormat Format(SimulationRequest request)
    {
        var previous=request.History?.LastOrDefault();
        if(previous is null||!IsClarification(request.Question)) return ExplanationFormat.Text;
        return previous.Format==ExplanationFormat.Text?ExplanationFormat.Flow:ExplanationFormat.Image;
    }

    public static string Topic(SimulationRequest request)
    {
        if(!IsClarification(request.Question)) return request.Question;
        var previous=request.History?.LastOrDefault();
        return previous is null?request.Question:!string.IsNullOrWhiteSpace(previous.Topic)?previous.Topic:
            request.History!.LastOrDefault(t=>!IsClarification(t.Question))?.Question??request.Question;
    }

}
