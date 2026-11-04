using System.Security.Cryptography;
using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;

internal static class ModuleTests
{
    private static void Check(bool condition) { if(!condition) throw new InvalidOperationException("Module assertion failed"); }
    public static Task Separation()
    {
        Check(KnowledgeSearch.ParameterModule("SOLICITA_QUANTIDADE","PDV")=="PDV");
        Check(KnowledgeSearch.ParameterModule("DIGITA_QUANTIDADE_BOTAO_CONSUMER","PDV")=="Ambiguous");
        Check(!KnowledgeSearch.MatchesModule(new("S0","parameter","Conflito","fixture","",true,Module:"Ambiguous"),"PDV"));
        var consumer=new KnowledgeEvidence("S1","parameter","DIGITA_QUANTIDADE_BOTAO_CONSUMER · PDV","Parametro/1","Solicita quantidade do produto",true,Module:"Consumer");
        var pdv=new KnowledgeEvidence("S2","code","PDV.Entrada","PDV.cs:12","Quantidade do produto no teclado",true,Module:"PDV");
        var sources=KnowledgeSearch.Rank([consumer,pdv],"Como informar quantidade do produto no PDV?",SimulationMode.CustomerSupport);
        Check(sources.Count==1&&sources[0].Module=="PDV");
        var context=new RetrievalContext("","",[consumer],[]);
        var invalid=SimulationService.Validate(Guid.NewGuid(),new("Use o Consumer no PDV","",true,["S1"],""),context,SimulationMode.CustomerSupport,"fixture","PDV");
        Check(!invalid.SufficientEvidence&&!invalid.Answer.Contains("Use o Consumer"));
        var unknown=context with { Sources=[consumer with {Title="Quantidade",Module="",Locator="global"}] };
        var unsupported=SimulationService.Validate(Guid.NewGuid(),new("Há esse procedimento no PDV","",true,["S1"],""),unknown,SimulationMode.CustomerSupport,"fixture","PDV");
        Check(!unsupported.SufficientEvidence&&!unsupported.Answer.Contains("Há esse procedimento"));
        var question=KnowledgeSearch.RetrievalQuestion(new("Agora quero quantidade no PDV",SimulationMode.CustomerSupport,
            History:[new("Como funciona o Consumer?","Resposta anterior")]));
        Check(KnowledgeSearch.ModuleOf(question)=="PDV"&&!question.Contains("Consumer"));
        return Task.CompletedTask;
    }
    public static async Task MethodBodies()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"module-fixtures",Guid.NewGuid().ToString("N"));
        var pdvPath=Path.Combine(root,"Principal","Aplicacoes","PDV.cs");
        var consumerPath=Path.Combine(root,"Consumer","Consumer.cs");
        var backupPath=Path.Combine(root,"Principal - antes de alterar","PDV.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(pdvPath)!);Directory.CreateDirectory(Path.GetDirectoryName(consumerPath)!);Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await File.WriteAllTextAsync(pdvPath,"""
            class PDV {
                void ConfiguraJanela() { var cor = 1; }
                void InsereQuantidade() { txbQuantidade.Text = decimal.Parse(txbProduto.Text).ToString(); }
                void CalculaQuantidade() { InsereQuantidade(); txbProduto.Text = ""; txbProduto.Focus(); }
                void OnInput() { if (e.KeyCode == Keys.Multiply) CalculaQuantidade(); }
            }
            """);
        await File.WriteAllTextAsync(consumerPath,"class Consumer { void QuantidadeProduto() { SolicitaQuantidadeBotaoConsumer(); } }");
        await File.WriteAllTextAsync(backupPath,"class PDV { void InsereQuantidadeProduto() { ProcedimentoAntigo(); } }");
        var files=new List<FileInfoResult>();var classes=new List<ClassInfoResult>();
        foreach(var path in new[] { pdvPath,consumerPath,backupPath })
        {
            files.Add(new(path,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),
                path==pdvPath?Path.Combine(root,"Principal"):path==consumerPath?Path.Combine(root,"Consumer"):Path.Combine(root,"Principal - antes de alterar")));
            classes.Add(new(Path.GetFileNameWithoutExtension(path),root,[new(path,1)],[],[],null));
        }
        var result=new AnalysisResult(root,DateTimeOffset.UtcNow,[],[],files,classes,[]);
        var found=await KnowledgeRetriever.ReadCodeAsync(result,"Como informar quantidade de produto no PDV?","fixture-commit",default);
        Check(found.Any(e=>e.Text.Contains("Keys.Multiply"))&&found.Any(e=>e.Text.Contains("txbQuantidade.Text")));
        Check(found.All(e=>e.Module=="PDV"&&!e.Locator.Contains("Consumer")&&!e.Locator.Contains("antes de")));
        Check(found.All(e=>e.Version=="fixture-commit"));
        var selected=await KnowledgeRetriever.ReadCodeAsync(result,"Como informar quantidade de produto no PDV?","fixture-commit",default,"Principal");
        Check(selected.Count>0&&selected.All(e=>e.Locator.Contains("Principal")&&!e.Locator.Contains("antes de")));
        Check((await KnowledgeRetriever.ReadCodeAsync(result,"Como informar quantidade de produto no PDV?","fixture-commit",default,"ProjetoInexistente")).Count==0);
    }
    public static Task ParameterHandoff()
    {
        var source=new KnowledgeEvidence("S1","parameter","OPCAO_PDV_INTERNA · PDV","Parametro/1","Definição da opção.",true,Module:"PDV");
        var context=new RetrievalContext("","",[source],[]);
        var generated=new GeneratedAnswer("Habilite o parâmetro OPCAO_PDV_INTERNA.","Nota interna.",true,["S1"],"Você consultou OPCAO_PDV_INTERNA?");
        foreach(var mode in new[] {SimulationMode.CustomerSupport,SimulationMode.Commercial})
        {
            var answer=SimulationService.Validate(Guid.NewGuid(),generated,context,mode,"fixture","PDV");
            if(!answer.Answer.Contains("OPCAO_PDV_INTERNA")||!answer.FollowUp.Contains("OPCAO_PDV_INTERNA")||answer.Answer.Contains("Habilite")||!answer.Answer.Contains("suporte"))
                throw new InvalidOperationException("Customer parameter policy was not applied");
        }
        var technical=SimulationService.Validate(Guid.NewGuid(),generated,context,SimulationMode.Technician,"fixture","PDV");
        if(!technical.Answer.Contains("OPCAO_PDV_INTERNA")) throw new InvalidOperationException("Internal reference was lost");
        var informative=CustomerParameterPolicy.Apply("Há uma configuração relacionada a essa opção.",[source],true);
        if(!informative.Contains("suporte")||!informative.Contains("OPCAO_PDV_INTERNA")) throw new InvalidOperationException("Missing parameter name or support handoff");
        return Task.CompletedTask;
    }
}
