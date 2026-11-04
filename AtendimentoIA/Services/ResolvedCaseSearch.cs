using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public static class ResolvedCaseSearch
{
    // The ERP's CentralTickets maps FINALIZADO to CodSituacao=3, not to the unrelated Situacao catalog.
    // Only reviewed, confirmed solution summaries may enter a customer answer; never a transcript from another customer.
    public static async Task<List<KnowledgeEvidence>> ReadAsync(SqlConnection connection,int companyId,int closedSituation,string question,string segment,CancellationToken token)
    {
        var module=KnowledgeSearch.ModuleOf(question);
        var terms=KnowledgeSearch.Terms(question).Where(t=>!t.Equals(module,StringComparison.OrdinalIgnoreCase)).Take(4).ToArray();
        if(terms.Length==0) return [];
        using var command=new SqlCommand(Query(terms.Length),connection) {CommandTimeout=20};
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=companyId;
        command.Parameters.Add("@ClosedSituation",SqlDbType.Int).Value=closedSituation;
        command.Parameters.Add("@Segment",SqlDbType.VarChar,100).Value=segment;
        for(var i=0;i<terms.Length;i++) command.Parameters.Add($"@Term{i}",SqlDbType.NVarChar,150).Value=$"%{terms[i]}%";
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
    public static string Query(int termCount)
    {
        if(termCount is <1 or >4) throw new ArgumentOutOfRangeException(nameof(termCount));
        var predicates=string.Join(" AND ",Enumerable.Range(0,termCount).Select(i=>$"CONCAT(ai.PerguntaCliente,' ',ai.TopicoTitulo,' ',ai.Resumo,' ',ai.Solucao) COLLATE Latin1_General_CI_AI LIKE @Term{i}"));
        return $"""
            SELECT TOP (8) ai.Codigo,h.Codigo,LEFT(ai.PerguntaCliente,1000),LEFT(ai.Solucao,2400),cli.Segmento,h.Versao,cli.Nome
            FROM dbo.AiTicketInsights ai
            JOIN dbo.TicketChamadoC h ON h.Codigo=ai.CodTicketChamadoC
            JOIN dbo.Cliente cli ON cli.Codigo=h.CodCliente AND cli.CodEmp=h.CodEmp
            WHERE h.CodEmp=@CompanyId AND h.CodSituacao=@ClosedSituation
              AND ai.SolucaoConfirmada=1 AND ai.StatusRevisao='aprovado'
              AND NULLIF(LTRIM(RTRIM(ai.Solucao)),'') IS NOT NULL AND ({predicates})
            ORDER BY CASE WHEN cli.Segmento=@Segment AND @Segment<>'' THEN 0 ELSE 1 END,
                ai.RevisadoEm DESC,ai.Codigo DESC;
            """;
    }
    public static string Sanitize(string text,string customerName)
    {
        if(customerName.Trim().Length>=3) text=Regex.Replace(text,Regex.Escape(customerName.Trim()),"[cliente do atendimento anterior]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        return ExternalText.Plain(text);
    }
}
