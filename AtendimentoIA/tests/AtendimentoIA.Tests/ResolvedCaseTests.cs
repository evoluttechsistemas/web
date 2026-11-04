using EvolutTech.IA.Painel.Services;

internal static class ResolvedCaseTests
{
    public static Task Corroboration()
    {
        var sql=ResolvedCaseSearch.Query(2);
        if(!sql.Contains("h.CodEmp=@CompanyId")||!sql.Contains("h.CodSituacao=@ClosedSituation")||!sql.Contains("ai.SolucaoConfirmada=1")||!sql.Contains("ai.StatusRevisao='aprovado'")||
            !sql.Contains("LIKE @Term0 AND")||!sql.Contains("LIKE @Term1")||sql.Contains("TicketChamadoD")||sql.Contains("UPDATE ")||sql.Contains("INSERT ")) throw new Exception("Similar case query is not bounded and reviewed");
        var sanitized=ResolvedCaseSearch.Sanitize("Empresa Exemplo Ltda: contato fixture@example.invalid; senha=abc123; resolvido.","Empresa Exemplo Ltda");
        if(sanitized.Contains("Empresa Exemplo Ltda")||sanitized.Contains("fixture@example.invalid")||sanitized.Contains("abc123")) throw new Exception("Customer history was not masked");
        var historical=new KnowledgeEvidence("S1","resolved-case","Caso revisado","fixture","Solução anterior",true,"25.10.01","PDV");
        var context=new RetrievalContext("","26.10.06",[historical],[]);
        var result=SimulationService.Validate(Guid.NewGuid(),new("A funcionalidade está disponível porque funcionou antes.","",true,["S1"],""),context,SimulationMode.CustomerSupport,"fixture","PDV");
        if(result.SufficientEvidence||result.Answer.Contains("porque funcionou")) throw new Exception("Historical solution was accepted without corroboration");
        return Task.CompletedTask;
    }
}
