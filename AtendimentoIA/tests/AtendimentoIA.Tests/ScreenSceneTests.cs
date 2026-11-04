using System.Text;
using System.Xml.Linq;
using EvolutTech.IA.Painel.Services;

internal static class ScreenSceneTests
{
    public static Task Discovery()
    {
        var clarification=new SimulationRequest("Ainda não entendi.",SimulationMode.CustomerSupport,History:[new("Como configurar perdas?","Explicação anterior.",ExplanationFormat.Text,"Como configurar perdas?")]);
        if(KnowledgeSearch.RetrievalQuestion(clarification)!="Como configurar perdas?") throw new Exception("Clarification filler changed the retrieval topic");
        var menu=new KnowledgeEvidence("S1","interface","Menu","Principal/Menu.Designer.cs","",true,Actions:[new("Lançamento de Perdas","Estoque → Perdas → Lançamento de Perdas","","LancamentoPerdas"),new("Relatório de Perdas","Estoque → Perdas → Relatório de Perdas","","RelPerdas")]);
        var form=new KnowledgeEvidence("S2","interface","LancamentoPerdas","Principal/LancamentoPerdas.Designer.cs","Quantidade de perda",true);
        var scene=ScreenSceneResolver.Resolve("Como criar perdas no sistema?",[menu,form]);
        if(scene?.Form!="LancamentoPerdas"||scene.Locator!=form.Locator||!scene.Caption.Contains("Estoque → Perdas")) throw new Exception("The verified menu target was not selected for rendering");
        if(ScreenSceneResolver.Resolve("Como fechar caixa?",[menu,form with {Locator="x.Designer.cs"},form with {Locator="y.Designer.cs"}]) is not null) throw new Exception("An ambiguous screen was guessed");
        const string png="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4rEAAAAASUVORK5CYII=";
        var reference=new ScreenReference(Guid.NewGuid(),"Aplicação","perda","",scene.Caption,png,0,0,100,100,Highlight:false);
        var svg=ScreenIllustration.Render(reference);var xml=XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(svg.Split(',')[1])));
        if(xml.Descendants().Any(e=>e.Attribute("marker-end") is not null)) throw new Exception("An unverified field position was annotated");
        var clean=ExternalText.CustomerAnswer("Lan\u007fCamento de Perdas");
        if(clean.Any(c=>char.IsControl(c))) throw new Exception("Invalid control characters reached the answer");
        var answer=new SimulationAnswer(Guid.NewGuid(),clean,"",true,"",[],[],"","","fixture",0,0);
        if(!NavigationPresentation.Complete(answer,[menu],"","Como criar perdas?").Answer.Contains("Lançamento de Perdas")) throw new Exception("The verified interface label was not restored");
        return Task.CompletedTask;
    }
}
