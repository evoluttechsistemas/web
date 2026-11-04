using System.Security.Cryptography;
using System.Text;
using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;

internal static class ProductAndVideoTests
{
    private static void Check(bool value) {if(!value) throw new Exception("Product/video assertion failed");}
    public static async Task Product()
    {
        const string question="Consigo colocar o código de balança no cadastro de produto?";
        var retrieval=KnowledgeSearch.RetrievalQuestion(new(question,SimulationMode.CustomerSupport,History:[new("Onde fechar o caixa no PDV?","F11")]));
        Check(!retrieval.Contains("PDV")&&ApplicationSearchIntent.PreferredForm(question)=="CadProduto");
        var followUp=KnowledgeSearch.RetrievalQuestion(new("Não entendi",SimulationMode.CustomerSupport,History:[new("Onde fechar caixa no PDV?","F11"),new(question,"Resposta",ExplanationFormat.Text,question)]));
        Check(!followUp.Contains("PDV")&&followUp.Contains("balança"));
        Check(DesignerScreenService.SelectTarget(question)?.Control=="txbCodigoBal");
        Check(DesignerScreenService.SelectTarget("Onde fechar caixa no PDV?")?.Form=="FuncoesPDV");
        var root=Path.Combine(AppContext.BaseDirectory,"product-fixtures",Guid.NewGuid().ToString("N"));var scope=Path.Combine(root,"Principal");Directory.CreateDirectory(scope);
        var path=Path.Combine(scope,"CadProduto.cs");
        await File.WriteAllTextAsync(path,"""
            class CadProduto { void CadProduto_Load() {
                var manual=pd.buscaParametroDinamico("ALTERA_CODIGO_BALANCA_MANUAL",empresa,"PRODUTO","Permite informar código de balança manualmente no cadastro do produto","N");
                if(manual=="S") txbCodigoBal.Enabled=true;
            } void BotaoConsumer_Click() {} }
            """);
        var designer=Path.Combine(scope,"CadProduto.Designer.cs");
        await File.WriteAllTextAsync(designer,"class CadProduto {void InitializeComponent() {this.label.Text=\"Código Balança\";this.group.Text=\"Dados de Balança\";} }");
        var files=new List<FileInfoResult>();foreach(var p in new[]{path,designer}) files.Add(new(p,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(p))),scope));
        var result=new AnalysisResult(root,DateTimeOffset.UtcNow,[],[],files,[],[]);
        var evidence=await KnowledgeRetriever.ReadCodeAsync(result,question,"fixture",default,"Principal");
        Check(evidence.Any(e=>e.Kind=="parameter"&&e.Title.Contains("ALTERA_CODIGO_BALANCA_MANUAL")));
        Check(evidence.Any(e=>e.Kind=="interface"&&e.Text.Contains("Código Balança"))&&evidence.All(e=>e.Module!="Consumer"));
        var answer=CustomerParameterPolicy.Apply("Acesse o cadastro do produto. O parâmetro ALTERA_CODIGO_BALANCA_MANUAL deve ser avaliado pelo suporte.",evidence,true);
        Check(answer.StartsWith("Acesse o cadastro")&&answer.Contains("ALTERA_CODIGO_BALANCA_MANUAL"));
        var referral=CustomerParameterPolicy.Apply("Na aba Gerais há o campo Código Balança. Verifique se esse parâmetro está ativo.",evidence,true);
        Check(referral.Contains("Código Balança")&&referral.Contains("ALTERA_CODIGO_BALANCA_MANUAL")&&referral.Contains("suporte")&&!referral.Contains("Verifique se esse parâmetro"));
    }
    public static Task ResourcesAndVideos()
    {
        var layout=DesignerLayout.Read("""
            class FuncoesPDV { void InitializeComponent() {this.input=new Principal.txtInteiro();this.label=new System.Windows.Forms.Label();this.label.Text=resources.GetString("label.Text");this.panel=new Panel();this.Controls.Add(this.panel);this.panel.Controls.Add(this.input);this.panel.Location=new System.Drawing.Point(0,77);this.input.Location=new System.Drawing.Point(92,18);this.input.Size=new System.Drawing.Size(77,39);} }
            ""","class FuncoesPDV {void FuncoesPDV_Load(){panel.Visible=false;}}",new Dictionary<string,string>{{"label.Text","8 - Fechar Caixa"}});
        Check(layout.ToString().Contains("8 - Fechar Caixa")&&DesignerLayout.Bounds(layout,"input")== (92,95,77,39));
        const string channel="UCJ95JKodmws25c07LItAN2g";
        var xml=$"<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:yt=\"http://www.youtube.com/xml/schemas/2015\"><yt:channelId>{channel}</yt:channelId><entry><yt:videoId>IvkoDG5iThQ</yt:videoId><title>Fechamento de Caixa no PDV</title><link href=\"https://evil.invalid\"/></entry></feed>";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(xml));var videos=VideoCatalogService.Parse(input,channel);
        Check(videos.Single().Url=="https://www.youtube.com/watch?v=IvkoDG5iThQ");
        Check(VideoCatalogService.Rank(videos,"Onde fechar caixa no PDV?").Count==1);
        Check(VideoCatalogService.Rank(videos,"Quantidade no Consumer").Count==0);
        Check(VideoCatalogService.Rank(videos,"Código de balança no produto").Count==0);
        using var wrong=new MemoryStream(Encoding.UTF8.GetBytes(xml));
        try {VideoCatalogService.Parse(wrong,"UCwrong");throw new Exception("Wrong channel accepted");} catch(InvalidDataException) { }
        return Task.CompletedTask;
    }
}
