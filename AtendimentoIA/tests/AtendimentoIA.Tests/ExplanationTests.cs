using System.Text;
using System.Xml.Linq;
using EvolutTech.IA.Painel.Services;

internal static class ExplanationTests
{
    private static void Check(bool value) { if(!value) throw new InvalidOperationException("Explanation assertion failed"); }
    public static Task Progression()
    {
        var first=new SimulationRequest("Como informar quantidade no PDV?",SimulationMode.CustomerSupport);
        Check(ExplanationPolicy.Format(first)==ExplanationFormat.Text);
        var flow=first with {Question="Não entendi",History=[new(first.Question,"Texto inicial.",ExplanationFormat.Text,first.Question)]};
        Check(ExplanationPolicy.Format(flow)==ExplanationFormat.Flow);
        var image=flow with {Question="Ainda não entendi",History=[..flow.History!,new(flow.Question,"Passos",ExplanationFormat.Flow,ExplanationPolicy.Topic(flow))]};
        Check(ExplanationPolicy.Format(image)==ExplanationFormat.Image);
        Check(ExplanationPolicy.Topic(image)==first.Question&&KnowledgeSearch.ModuleOf(KnowledgeSearch.RetrievalQuestion(image))=="PDV");
        var repeated=image with {History=[new("Não entendi","Ilustração",ExplanationFormat.Image,first.Question)]};
        Check(ExplanationPolicy.Format(repeated)==ExplanationFormat.Image&&ExplanationPolicy.Topic(repeated)==first.Question);
        var newTopic=repeated with {Question="Como cadastrar um produto no Consumer?"};
        Check(ExplanationPolicy.Format(newTopic)==ExplanationFormat.Text&&ExplanationPolicy.Topic(newTopic)==newTopic.Question);
        Check(ExplanationPolicy.Format(first with {Question="Não entendi"})==ExplanationFormat.Text);
        return Task.CompletedTask;
    }
    public static Task EvidenceAndImage()
    {
        var source=new KnowledgeEvidence("S1","code","PDV","fixture","Procedimento",true,Module:"PDV");
        var context=new RetrievalContext("","",[source],[]);
        var generated=new GeneratedAnswer("Siga os passos.","",true,["S1"],"",Steps:["Digite a quantidade.","Pressione *.","Passe o produto."]);
        var illustrated=SimulationService.Validate(Guid.NewGuid(),generated,context,SimulationMode.CustomerSupport,"fixture","PDV",ExplanationFormat.Image);
        Check(illustrated.Format==ExplanationFormat.Image&&illustrated.Steps?.Length==3);
        var ungrounded=SimulationService.Validate(Guid.NewGuid(),generated with {SourceIds=["inexistente"]},context,SimulationMode.CustomerSupport,"fixture","PDV",ExplanationFormat.Image);
        Check(ungrounded.Format==ExplanationFormat.Text&&ungrounded.Steps?.Length==0);
        var unsupported=SimulationService.Validate(Guid.NewGuid(),generated with {SufficientEvidence=false},context,SimulationMode.CustomerSupport,"fixture","PDV",ExplanationFormat.Flow);
        Check(unsupported.Format==ExplanationFormat.Text&&unsupported.Steps?.Length==0);
        const string png="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4rEAAAAASUVORK5CYII=";
        var reference=new ScreenReference(Guid.NewGuid(),"PDV","quantidade","26.10.06","<script>alert('x')</script> & quantidade",png,5,10,40,8);
        var uri=ScreenIllustration.Render(reference);
        var xml=XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(uri.Split(',')[1])));
        Check(!xml.Descendants().Any(e=>e.Name.LocalName=="script"));
        Check(xml.Descendants().Any(e=>e.Name.LocalName=="text"&&e.Value.Contains("<script>")));
        Check(xml.Descendants().Single(e=>e.Name.LocalName=="image").Attribute("href")?.Value==png);
        Check(xml.Descendants().Any(e=>e.Name.LocalName=="rect"&&e.Attribute("stroke") is not null));
        Check(ScreenIllustration.Find([reference],"Quantidade no PDV","26.10.06")==reference);
        Check(ScreenIllustration.Find([reference],"Quantidade no Consumer","26.10.06") is null);
        Check(ScreenIllustration.Find([reference],"Cancelar venda no PDV","26.10.06") is null);
        Check(ScreenIllustration.Find([reference],"Quantidade no PDV","26.09.01") is null);
        Check(ScreenIllustration.Find([],"Quantidade no PDV","26.10.06") is null);
        try { ScreenIllustration.Render(reference with {X=90,Width=40});throw new InvalidOperationException("Out-of-bounds area accepted"); } catch(ArgumentException) { }
        try { ScreenIllustration.Render(reference with {ImageDataUri="data:image/svg+xml;base64,PHN2Zy8+"});throw new InvalidOperationException("Untrusted image type accepted"); } catch(ArgumentException) { }
        var clean=ExternalText.CustomerAnswer("No campo txbProduto, o método insereQuantidade() altera txbQuantidade; use calculaQuantidade(). Parâmetro OPCAO_PDV.");
        Check(!clean.Contains("txbProduto")&&!clean.Contains("txbQuantidade")&&!clean.Contains("insereQuantidade")&&!clean.Contains("calculaQuantidade")&&clean.Contains("OPCAO_PDV"));
        return Task.CompletedTask;
    }
}
