using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class KnowledgeSearch
{
    private static readonly HashSet<string> StopWords = new("como onde devo para pelo pela pode posso quero existe tenho tem uma um que qual quais sistema cliente favor fazer sobre gostaria consigo funcionalidade isso essa esse estas estes ele ela disso dessa desse informar informe digitar digite passar passe antes depois durante maior maiores mesmo mesma preciso nao estou esta estao estava quando porque por que aparece apareceu aparecendo deu dando agora ainda tambem entao aqui ali vez sempre nunca todo toda todos todas cada outro outra muito muita bom boa tarde noite obrigado obrigada ola pessoal ajuda ajudar alguem certo voces voce senhor senhora gente pra pro favor seria queria tentei tentando fica ficou faco faz fazendo vou vai quer querem saber sabe com dos das nos nas ate mas foi ser sao pois tipo coisa".Split(' '));
    public static string RetrievalQuestion(SimulationRequest request)
    {
        if(ExplanationPolicy.IsClarification(request.Question)) return ExplanationPolicy.Topic(request);
        var contextual=ExplanationPolicy.IsClarification(request.Question)||Regex.IsMatch(Normalize(request.Question),@"\b(isso|isto|esse|essa|esses|essas|ele|ela|aquele|aquela)\b|^(?:e\s+)?qual\s+(tecla|atalho|campo|aba)[?!. ]*$");
        if(!contextual) return request.Question+FromImage(request);
        var topic=ExplanationPolicy.Topic(request);var last=request.History?.LastOrDefault();
        var previous=last is null?"":string.IsNullOrWhiteSpace(last.Topic)?last.Question:last.Topic;
        if(ModuleOf(request.Question).Length>0&&ModuleOf(previous).Length>0&&ModuleOf(request.Question)!=ModuleOf(previous)) previous="";
        return topic+" "+request.Question+" "+previous+FromImage(request);
    }
    // O texto lido no print do cliente (título da tela, mensagem de erro) entra na busca junto com a pergunta.
    private static string FromImage(SimulationRequest request)=>(string.IsNullOrWhiteSpace(request.ImageSearchText)?"":" "+request.ImageSearchText)+
        (string.IsNullOrWhiteSpace(request.SearchHint)?"":" "+request.SearchHint);
    public static string Normalize(string text)
    {
        text = Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = string.Concat(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
        // "NFC-e", "NF-e", "MDF-e" viram uma palavra só, igual na pergunta e nas fontes
        // (antes a pergunta virava "nfce" e a documentação continuava "nfc-e": não batia).
        return FiscalHyphen.Replace(plain, "$1e");
    }
    private static readonly Regex FiscalHyphen = new(@"(?<![a-z0-9])(nfc|nfs|nf|ct|mdf|cf)\s?-\s?e(?![a-z0-9])", RegexOptions.Compiled);

    /// <summary>
    /// Documento fiscal de que o texto fala: "nfce", "nfe", "ambos" ou "" (nenhum).
    /// NFC-e e NF-e têm telas e regras diferentes: uma fonte de uma não responde pergunta da outra.
    /// </summary>
    public static string FiscalDocument(string text)
    {
        var t = Normalize(text ?? "");
        var nfce = Regex.IsMatch(t, @"(?<![a-z0-9])nfce(?![a-z0-9])|cupom fiscal|nota (fiscal )?de consumidor|nota do consumidor");
        var nfe = Regex.IsMatch(t, @"(?<![a-z0-9])(nfe|danfe)(?![a-z0-9])|nota fiscal eletronica");
        return nfce && nfe ? "ambos" : nfce ? "nfce" : nfe ? "nfe" : "";
    }

    /// <summary>Ajuste da nota pela correspondência do documento fiscal (pergunta de NFC-e x fonte de NF-e).</summary>
    public static int FiscalAdjust(string askedDocument, KnowledgeEvidence evidence)
    {
        if (askedDocument is not ("nfce" or "nfe")) return 0;
        var inTitle = FiscalDocument(evidence.Title);
        if (inTitle == askedDocument) return 6;
        if (inTitle.Length > 0 && inTitle != "ambos") return -1000; // título só do outro documento: fora
        var inText = FiscalDocument(evidence.Text);
        if (inText == askedDocument || inText == "ambos") return 2;
        return inText.Length > 0 ? -8 : 0;                           // conteúdo só do outro documento
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
    /// <summary>
    /// Grupos usados nas consultas SQL e na nota das fontes de texto: cada termo da pergunta com raiz e sinônimos,
    /// mais os sinônimos de expressões ("nota fiscal", "fechar caixa"). Máximo de 8 grupos, os mais específicos primeiro.
    /// </summary>
    public static List<string[]> SearchGroups(string question, IReadOnlyList<string[]>? vocabulary = null)
    {
        vocabulary ??= ErpVocabulary.Parse(ErpVocabulary.DefaultGroups);
        var groups = new List<string[]>();
        foreach (var phrase in ErpVocabulary.PhrasesIn(question, vocabulary))
            groups.Add(vocabulary.First(g => g.Contains(phrase)).Prepend(phrase).Distinct().Take(5).ToArray());
        var covered = groups.SelectMany(g => g).SelectMany(t => t.Split(' ')).ToHashSet();
        // Termos mais longos costumam ser mais específicos ("rejeicao" diz mais que "nota").
        var terms = Terms(question).Where(t => !covered.Contains(t))
            .Select((t, i) => (t, i)).OrderByDescending(x => x.t.Length >= 6 ? 1 : 0).ThenBy(x => x.i).Select(x => x.t).ToList();
        groups.AddRange(ErpVocabulary.Expand(terms, vocabulary));
        return groups.DistinctBy(g => string.Join("|", g)).Take(8).ToList();
    }
    /// <summary>Nota por grupo: título vale 5, conteúdo 1; bônus quando todos os grupos aparecem.</summary>
    public static int ScoreGroups(string title, string content, IReadOnlyList<string[]> groups)
    {
        title = Normalize(title); content = Normalize(content);
        var score = 0; var matched = 0;
        foreach (var group in groups)
        {
            var inTitle = group.Any(t => title.Contains(t, StringComparison.Ordinal));
            var inContent = group.Any(t => content.Contains(t, StringComparison.Ordinal));
            score += (inTitle ? 5 : 0) + (inContent ? 1 : 0);
            if (inTitle || inContent) matched++;
        }
        return score + (groups.Count > 1 && matched == groups.Count ? 3 : 0);
    }
    public static int Score(string title, string content, IReadOnlyList<string> terms)
    {
        title = Normalize(title); content = Normalize(content);
        return terms.Sum(term => (title.Contains(term, StringComparison.Ordinal) ? 5 : 0) + (content.Contains(term, StringComparison.Ordinal) ? 1 : 0));
    }
    private static readonly HashSet<string> TextKinds = ["documentation","resolved-case","guidance","parameter"];
    public static List<KnowledgeEvidence> Rank(IEnumerable<KnowledgeEvidence> evidence, string question, SimulationMode mode,
        IReadOnlyList<string[]>? vocabulary = null)
    {
        var requestedModule=ModuleOf(question);
        var terms=FocusTerms(question);
        // Fontes de texto (documentação, casos, orientações, parâmetros) também contam raiz e sinônimos:
        // a consulta SQL já as trouxe por eles, então a nota precisa enxergá-los.
        var groups=ErpVocabulary.Expand(terms,vocabulary??ErpVocabulary.Parse(ErpVocabulary.DefaultGroups));
        var navigation=ApplicationInspectionService.NeedsNavigation(question);
        var askedDocument=FiscalDocument(question);
        var eligible=evidence.Where(e => (mode == SimulationMode.Technician || e.CustomerSafe) && MatchesModule(e,requestedModule)).ToList();
        if(ApplicationSearchIntent.IsConfiguration(question)&&eligible.Any(e=>e.Parameter is not null&&Terms(e.Parameter.Group).Any(terms.Contains)))
            eligible=eligible.Where(e=>e.Kind!="parameter"||e.Parameter is not null&&Terms(e.Parameter.Group).Any(terms.Contains)).ToList();
        var ranked=eligible
            .Select(e=>e with { Module=ModuleOf(e) })
            .Select(e => (Evidence: e, Score: TextKinds.Contains(e.Kind)?ScoreGroups(e.Title, e.Text, groups):Score(e.Title, e.Text, terms), Fiscal: FiscalAdjust(askedDocument, e)))
            .Where(e => e.Score > 0 && e.Fiscal > -1000).OrderByDescending(e => e.Fiscal + ApplicationSearchIntent.FormBoost(System.Text.RegularExpressions.Regex.Replace(e.Evidence.Locator,@":\d+$",""),question)+(e.Evidence.Kind=="interface"?(navigation?30:8):0)+(e.Evidence.Kind=="code"&&e.Evidence.Text.Contains("Keys.",StringComparison.Ordinal)?4:0)+e.Score +
                (!string.IsNullOrEmpty(requestedModule)&&e.Evidence.Module==requestedModule?10:0))
            .DistinctBy(e=>e.Evidence.Kind=="parameter"?"parameter|"+Normalize(e.Evidence.Title):e.Evidence.Kind+"|"+e.Evidence.Locator+"|"+e.Evidence.Title).ToList();
        // Vagas por tipo: orientações validadas, documentação e casos aprovados não podem ser empurrados
        // para fora pelo código. Interface e código continuam garantidos para caminho, atalho e regra.
        var selected=ranked.Where(e=>e.Evidence.Kind=="guidance").Take(2)
            .Concat(ranked.Where(e=>e.Evidence.Kind=="documentation").Take(3))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="resolved-case").Take(2))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="interface").Take(3))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="code").Take(3))
            .Concat(ranked.Where(e=>e.Evidence.Kind=="parameter").Take(ApplicationSearchIntent.IsConfiguration(question)?4:2))
            .Concat(ranked).DistinctBy(e=>(e.Evidence.Kind,e.Evidence.Locator,e.Evidence.Title));
        return selected.Take(14)
            .Select((entry, index) => entry.Evidence with { Id = $"S{index + 1}", Text = ExternalText.Clean(entry.Evidence.Text, 2800), Title = ExternalText.Clean(entry.Evidence.Title, 200) }).ToList();
    }
}
