using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public sealed class SimulationService(IKnowledgeRetriever retriever,IAnswerProvider provider,PanelStore store,
    ILogger<SimulationService> logger,DesignerScreenService? designerScreens=null,ApplicationInspectionService? applicationInspection=null,VideoCatalogService? videos=null,
    QueryRewriter? rewriter=null)
{
    public bool IsConfigured=>provider.IsConfigured;
    public async Task<AiSettings> GetSettingsAsync()=>await store.ReadAsync<AiSettings>("ai-settings.json")??new();
    public async Task SaveSettingsAsync(AiSettings settings)
    {
        if(!Regex.IsMatch(settings.Model,@"\A[a-zA-Z0-9_.:-]{1,120}\z")||!Regex.IsMatch(settings.AuxModel,@"\A[a-zA-Z0-9_.:-]{1,120}\z")) throw new ArgumentException("Nome de modelo inválido.");
        await store.WriteAsync("ai-settings.json",settings);
    }
    public async Task<SimulationAnswer> AskAsync(SimulationRequest request,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(request.Question)||request.Question.Length>2000) throw new SimulationException("Escreva uma pergunta de até 2.000 caracteres.");
        if(!Enum.IsDefined(request.Mode)||request.TicketId is <=0||request.CrmId is <=0||request.Segment.Length>100)
            throw new SimulationException("Confira o modo e os códigos informados.");
        if(request.Mode==SimulationMode.Commercial&&request.TicketId is not null||request.Mode!=SimulationMode.Commercial&&request.CrmId is not null)
            throw new SimulationException("O contexto comercial e o chamado de suporte devem permanecer separados.");
        if(SupportResolution.IsConfirmed(request)) return new(Guid.NewGuid(),"Que bom que resolveu! Se precisar de mais alguma orientação, estou à disposição.","Confirmação recebida na conversa; nenhum chamado foi encerrado automaticamente.",false,"",[],[],"","","Confirmação do cliente",0,0);
        if(!IsConfigured) throw new SimulationException("Cadastre a chave em Configurações → OpenAI. Não é o token do GitHub.");
        var id=Guid.NewGuid();
        var settings=await GetSettingsAsync();
        try
        {
            // Modelo auxiliar traduz a mensagem para os termos do ERP antes da busca (falha = segue com a pergunta original).
            if(rewriter is not null&&!ExplanationPolicy.IsClarification(request.Question))
            {
                var hint=await rewriter.RewriteAsync(request,token);
                if(hint.Length>0) request=request with {SearchHint=hint};
            }
            var context=await retriever.RetrieveAsync(request,token);
            if(applicationInspection is not null) context=await applicationInspection.InspectAsync(request,context,token);
            SimulationAnswer answer;
            if(context.Sources.Count==0)
                answer=new(id,"Não encontrei evidências suficientes na base para confirmar essa informação. Isso não significa que a funcionalidade não exista.",
                    "Nenhuma fonte relevante foi recuperada. Indexe o código, confira as fontes ou cadastre uma orientação validada.",false,
                    "Qual é a tela ou operação relacionada à sua dúvida?",[],context.Warnings,context.Segment,context.Version,"Sem chamada à IA",0,0);
            else
            {
                var generated=await provider.GenerateAsync(request,context,settings,token);
                answer=Validate(id,generated,context,request.Mode,settings.Model,KnowledgeSearch.ModuleOf(KnowledgeSearch.RetrievalQuestion(request)),ExplanationPolicy.Format(request),
                    applicationInspection is not null&&ApplicationInspectionService.NeedsNavigation(ExplanationPolicy.Topic(request)),KnowledgeSearch.RetrievalQuestion(request));
            }
            if(answer.Format==ExplanationFormat.Image&&answer.SufficientEvidence)
            {
                var screens=await store.ReadAsync<List<ScreenReference>>("screen-references.json")??[];
                var screen=ScreenIllustration.Find(screens,ExplanationPolicy.Topic(request),context.Version);
                if(screen is not null)
                    answer=answer with {ScreenImage=ScreenIllustration.Render(screen),ScreenNotice=$"Tela de referência: {screen.Module} · versão {(screen.Version.Length==0?"não informada; confirme se corresponde ao seu sistema":screen.Version)}."};
                else if(designerScreens is not null&&await designerScreens.TryCreateAsync(ExplanationPolicy.Topic(request),context.Sources,token) is { } reconstructed)
                    answer=answer with {ScreenImage=ScreenIllustration.Render(reconstructed.Reference),ScreenNotice=$"Tela reconstruída do Designer · {reconstructed.Reference.Module} · código {reconstructed.Commit[..Math.Min(8,reconstructed.Commit.Length)]}. Cores, resolução e informações da instalação podem variar."};
                else answer=answer with {ScreenNotice="Não foi possível reconstruir esta tela a partir das fontes disponíveis. Informe ao suporte qual tela está usando para continuar a orientação."};
            }
            answer=SupportResolution.Ask(answer,request.Mode) with {InterfaceReview=context.InterfaceReview,Checks=context.Checks};
            if(videos is not null)
                try {answer=answer with {Videos=await videos.RecommendAsync(ExplanationPolicy.Topic(request),token)};}
                catch(Exception ex) when(ex is IOException or HttpRequestException or JsonException or System.Xml.XmlException)
                {answer=answer with {Warnings=[..answer.Warnings,"O catálogo de vídeos está indisponível. A orientação foi mantida com as demais fontes."]};}
            await AuditAsync(id,request,settings.Model,answer.SufficientEvidence?"answered":"insufficient",answer.Sources,answer.InputTokens,answer.OutputTokens,context.Checks,context.InterfaceReview);
            // Lacunas: assuntos sem evidência viram a lista de documentações/orientações que faltam (só palavras-chave).
            if(request.Mode!=SimulationMode.Technician) await KnowledgeQuality.RecordAsync(store,KnowledgeSearch.RetrievalQuestion(request),answer.SufficientEvidence);
            return answer;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { await AuditAsync(id,request,settings.Model,"cancelled",[],0,0); throw; }
        catch(SimulationException) { await AuditAsync(id,request,settings.Model,"failed",[],0,0); throw; }
        catch(SqlException ex) { await AuditAsync(id,request,settings.Model,"sql-failed",[],0,0); throw new SimulationException($"Não foi possível consultar o contexto no SQL Server (código {ex.Number})."); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            await AuditAsync(id,request,settings.Model,"configuration-failed",[],0,0);
            throw new SimulationException("Não foi possível carregar as fontes ou a configuração protegida. Confira o armazenamento e o usuário do aplicativo.");
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning("Falha ao gerar resposta: {ErrorType}; causa interna: {InnerType}.",ex.GetType().Name,ex.InnerException?.GetType().Name??"nenhuma");
            await AuditAsync(id,request,settings.Model,"provider-failed",[],0,0);
            throw new SimulationException("Não foi possível concluir a resposta. Confira conexão, modelo e chave da IA e tente novamente.");
        }
    }
    public static SimulationAnswer Validate(Guid id,GeneratedAnswer generated,RetrievalContext context,SimulationMode mode,string model,string requestedModule="",ExplanationFormat format=ExplanationFormat.Text,bool requireInterfaceEvidence=false,string question="")
    {
        var ids=generated.SourceIds.Distinct().ToArray();
        var sources=context.Sources.Where(s=>ids.Contains(s.Id,StringComparer.Ordinal)).ToList();
        var valid=ids.Length>0&&sources.Count==ids.Length&&sources.All(s=>(mode==SimulationMode.Technician||s.CustomerSafe)&&KnowledgeSearch.MatchesModule(s,requestedModule));
        // Insight aprovado (resolved-case) também sustenta a resposta: são soluções confirmadas de problemas específicos.
        var hasFactualSource=sources.Any(s=>s.Kind is "guidance" or "documentation" or "code" or "parameter" or "interface" or "resolved-case")&&(!requireInterfaceEvidence||sources.Any(s=>s.Kind=="interface"&&(requestedModule.Length==0||KnowledgeSearch.ModuleOf(s)==requestedModule)));
        var hasModuleEvidence=requestedModule.Length==0||sources.Any(s=>KnowledgeSearch.ModuleOf(s).Equals(requestedModule,StringComparison.OrdinalIgnoreCase));
        var sufficient=generated.SufficientEvidence&&valid&&hasFactualSource&&hasModuleEvidence&&!string.IsNullOrWhiteSpace(generated.Answer);
        // Invalid IDs are never silently accepted as grounding for a model claim.
        var answer=valid&&hasFactualSource&&hasModuleEvidence?generated.Answer:"Não tenho evidências suficientes para confirmar essa informação no módulo solicitado. Vou precisar de mais detalhes ou da verificação de um técnico.";
        if(!valid) sources=[];
        var steps=sufficient&&format!=ExplanationFormat.Text?(generated.Steps??[]).Where(s=>!string.IsNullOrWhiteSpace(s)).Take(6)
            .Select(s=>mode==SimulationMode.Technician?ExternalText.Clean(s,400):CustomerParameterPolicy.Apply(ExternalText.Clean(s,400),sources,sufficient,includeHandoff:false)).ToArray():[];
        if(steps.Length<2) { steps=[];format=ExplanationFormat.Text; }
        var validated=new SimulationAnswer(id,mode==SimulationMode.Technician?ExternalText.Clean(answer,8000):CustomerParameterPolicy.Apply(answer,sources,sufficient),
            ExternalText.Clean(generated.TechnicalNotes,5000),sufficient,
            mode==SimulationMode.Technician?ExternalText.Clean(generated.FollowUp,500):string.IsNullOrWhiteSpace(generated.FollowUp)?"":CustomerParameterPolicy.Apply(generated.FollowUp,sources,sufficient,includeHandoff:false),sources,context.Warnings,
            context.Segment,context.Version,model,generated.InputTokens,generated.OutputTokens,format,steps);
        var resolvedOnly=sufficient&&sources.Count>0&&sources.All(s=>s.Kind is "resolved-case" or "history" or "ai-history" or "crm-history");
        return NavigationPresentation.Complete(ConfigurationPresentation.Complete(validated with {ResolvedCaseOnly=resolvedOnly},context.Sources,question),context.Sources,requestedModule,question);
    }
    private async Task AuditAsync(Guid id,SimulationRequest request,string model,string status,List<KnowledgeEvidence> sources,int inputTokens,int outputTokens,IReadOnlyList<SourceCheck>? checks=null,InterfaceReview? review=null)
    {
        // No question text, message bodies, snippets, credentials or customer personal data in audit.
        try { await store.WriteAsync(Path.Combine("simulation-audit",$"{id:N}.json"),new {
            id,time=DateTimeOffset.UtcNow,mode=request.Mode.ToString(),ticket=request.TicketId,card=request.CrmId,model,status,inputTokens,outputTokens,
            sourceReferences=sources.Select(s=>new { s.Kind,s.Locator,s.Version }),sourceChecks=checks,interfaceReview=review }); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        { logger.LogWarning("Auditoria da simulação {Id} indisponível: {ErrorType}.",id,ex.GetType().Name); }
        logger.LogInformation("Simulação {Id}: {Status}; {Sources} fontes.",id,status,sources.Count);
    }
}
