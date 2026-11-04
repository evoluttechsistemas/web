using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;

internal static class InterfaceTests
{
    private static void Check(bool value) {if(!value) throw new Exception("Interface assertion failed");}
    public static async Task MenusAndResources()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"interface-fixtures",Guid.NewGuid().ToString("N"),"source");
        var scope=Path.Combine(root,"Principal");Directory.CreateDirectory(scope);
        var path=Path.Combine(scope,"PDV.Designer.cs");
        await File.WriteAllTextAsync(path,"""
            class PDV {void InitializeComponent() {
                this.caixa.Text="Caixa";this.fechar.Text="Fechar Caixa";
                this.caixa.DropDownItems.AddRange(new ToolStripItem[] {this.fechar});
                this.fechar.ShortcutKeys = ((Keys)(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.C));
                this.fechar.Click += new EventHandler(this.FecharCaixa);
                this.acesso.Text="Acesso";this.funcoes.Text="Funções";
                this.acesso.DropDownItems.Add(this.funcoes);this.funcoes.ShortcutKeys=System.Windows.Forms.Keys.F11;
                this.toolStripStatusLabel1.Text="[F11 = Funções]";
            }}
            """);
        var functions=Path.Combine(scope,"FuncoesPDV.Designer.cs");
        await File.WriteAllTextAsync(functions,"""
            class FuncoesPDV {void InitializeComponent() {this.label1.Text=resources.GetString("label1.Text");this.label2.Text="Digite o número + ENTER";}}
            """);
        using(var zip=ZipFile.Open(Path.Combine(Path.GetDirectoryName(root)!,"source.zip"),ZipArchiveMode.Create))
        {
            using var writer=new StreamWriter(zip.CreateEntry("Principal/FuncoesPDV.resx").Open());
            await writer.WriteAsync("<root><data name=\"label1.Text\"><value>8 - Fechar Caixa</value></data><data name=\"serialized\" mimetype=\"application/x-microsoft.net.object.binary.base64\"><value>DO_NOT_DESERIALIZE</value></data></root>");
        }
        var files=new List<FileInfoResult>();foreach(var p in new[]{path,functions}) files.Add(new(p,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(p))),scope));
        var result=new AnalysisResult(root,DateTimeOffset.UtcNow,[],[],files,[],[]);
        var sources=await InterfaceEvidenceSearch.ReadAsync(result,"Onde fechar caixa no PDV?","fixture",default,"Principal");
        Check(sources.Count==2&&sources.All(s=>s.Kind=="interface"&&s.Module=="PDV"));
        var text=string.Join(" ",sources.Select(s=>s.Text));
        Check(text.Contains("Caixa → Fechar Caixa")&&text.Contains("Ctrl + Shift + C")&&text.Contains("F11")&&text.Contains("8 - Fechar Caixa")&&text.Contains("ENTER")&&!text.Contains("DO_NOT_DESERIALIZE"));
        Check(sources.Any(s=>s.Actions?.Any(a=>a.Label=="Funções"&&a.Shortcut=="F11")==true));
        var actionSource=sources.First(s=>s.Actions?.Any(a=>a.Label=="Funções")==true) with {Id="S2",Actions=[new("Funções","Acesso → Funções","F9")]};
        var initial=new SimulationAnswer(Guid.NewGuid(),"Abra Funções e selecione a operação.","",true,"",[],[],"","","fixture",0,0);
        var completed=NavigationPresentation.Complete(initial,[actionSource],"PDV");
        Check(completed.Answer.Contains("F9")&&!completed.Answer.Contains("F11")&&completed.Sources.Count==1);
        Check(NavigationPresentation.Complete(completed,[actionSource],"PDV").Answer==completed.Answer);
        Check((await InterfaceEvidenceSearch.ReadAsync(result,"Onde fechar caixa no PDV?","fixture",default,"OutroProjeto")).Count==0);
        Check((await InterfaceEvidenceSearch.ReadAsync(result,"Onde fechar caixa no Consumer?","fixture",default,"Principal")).Count==0);
        await File.AppendAllTextAsync(path,"// changed");
        Check((await InterfaceEvidenceSearch.ReadAsync(result,"Onde fechar caixa no PDV?","fixture",default,"Principal")).Count==1);
        var code=new KnowledgeEvidence("S1","code","PDV.FecharCaixa","fixture","Rotina de fechar caixa",true,Module:"PDV");
        var context=new RetrievalContext("","",[code],[]);
        var unsupported=SimulationService.Validate(Guid.NewGuid(),new("Entre pelo menu suposto","",true,["S1"],""),context,SimulationMode.CustomerSupport,"fixture","PDV",requireInterfaceEvidence:true);
        Check(!unsupported.SufficientEvidence&&!unsupported.Answer.Contains("menu suposto"));
    }
}
