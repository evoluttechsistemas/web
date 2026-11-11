using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public static class ResolvedCaseSearch
{
    // The ERP's CentralTickets maps FINALIZADO to CodSituacao=3, not to the unrelated Situacao catalog.
    // Only reviewed, confirmed solution summaries may enter a customer answer; never a transcript from another customer.
    public static async Task<List<KnowledgeEvidence>> ReadAsync(SqlConnection connection,int companyId,int closedSituation,string question,string segment,CancellationToken token,
        IReadOnlyList<string[]>? vocabulary=null)
    {
        var module=KnowledgeSearch.Normalize(KnowledgeSearch.ModuleOf(question));
        // Termos com raiz e sinônimos; o nome do módulo sozinho não identifica o caso.
        var groups=KnowledgeSearch.SearchGroups(question,vocabulary).Where(g=>module.Length==0||g[0]!=module).Take(6).ToList();
        if(groups.Count==0) return [];
        var filter=new SqlTermFilter(groups);
        using var command=new SqlCommand(Query(filter),connection) {CommandTimeout=20};
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=companyId;
        command.Parameters.Add("@ClosedSituation",SqlDbType.Int).Value=closedSituation;
        command.Parameters.Add("@Segment",SqlDbType.VarChar,100).Value=segment;
        command.Parameters.Add("@MinMatches",SqlDbType.Int).Value=MinMatches(filter.Count);
        filter.AddParameters(command);
        var result=new List<KnowledgeEvidence>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            string Text(int i)=>reader.IsDBNull(i)?"":reader.GetValue(i).ToString()??"";
            var text=Sanitize($"Dúvida anterior: {Text(2)}. Solução revisada: {Text(3)}",Text(6));
            result.Add(new("","resolved-case","Solução revisada de atendimento finalizado",$"AiTicketInsights/{Text(0)}; TicketChamadoC/{Text(1)}",
                $"{text}\nSegmento do caso anterior: {ExternalText.Clean(Text(4),100)}. Versão registrada no atendimento anterior: {ExternalText.Clean(Text(5),50)}. Confira a aplicação ao contexto atual; histórico não comprova comportamento ou configuração atual.",
                true,Text(5),KnowledgeSearch.ModuleOf(text)));
        }
        return result;
    }
    /// <summary>
    /// Antes exigia TODAS as palavras (AND): "não sai a nota" nunca achava "NF-e rejeitada".
    /// Agora basta parte delas (1 com até 2 termos, 2 com 3 ou mais), e o mais parecido vem primeiro.
    /// </summary>
    public static int MinMatches(int groups)=>groups>=3?2:1;
    public static string Query(SqlTermFilter filter)
    {
        if(filter.Count is <1 or >8) throw new ArgumentOutOfRangeException(nameof(filter));
        const string question="CONCAT(ai.PerguntaCliente,' ',ai.TopicoTitulo)";
        const string solution="CONCAT(ai.Resumo,' ',ai.Solucao)";
        return $"""
            SELECT TOP (8) ai.Codigo,h.Codigo,LEFT(ai.PerguntaCliente,1000),LEFT(ai.Solucao,2400),cli.Segmento,h.Versao,cli.Nome,
              {filter.Score((question,3),(solution,1))} AS Relevancia
            FROM dbo.AiTicketInsights ai
            JOIN dbo.TicketChamadoC h ON h.Codigo=ai.CodTicketChamadoC
            JOIN dbo.Cliente cli ON cli.Codigo=h.CodCliente AND cli.CodEmp=h.CodEmp
            WHERE h.CodEmp=@CompanyId AND h.CodSituacao=@ClosedSituation
              -- No HELP são dois caminhos de confiança: solução confirmada no fechamento OU aprovado na Curadoria IA.
              -- Exigir os dois juntos deixava a base sem nenhum caso. Descartados e suspensos nunca entram.
              AND (ai.SolucaoConfirmada=1 OR ai.StatusRevisao='aprovado')
              AND ISNULL(ai.StatusRevisao,'') NOT IN ('descartado','suspenso')
              AND NULLIF(LTRIM(RTRIM(ai.Solucao)),'') IS NOT NULL
              AND {filter.MatchedGroups(question,solution)} >= @MinMatches
            ORDER BY Relevancia DESC,
                CASE WHEN ai.SolucaoConfirmada=1 AND ai.StatusRevisao='aprovado' THEN 0 ELSE 1 END,
                CASE WHEN cli.Segmento=@Segment AND @Segment<>'' THEN 0 ELSE 1 END,
                ai.RevisadoEm DESC,ai.Codigo DESC;
            """;
    }
    public static string Sanitize(string text,string customerName)
    {
        if(customerName.Trim().Length>=3) text=Regex.Replace(text,Regex.Escape(customerName.Trim()),"[cliente do atendimento anterior]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        return ExternalText.Plain(text);
    }
}
