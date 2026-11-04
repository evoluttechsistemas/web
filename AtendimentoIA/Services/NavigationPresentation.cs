using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class NavigationPresentation
{
    // Complete mentioned actions with their actual Designer shortcuts; never infer a key from a method name.
    public static SimulationAnswer Complete(SimulationAnswer answer,IReadOnlyList<KnowledgeEvidence> available,string module,string question="")
    {
        if(!answer.SufficientEvidence) return answer;
        string Keys(string text)=>Regex.Replace(KnowledgeSearch.Normalize(text).Replace("control","ctrl",StringComparison.Ordinal),@"[^a-z0-9]","");
        var sources=answer.Sources.ToList();var text=answer.Answer;
        foreach(var source in available.Where(s=>s.Kind=="interface"&&s.CustomerSafe&&KnowledgeSearch.MatchesModule(s,module)))
            foreach(var action in source.Actions??[])
            {
                if(action.Label.Any(c=>c>127))
                {
                    var folded=string.Concat(action.Label.Normalize(System.Text.NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
                    var pattern=string.Concat(folded.Select(c=>c switch {'a'=>"[aáàâãä]",'e'=>"[eéèêë]",'i'=>"[iíìîï]",'o'=>"[oóòôõö]",'u'=>"[uúùûü]",'c'=>"[cç]",' '=>@"\s+",_=>Regex.Escape(c.ToString())}));
                    text=Regex.Replace(text,$@"(?<!\w){pattern}(?!\w)",_=>action.Label,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
                }
                var label=Regex.Replace(action.Label,@"^\s*\d+\s*[-.)]\s*","");
                if(label.Length==0||!Regex.IsMatch(KnowledgeSearch.Normalize(text),$@"(?<!\w){Regex.Escape(KnowledgeSearch.Normalize(label))}(?!\w)")) continue;
                var changed=false;
                var routeRelevant=question.Length==0||KnowledgeSearch.Score(action.Label,"",KnowledgeSearch.FocusTerms(question))>0||KnowledgeSearch.Normalize(label)=="funcoes";
                if(routeRelevant&&action.Path.Contains(" → ",StringComparison.Ordinal)&&!Keys(text).Contains(Keys(action.Path),StringComparison.Ordinal))
                {text+=$"\n\nCaminho no menu: {ExternalText.CustomerAnswer(action.Path)}.";changed=true;}
                if(action.Shortcut.Length>0&&!Keys(text).Contains(Keys(action.Shortcut),StringComparison.Ordinal))
                {text+=$"\n\nPara acessar {ExternalText.CustomerAnswer(action.Label)}, use {ExternalText.CustomerAnswer(action.Shortcut)}.";changed=true;}
                if(changed&&!sources.Any(s=>s.Id==source.Id)) sources.Add(source);
            }
        return answer with {Answer=text,Sources=sources};
    }
}
