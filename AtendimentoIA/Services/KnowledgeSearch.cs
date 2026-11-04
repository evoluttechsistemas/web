using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class KnowledgeSearch
{
    private static readonly HashSet<string> StopWords = new("como onde devo para pelo pela pode posso quero existe tenho tem uma um que qual quais sistema cliente favor fazer sobre gostaria consigo funcionalidade isso essa esse estas estes ele ela disso dessa desse informar informe digitar digite passar passe antes depois durante maior maiores mesmo mesma preciso".Split(' '));
    public static string RetrievalQuestion(SimulationRequest request)
    {
        if(ExplanationPolicy.IsClarification(request.Question)) return ExplanationPolicy.Topic(request);
        var contextual=ExplanationPolicy.IsClarification(request.Question)||Regex.IsMatch(Normalize(request.Question),@"\b(isso|isto|esse|essa|esses|essas|ele|ela|aquele|aquela)\b|^(?:e\s+)?qual\s+(tecla|atalho|campo|aba)[?!. ]*$");
        if(!contextual) return request.Question;
        var topic=ExplanationPolicy.Topic(request);var last=request.History?.LastOrDefault();
        var previous=last is null?"":string.IsNullOrWhiteSpace(last.Topic)?last.Question:last.Topic;
        if(ModuleOf(request.Question).Length>0&&ModuleOf(previous).Length>0&&ModuleOf(request.Question)!=ModuleOf(previous)) previous="";
        return topic+" "+request.Question+" "+previous;
    }
    public static string Normalize(string text)
    {
        text = Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");
        var decomposed = text.Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    }
    public static string[] Terms(string question)
    {
        var normalized=Normalize(question.Replace("NF-e","NFe",StringComparison.OrdinalIgnoreCase).Replace("NFC-e","NFCe",StringComparison.OrdinalIgnoreCase));
        var terms=Regex.Matches(normalized,@"[a-z0-9]{3,}").Select(m=>m.Value).Where(t=>!StopWords.Contains(t)&&!t.StartsWith("configur",StringComparison.Ordinal)&&!t.StartsWith("parametr",StringComparison.Ordinal)).Select(CanonicalTerm).Distinct().ToList();
        if(Regex.IsMatch(normalized,@"\b(qtd|qtde|quantidades|asterisco|multiplicar)\b|mais de uma unidade")||question.Contains('*')) terms.Insert(0,"quantidade");
        return terms.Distinct().Take(8).ToArray();
    }
    // These distinct products were confirmed by the ERP owner; segment never assigns a module.
    public static string CanonicalTerm(string term)=>term.Length>4&&(term.EndsWith("as",StringComparison.Ordinal)||term.EndsWith("os",StringComparison.Ordinal))?term[..^1]:term;
    public static string[] FocusTerms(string question)
    {
        var terms=Terms(question);var module=CanonicalTerm(Normalize(ModuleOf(question)));
        var subject=Regex.Replace(question,@"\b(?:erp|evolut\s*tech)\b","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        var focused=Terms(subject).Where(t=>t!=module).ToArray();return focused.Length>0?focused:terms;
    }
    public static string ModuleOf(string text)
    {
        var normalized=Normalize(text);
        var found=new[] { "PDV", "Consumer", "Vendas" }.Where(module=>
            Regex.IsMatch(normalized,$@"(?<![a-z0-9]){module.ToLowerInvariant()}(?![a-z0-9])")).ToArray();
        return found.Length==1?found[0]:"";
    }
    public static string ModuleOf(KnowledgeEvidence evidence)=>!string.IsNullOrWhiteSpace(evidence.Module)?evidence.Module:
        ModuleOf(evidence.Kind=="code"?$"{evidence.Locator} {evidence.Title}":evidence.Title);
    public static string ParameterModule(string name,string group)
    {
        var named=ModuleOf(name);
        var grouped=ModuleOf(group);
        return named.Length>0&&grouped.Length>0&&named!=grouped?"Ambiguous":grouped.Length>0?grouped:named;
    }
    public static bool MatchesModule(KnowledgeEvidence evidence,string requestedModule)
    {
        var sourceModule=ModuleOf(evidence);
        if(sourceModule=="Ambiguous") return false;
        return string.IsNullOrWhiteSpace(requestedModule)||string.IsNullOrWhiteSpace(sourceModule)||
            sourceModule.Equals(requestedModule,StringComparison.OrdinalIgnoreCase);
    }
    public static int Score(string title, string content, IReadOnlyList<string> terms)
    {
        title = Normalize(title); content = Normalize(content);
        return terms.Sum(term => (title.Contains(term, StringComparison.Ordinal) ? 5 : 0) + (content.Contains(term, StringComparison.Ordinal) ? 1 : 0));
    }
    public static List<KnowledgeEvidence> Rank(IEnumerable<KnowledgeEvidence> evidence, string question, SimulationMode mode)
    {
        var requestedModule=ModuleOf(question);
        var terms=FocusTerms(question);
        var eligible=evidence.Where(e => (mode == SimulationMode.Technician || e.CustomerSafe) && MatchesModule(e,requestedModule)).ToList();
        if(ApplicationSearchIntent.IsConfiguration(question)&&eligible.Any(e=>e.Parameter is not null&&Terms(e.Parameter.Group).Any(terms.Contains)))
            eligible=eligible.Where(e=>e.Kind!="parameter"||e.Parameter is not null&&Terms(e.Parameter.Group).Any(terms.Contains)).ToList();
        var ranked=eligible
            .Select(e=>e with { Module=ModuleOf(e) })
            .Select(e => (Evidence: e, Score: Score(e.Title, e.Text, terms)))
            .Where(e => e.Score > 0).OrderByDescending(e => ApplicationSearchIntent.FormBoost(System.Text.RegularExpressions.Regex.Replace(e.Evidence.Locator,@":\d+$",""),question)+(e.Evidence.Kind=="interface"?30:0)+(e.Evidence.Kind=="code"&&e.Evidence.Text.Contains("Keys.",StringComparison.Ordinal)?4:0)+e.Score +
                (!string.IsNullOrEmpty(requestedModule)&&e.Evidence.Module==requestedModule?10:0))
            .DistinctBy(e=>e.Evidence.Kind=="parameter"?"parameter|"+Normalize(e.Evidence.Title):e.Evidence.Kind+"|"+e.Evidence.Locator+"|"+e.Evidence.Title).ToList();
        var selected=ranked.Where(e=>e.Evidence.Kind=="interface").Take(3)
            .Concat(ranked.Where(e=>e.Evidence.Kind=="code").Take(4))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="parameter").Take(ApplicationSearchIntent.IsConfiguration(question)?4:2))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="documentation").Take(1))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="resolved-case").Take(1))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="guidance").Take(1))
            .Concat(ranked).DistinctBy(e=>(e.Evidence.Kind,e.Evidence.Locator,e.Evidence.Title));
        return selected.Take(12)
            .Select((entry, index) => entry.Evidence with { Id = $"S{index + 1}", Text = ExternalText.Clean(entry.Evidence.Text, 2800), Title = ExternalText.Clean(entry.Evidence.Title, 200) }).ToList();
    }
}
