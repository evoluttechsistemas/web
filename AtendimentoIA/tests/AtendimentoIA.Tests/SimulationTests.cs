using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class SimulationTests
{
    private static void Check(bool value) { if(!value) throw new InvalidOperationException("Simulation assertion failed"); }
    private static (PanelStore Store,SecretStore Secrets,IConfiguration Configuration) Setup()
    {
        var environment=new TestEnvironment { ContentRootPath=Path.Combine(AppContext.BaseDirectory,"simulation-fixtures",Guid.NewGuid().ToString("N")) };
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Panel:DataDirectory"]="data",["OpenAI:ApiKey"]="test-only-key-do-not-use-with-live-api" }).Build();
        var store=new PanelStore(configuration,environment);
        return (store,new SecretStore(store,new EphemeralDataProtectionProvider()),configuration);
    }
    private static KnowledgeEvidence Source(string kind="code",bool customer=true)=>new("S1",kind,"Controle de estoque","private-location.cs:20","O processo verifica o estoque antes da operação.",customer,"test-commit");
    public static async Task Provider()
    {
        var (_,secrets,configuration)=Setup();
        var handler=new ResponseHandler();
        var provider=new OpenAiAnswerProvider(new HttpClient(handler),secrets,configuration);
        var context=new RetrievalContext("Supermercado","26.10.06",[Source()],[]);
        var request=new SimulationRequest("Como verificar estoque? senha=private-marker; fixture@example.invalid",SimulationMode.CustomerSupport,778899);
        var response=await provider.GenerateAsync(request,context,new(),default);
        Check(response.Answer=="Confira o estoque disponível."&&response.InputTokens==100&&response.SourceIds.Single()=="S1");
        Check(handler.Endpoint=="https://api.openai.com/v1/responses"&&handler.HadAuthorization);
        using var body=JsonDocument.Parse(handler.Body);
        Check(!body.RootElement.GetProperty("store").GetBoolean());
        Check(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Check(!body.RootElement.TryGetProperty("tools",out _));
        Check(!handler.Body.Contains("private-marker")&&!handler.Body.Contains("fixture@example.invalid")&&!handler.Body.Contains("778899")&&!handler.Body.Contains("private-location"));
        Check(handler.Body.Contains("26.10.06")&&handler.Body.Contains("Supermercado"));
        const string png="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4rEAAAAASUVORK5CYII=";
        await provider.GenerateAsync(request,context with {InterfaceReview=new(true,true,1,"Fixture"),Images=[new("Tela reconstruída",png,"fixture")]},new(),default);
        using var illustrated=JsonDocument.Parse(handler.Body);
        var parts=illustrated.RootElement.GetProperty("input")[0].GetProperty("content");
        Check(parts[0].GetProperty("type").GetString()=="input_text"&&parts[1].GetProperty("type").GetString()=="input_image"&&parts[1].GetProperty("image_url").GetString()==png);
    }
    public static async Task Absence()
    {
        var (store,_,_)=Setup(); var retriever=new EmptyRetriever(); var provider=new CountingProvider();
        var service=new SimulationService(retriever,provider,store,NullLogger<SimulationService>.Instance);
        provider.Configured=false;
        try { await service.AskAsync(new("Consulta",SimulationMode.CustomerSupport)); throw new Exception(); } catch(SimulationException) { }
        Check(retriever.Calls==0&&provider.Calls==0);
        provider.Configured=true;
        var answer=await service.AskAsync(new("private-question-marker",SimulationMode.Commercial));
        Check(!answer.SufficientEvidence&&answer.Sources.Count==0&&provider.Calls==0);
        var audit=Directory.GetFiles(Path.Combine(store.Root,"simulation-audit"),"*.json").Single();
        Check(!File.ReadAllText(audit).Contains("private-question-marker"));
        try { await service.AskAsync(new("Pergunta",SimulationMode.Commercial,TicketId:123)); throw new Exception(); } catch(SimulationException) { }
        Check(retriever.Calls==1);
    }
    public static Task Citations()
    {
        var context=new RetrievalContext("Vestuário","26.10.06",[Source()],[]);
        var unknown=new GeneratedAnswer("Afirmação sem fonte","",true,["S999"],"");
        var rejected=SimulationService.Validate(Guid.NewGuid(),unknown,context,SimulationMode.CustomerSupport,"fixture");
        Check(!rejected.SufficientEvidence&&rejected.Sources.Count==0&&!rejected.Answer.Contains("Afirmação sem fonte"));
        var technical=new GeneratedAnswer("Consulte C:\\private\\example.cs e ParametroDinamico.","Detalhe para o técnico.",true,["S1"],"");
        var customer=SimulationService.Validate(Guid.NewGuid(),technical,context,SimulationMode.Commercial,"fixture");
        Check(customer.SufficientEvidence&&!customer.Answer.Contains("example.cs")&&!customer.Answer.Contains("ParametroDinamico"));
        var historical=context with { Sources=[Source("ai-history",false)] };
        var historyOnly=SimulationService.Validate(Guid.NewGuid(),new("O histórico diz que existe.","",true,["S1"],""),historical,SimulationMode.Technician,"fixture");
        Check(!historyOnly.SufficientEvidence&&!historyOnly.Answer.Contains("diz que existe"));
        return Task.CompletedTask;
    }
    public static Task Ranking()
    {
        var candidates=new[] { Source("guidance",false),Source("guidance",true) with { Locator="public" },Source() with { Title="Financeiro",Text="Contas a pagar" } };
        var external=KnowledgeSearch.Rank(candidates,"Como controlar estoque?",SimulationMode.Commercial);
        Check(external.Count==1&&external[0].Locator=="public"&&external[0].Id=="S1");
        Check(KnowledgeSearch.Rank(candidates,"Como controlar estoque?",SimulationMode.Technician).Count==2);
        Check(KnowledgeSearch.Terms("Como emitir NF-e?").Contains("nfe"));
        var followUp=KnowledgeSearch.RetrievalQuestion(new("E como configurar isso?",SimulationMode.CustomerSupport,History:[new("Existe controle de estoque?","Resposta anterior")]));
        Check(KnowledgeSearch.Terms(followUp).Contains("estoque")&&!followUp.Contains("Resposta anterior"));
        return Task.CompletedTask;
    }
    public static async Task CodeHash()
    {
        var (store,_,_)=Setup();
        var path=Path.Combine(store.Root,"Estoque.cs");
        const string content="class Estoque { void VerificarEstoque() { var quantidade = 1; } }";
        await File.WriteAllTextAsync(path,content);
        var bytes=await File.ReadAllBytesAsync(path);
        var result=new AnalysisResult(store.Root,DateTimeOffset.UtcNow,[],[],[new(path,Convert.ToHexString(SHA256.HashData(bytes)),store.Root)],
            [new("Estoque",store.Root,[new(path,1)],[new("VerificarEstoque","method",new(path,1))],[],null)],[]);
        Check((await KnowledgeRetriever.ReadCodeAsync(result,"verificar estoque","fixture",default)).Count>0);
        await File.WriteAllTextAsync(path,content+" // changed");
        Check((await KnowledgeRetriever.ReadCodeAsync(result,"verificar estoque","fixture",default)).Count==0);
    }
    public static Task Redaction()
    {
        var text="Senha=fixture-secret; email fixture@example.invalid; CPF 123.456.789-00; C:\\private\\file.cs; 192.168.100.10; sk-abcdefghijklmnopqrstuvwxyz; -----BEGIN PRIVATE KEY-----\nprivate-material\n-----END PRIVATE KEY-----";
        var clean=ExternalText.Clean(text);
        foreach(var marker in new[] { "fixture-secret","fixture@example.invalid","123.456.789-00","private-material","192.168.100.10","sk-abcdefghijklmnopqrstuvwxyz","C:\\private" }) Check(!clean.Contains(marker));
        Check(ExternalText.Clean("Um texto informativo.")=="Um texto informativo.");
        Check(!ExternalText.Clean("const string MinhaSenhaConexao = \"sensitive-marker\";").Contains("sensitive-marker"));
        return Task.CompletedTask;
    }
    private sealed class EmptyRetriever:IKnowledgeRetriever
    {
        public int Calls;
        public Task<RetrievalContext> RetrieveAsync(SimulationRequest request,CancellationToken token) { Calls++; return Task.FromResult(new RetrievalContext("","",[],[])); }
    }
    private sealed class CountingProvider:IAnswerProvider
    {
        public bool Configured=true; public int Calls;
        public bool IsConfigured=>Configured;
        public Task<GeneratedAnswer> GenerateAsync(SimulationRequest request,RetrievalContext context,AiSettings settings,CancellationToken token) { Calls++; throw new InvalidOperationException("Must not generate without sources"); }
    }
    private sealed class ResponseHandler:HttpMessageHandler
    {
        public string Body="",Endpoint="";public bool HadAuthorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Endpoint=request.RequestUri!.AbsoluteUri; HadAuthorization=request.Headers.Authorization?.Scheme=="Bearer";
            Body=await request.Content!.ReadAsStringAsync(token);
            var generated=JsonSerializer.Serialize(new { answer="Confira o estoque disponível.",technical_notes="Fonte verificada no teste.",sufficient_evidence=true,source_ids=new[] {"S1"},follow_up="" });
            var result=JsonSerializer.Serialize(new { status="completed",output=new[] { new { type="message",content=new[] {new { type="output_text",text=generated } } } },usage=new {input_tokens=100,output_tokens=30} });
            return new(HttpStatusCode.OK) { Content=new StringContent(result,Encoding.UTF8,"application/json") };
        }
    }
}
