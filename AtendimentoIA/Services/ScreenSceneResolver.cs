namespace EvolutTech.IA.Painel.Services;

public sealed record ScreenScene(string Form,string Locator,string Caption,string Module);
public static class ScreenSceneResolver
{
    public static ScreenScene? Resolve(string question,IReadOnlyList<KnowledgeEvidence> evidence)
    {
        var terms=KnowledgeSearch.FocusTerms(question);var module=KnowledgeSearch.ModuleOf(question);
        var interfaces=evidence.Where(e=>e.Kind=="interface"&&e.CustomerSafe&&KnowledgeSearch.MatchesModule(e,module)).ToList();
        var actions=interfaces.SelectMany(source=>(source.Actions??[]).Where(a=>a.TargetForm.Length>0)
            .Select(a=>new {Action=a,Score=KnowledgeSearch.Score(a.Label,a.Path,terms)})).Where(a=>a.Score>0).OrderByDescending(a=>a.Score);
        foreach(var item in actions)
        {
            var locator=interfaces.FirstOrDefault(s=>Path.GetFileName(s.Locator).Equals(item.Action.TargetForm+".Designer.cs",StringComparison.OrdinalIgnoreCase))?.Locator??"";
            return new(item.Action.TargetForm,locator,ExternalText.CustomerAnswer(item.Action.Path),module.Length>0?module:"Aplicação");
        }
        // A single identified non-menu screen can be shown without guessing an individual field.
        var screens=interfaces.Where(e=>!Path.GetFileName(e.Locator).Equals("Menu.Designer.cs",StringComparison.OrdinalIgnoreCase))
            .Where(e=>e.Locator.EndsWith(".Designer.cs",StringComparison.OrdinalIgnoreCase)).ToList();
        if(screens.Count!=1) return null;
        return new(Path.GetFileName(screens[0].Locator).Replace(".Designer.cs","",StringComparison.OrdinalIgnoreCase),screens[0].Locator,
            "Tela da aplicação relacionada à orientação.",module.Length>0?module:"Aplicação");
    }
}
