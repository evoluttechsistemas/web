using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using EvolutTech.IA.Core;

namespace EvolutTech.IA.Painel.Services;

public sealed class KnowledgeRetriever(PanelStore store, SecretStore secrets, IndexingService indexing,
    SupportContextService support, IConfiguration configuration, ILogger<KnowledgeRetriever> logger) : IKnowledgeRetriever
{
    public async Task<RetrievalContext> RetrieveAsync(SimulationRequest request, CancellationToken token)
    {
        var warnings = new List<string>();
        var candidates = new List<KnowledgeEvidence>();
        var checks=new List<SourceCheck>();
        var searchQuestion = KnowledgeSearch.RetrievalQuestion(request);
        var segment = ExternalText.Clean(request.Segment,100);
        var version = "";
        if (request.Mode != SimulationMode.Commercial && request.TicketId is { } ticketId)
        {
            var context = await support.GetAsync(ticketId,token) ?? throw new SupportTicketNotFoundException();
            segment = ExternalText.Clean(context.Segment,100); version = context.Version;
            if (request.IncludeHistory && request.Mode == SimulationMode.Technician)
            {
                var history = await support.GetHistoryAsync(ticketId,token:token);
                candidates.AddRange(history.Notes.Select(n => new KnowledgeEvidence("", "history", "Anotação de atendimento anterior",
                    $"TicketChamadoD/{n.Id}; chamado {n.TicketId}", ExternalText.Plain(n.Text), false)));
            }
        }
        else if (request.Mode == SimulationMode.Commercial && request.CrmId is { } crmId)
        {
            await using var connection = await OpenAsync(token);
            using var contextCommand = new SqlCommand("""
                SELECT cli.Segmento FROM dbo.CRMC c
                LEFT JOIN dbo.Cliente cli ON cli.Codigo=c.CodCliente AND cli.CodEmp=c.CodEmp
                WHERE c.Codigo=@RecordId AND c.CodEmp=@CompanyId;
                """,connection) { CommandTimeout = 20 };
            AddContextParameters(contextCommand,crmId);
            await using (var reader = await contextCommand.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) throw new SimulationException("Card comercial não encontrado para a empresa configurada.");
                var knownSegment = Text(reader,0);
                if (!string.IsNullOrWhiteSpace(knownSegment)) segment=ExternalText.Clean(knownSegment,100);
            }
            if (request.IncludeHistory)
                candidates.AddRange(await ReadHistoryAsync(connection,CrmHistoryQuery,crmId,"crm-history",token));
        }

        var guidance = await store.GetGuidanceAsync();
        candidates.AddRange(guidance.Where(g => !g.Archived &&
            (g.Segment.Equals("Todos",StringComparison.OrdinalIgnoreCase) || g.Segment.Equals(segment,StringComparison.OrdinalIgnoreCase)))
            .Select(g => new KnowledgeEvidence("","guidance",$"{g.Title} · {g.Module}",$"Orientação/{g.Id}",g.Content,g.CustomerVisible,Module:KnowledgeSearch.ModuleOf(g.Module))));
        checks.Add(new("Orientações cadastradas","consultadas",candidates.Count(s=>s.Kind=="guidance"&&(request.Mode==SimulationMode.Technician||s.CustomerSafe)),"Orientações do segmento e do público selecionados; relevância e módulo são filtrados antes da resposta."));

        var terms = KnowledgeSearch.Terms(searchQuestion);
        if (secrets.HasSql && terms.Length > 0)
        {
            try
            {
                await using var connection = await OpenAsync(token);
                candidates.AddRange(await ReadSharedKnowledgeAsync(connection,terms,token));
                checks.Add(new("Documentação","consultada",candidates.Count(s=>s.Kind=="documentation"&&(request.Mode==SimulationMode.Technician||s.CustomerSafe)),"Documentos filtrados por empresa e visibilidade; documentos internos não autorizados para o cliente são excluídos da resposta externa."));
                checks.Add(new("Parâmetros do banco","consultados",candidates.Count(s=>s.Kind=="parameter"),"Nome, grupo e descrição; valores efetivos do cliente não foram consultados."));
                if(request.Mode!=SimulationMode.Commercial)
                {
                    var related=await ResolvedCaseSearch.ReadAsync(connection,support.CompanyId,configuration.GetValue<int>("Support:ClosedSituation",3),searchQuestion,segment,token);
                    candidates.AddRange(related);
                    checks.Add(new("Atendimentos semelhantes finalizados","consultados",related.Count,"Somente resumos de soluções confirmadas e aprovadas para revisão; nenhuma conversa de outro cliente é enviada."));
                }
                if (request.IncludeHistory && request.Mode == SimulationMode.Technician && request.TicketId is { } id)
                    candidates.AddRange(await ReadHistoryAsync(connection,AiHistoryQuery,id,"ai-history",token));
            }
            catch (SqlException ex)
            {
                logger.LogWarning("Busca de conhecimento SQL indisponível: código {Code}.",ex.Number);
                warnings.Add("Uma das fontes SQL não pôde ser consultada. As demais fontes continuam disponíveis.");
                checks.Add(new("Fontes SQL","indisponíveis",0,"Falha de consulta; as fontes já recuperadas são preservadas."));
            }
        }
        else checks.Add(new("Fontes SQL","não consultadas",0,"Conexão ausente ou pergunta sem termos de busca."));
        await indexing.Ready.WaitAsync(token);
        if (indexing.Result is { } result)
        {
            var run = indexing.History.FirstOrDefault(r => r.State is "Completed" or "CompletedWithLimitations");
            var selection=await store.ReadAsync<KnowledgeSettings>("knowledge-settings.json");
            var requestedModule=KnowledgeSearch.ModuleOf(searchQuestion);
            var sourceProject=selection?.ModuleProjects.FirstOrDefault(pair=>pair.Key.Equals(requestedModule,StringComparison.OrdinalIgnoreCase)).Value;
            if(string.IsNullOrWhiteSpace(sourceProject)&&requestedModule.Length==0)
                sourceProject=selection?.ModuleProjects.GetValueOrDefault("Aplicacao")??selection?.ModuleProjects.GetValueOrDefault("Cadastros")??selection?.ModuleProjects.GetValueOrDefault("PDV");
            var application=await ReadCodeAsync(result,searchQuestion,run?.Commit ?? "",token,sourceProject);candidates.AddRange(application);
            checks.Add(new("Aplicação","consultada",application.Count(s=>s.Kind=="code"),"Métodos e eventos do projeto selecionado, com verificação de hash."));
            checks.Add(new("Interface, menus e atalhos","consultados",application.Count(s=>s.Kind=="interface"),"Designer, rodapé e textos de recursos do código indexado."));
            if (result.Issues.Count > 0) warnings.Add("O código indexado tem limitações semânticas. Relações não resolvidas não comprovam funcionalidades.");
        }
        else {warnings.Add("Ainda não há uma indexação de código concluída.");checks.Add(new("Aplicação e interface","não disponíveis",0,"Não há índice concluído."));}
        if (request.Mode != SimulationMode.Technician)
            warnings.Add("Documentação marcada como interna e orientações sem permissão ao cliente não entram na resposta externa.");
        return new(segment,version,KnowledgeSearch.Rank(candidates,searchQuestion,request.Mode),warnings,Checks:checks);
    }

    public const string CrmHistoryQuery = """
        SELECT TOP (20) a.Codigo,LEFT(a.Anotacao,2200) FROM dbo.CRMAnotacao a
        JOIN dbo.CRMC c ON c.Codigo=a.CodCRMC AND c.CodEmp=a.CodEmp
        WHERE c.Codigo=@RecordId AND c.CodEmp=@CompanyId ORDER BY a.Codigo DESC;
        """;
    public const string AiHistoryQuery = """
        SELECT TOP (12) a.CodTicketAssistenteIA,LEFT(a.Mensagem,2200) FROM dbo.TicketAssistenteIA a
        JOIN dbo.TicketChamadoC h ON h.Codigo=a.CodTicketChamadoC AND h.CodEmp=a.CodEmp
        JOIN dbo.TicketChamadoC origin ON origin.CodCliente=h.CodCliente AND origin.CodEmp=h.CodEmp
        WHERE origin.Codigo=@RecordId AND origin.CodEmp=@CompanyId AND ISNULL(a.Arquivado,'N')<>'S'
        ORDER BY a.CodTicketAssistenteIA DESC;
        """;
    private async Task<List<KnowledgeEvidence>> ReadHistoryAsync(SqlConnection connection,string query,int id,string kind,CancellationToken token)
    {
        using var command=new SqlCommand(query,connection) { CommandTimeout=20 };
        AddContextParameters(command,id);
        await using var reader=await command.ExecuteReaderAsync(token);
        var result=new List<KnowledgeEvidence>();
        while(await reader.ReadAsync(token)) result.Add(new("",kind,
            kind=="crm-history" ? "Anotação do card comercial selecionado" : "Resposta histórica de IA, não validada",
            $"{(kind=="crm-history"?"CRMAnotacao":"TicketAssistenteIA")}/{reader.GetValue(0)}",ExternalText.Plain(Text(reader,1)),
            kind=="crm-history"));
        return result;
    }
    private async Task<List<KnowledgeEvidence>> ReadSharedKnowledgeAsync(SqlConnection connection,string[] terms,CancellationToken token)
    {
        var found=new List<KnowledgeEvidence>();
        // Only fixed, verified tables and columns. Values and default values of parameters are never selected.
        var predicate=string.Join(" OR ",terms.Take(5).Select((_,i)=>$"CONCAT(t.Titulo,' ',t.Descricao) COLLATE Latin1_General_CI_AI LIKE @Term{i}"));
        using var docs=new SqlCommand($"""
            SELECT TOP (25) t.Codigo,t.Titulo,LEFT(t.Descricao,2800),t.Publicado,t.Visibilidade,t.Sistema
            FROM dbo.DocumentacaoTutorial t WHERE t.CodEmp=@CompanyId AND ({predicate}) ORDER BY t.Codigo DESC;
            """,connection) { CommandTimeout=20 };
        AddSearchParameters(docs,terms);
        await using(var reader=await docs.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) found.Add(new("","documentation",Text(reader,1),$"DocumentacaoTutorial/{reader.GetValue(0)}",
                ExternalText.Plain(Text(reader,2)),IsPublicDocument(Text(reader,3),Text(reader,4)),Module:KnowledgeSearch.ModuleOf(Text(reader,5))));
        var blockPredicate=string.Join(" OR ",terms.Take(5).Select((_,i)=>$"CONCAT(t.Titulo,' ',b.Titulo,' ',b.Conteudo) COLLATE Latin1_General_CI_AI LIKE @Term{i}"));
        using var blocks=new SqlCommand($"""
            SELECT TOP (25) b.Codigo,t.Titulo,b.Titulo,LEFT(b.Conteudo,2800),t.Publicado,t.Visibilidade,t.Sistema
            FROM dbo.DocumentacaoTutorialBloco b JOIN dbo.DocumentacaoTutorial t
              ON t.Codigo=b.CodDocumentacaoTutorial AND t.CodEmp=b.CodEmp
            WHERE t.CodEmp=@CompanyId AND ({blockPredicate}) ORDER BY b.Codigo DESC;
            """,connection) { CommandTimeout=20 };
        AddSearchParameters(blocks,terms);
        await using(var reader=await blocks.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) found.Add(new("","documentation",$"{Text(reader,1)} · {Text(reader,2)}",$"DocumentacaoTutorialBloco/{reader.GetValue(0)}",
                ExternalText.Plain(Text(reader,3)),IsPublicDocument(Text(reader,4),Text(reader,5)),Module:KnowledgeSearch.ModuleOf(Text(reader,6))));
        var parameterPredicate=string.Join(" OR ",terms.Take(5).Select((_,i)=>$"CONCAT(p.Parametro,' ',p.Grupo,' ',p.Descricao) COLLATE Latin1_General_CI_AI LIKE @Term{i}"));
        var parameterScore=string.Join(" + ",terms.Take(5).Select((_,i)=>$"CASE WHEN p.Grupo COLLATE Latin1_General_CI_AI LIKE @Term{i} THEN 4 WHEN CONCAT(p.Parametro,' ',p.Descricao) COLLATE Latin1_General_CI_AI LIKE @Term{i} THEN 1 ELSE 0 END"));
        using var parameters=new SqlCommand($"""
            SELECT TOP (30) p.Codigo,p.Parametro,p.Grupo,LEFT(p.Descricao,2400)
            FROM dbo.ParametroDinamico p WHERE p.CodEmp=@CompanyId AND ({parameterPredicate}) ORDER BY ({parameterScore}) DESC,p.Codigo;
            """,connection) { CommandTimeout=20 };
        AddSearchParameters(parameters,terms);
        await using(var reader=await parameters.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) found.Add(new("","parameter",$"{Text(reader,1)} · {Text(reader,2)}",$"ParametroDinamico/{reader.GetValue(0)}",
                $"Definição de parâmetro: {ExternalText.Plain(Text(reader,3))}. O valor efetivo no cliente não foi consultado.",true,Module:KnowledgeSearch.ParameterModule(Text(reader,1),Text(reader,2)),Parameter:new(Text(reader,1),Text(reader,2),ExternalText.Plain(Text(reader,3)))));
        return found;
    }
    private bool IsPublicDocument(string published,string visibility) =>
        published.Equals(configuration["AI:DocumentPublishedValue"] ?? "S",StringComparison.OrdinalIgnoreCase) &&
        (configuration["AI:PublicDocumentVisibility"] ?? "").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Contains(visibility,StringComparer.OrdinalIgnoreCase);
    private void AddSearchParameters(SqlCommand command,string[] terms)
    {
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=support.CompanyId;
        for(var i=0;i<Math.Min(terms.Length,5);i++) command.Parameters.Add($"@Term{i}",SqlDbType.NVarChar,250).Value=$"%{terms[i]}%";
    }
    private void AddContextParameters(SqlCommand command,int id)
    {
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=support.CompanyId;
        command.Parameters.Add("@RecordId",SqlDbType.Int).Value=id;
    }
    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection=new SqlConnection(await secrets.GetSqlAsync() ?? throw new SimulationException("Cadastre a conexão SQL para usar o contexto do atendimento."));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static string Text(SqlDataReader reader,int index)=>reader.IsDBNull(index)?"":reader.GetValue(index).ToString()??"";

    public static async Task<List<KnowledgeEvidence>> ReadCodeAsync(AnalysisResult result,string question,string commit,CancellationToken token,string? sourceProject=null)
    {
        var interfaces=await InterfaceEvidenceSearch.ReadAsync(result,question,commit,token,sourceProject);
        var code=await CodeEvidenceSearch.ReadAsync(result,question,commit,token,sourceProject);
        return [..interfaces,..code];
    }
}
