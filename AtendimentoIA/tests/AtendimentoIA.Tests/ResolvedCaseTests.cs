using EvolutTech.IA.Painel.Services;

internal static class ResolvedCaseTests
{
    public static Task Corroboration()
    {
        var sql=ResolvedCaseSearch.Query(new SqlTermFilter([["nfe","nota fiscal"],["rejeicao"]]));
        if(!sql.Contains("h.CodEmp=@CompanyId")||!sql.Contains("h.CodSituacao=@ClosedSituation")||!sql.Contains("(ai.SolucaoConfirmada=1 OR ai.StatusRevisao='aprovado')")||!sql.Contains("NOT IN ('descartado','suspenso')")||
            !sql.Contains("LIKE @T0_0")||!sql.Contains("LIKE @T0_1")||!sql.Contains("LIKE @T1_0")||!sql.Contains(">= @MinMatches")||
            sql.Contains("TicketChamadoD")||sql.Contains("UPDATE ")||sql.Contains("INSERT ")) throw new Exception("Similar case query is not bounded and reviewed");
        if(ResolvedCaseSearch.MinMatches(1)!=1||ResolvedCaseSearch.MinMatches(2)!=1||ResolvedCaseSearch.MinMatches(4)!=2) throw new Exception("Similar case minimum matches changed");
        // Raiz e sinônimos: o cliente escreve de um jeito, a documentação de outro.
        var groups=KnowledgeSearch.SearchGroups("Não consigo emitir a nota fiscal, deu rejeição");
        if(!groups.Any(g=>g.Contains("nfe"))||!groups.Any(g=>g.Contains("emit"))||!groups.Any(g=>g.Contains("rejeicao")&&g.Contains("rejeitada"))) throw new Exception("Vocabulary expansion lost a root or synonym");
        if(groups.Any(g=>g.Contains("nao")||g.Contains("deu"))) throw new Exception("Filler words reached the search");
        // NFC-e e NF-e: com ou sem hífen é o mesmo termo, e uma não responde pela outra.
        if(KnowledgeSearch.Normalize("Cancelamento de NFC-e")!="cancelamento de nfce"||KnowledgeSearch.Normalize("NF-e")!="nfe") throw new Exception("Fiscal document spelling was not unified");
        if(!new SqlTermFilter([["nfce"]]).Matches("x").Contains("@T0_1")) throw new Exception("Hyphen spelling is not searched in SQL");
        var nfeDoc=new KnowledgeEvidence("D1","documentation","Cancelamento de NF-e","fixture","Clique no X vermelho da NF-e.",true,"","");
        var nfceDoc=new KnowledgeEvidence("D2","documentation","Cancelamento de NFC-e","fixture","Clique em cancelar NFC-e.",true,"","");
        var rankedFiscal=KnowledgeSearch.Rank([nfeDoc,nfceDoc],"como cancelar uma NFCe",SimulationMode.CustomerSupport);
        if(rankedFiscal.Count!=1||rankedFiscal[0].Title!="Cancelamento de NFC-e") throw new Exception("An NF-e source answered an NFC-e question");
        if(QueryRewriter.Sanitize("cancelamento NF-e nota fiscal eletrônica NFC-e","como cancelar NFC-e").Contains("NF-e")) throw new Exception("Rewrite changed the fiscal document");
        var sanitized=ResolvedCaseSearch.Sanitize("Empresa Exemplo Ltda: contato fixture@example.invalid; senha=abc123; resolvido.","Empresa Exemplo Ltda");
        if(sanitized.Contains("Empresa Exemplo Ltda")||sanitized.Contains("fixture@example.invalid")||sanitized.Contains("abc123")) throw new Exception("Customer history was not masked");
        var historical=new KnowledgeEvidence("S1","resolved-case","Caso revisado","fixture","Solução anterior",true,"25.10.01","PDV");
        var context=new RetrievalContext("","26.10.06",[historical],[]);
        // Insight aprovado do mesmo módulo sustenta a orientação, marcado como baseado em atendimento anterior.
        var result=SimulationService.Validate(Guid.NewGuid(),new("Refaça a sangria pelo F6, como no atendimento anterior.","",true,["S1"],""),context,SimulationMode.CustomerSupport,"fixture","PDV");
        if(!result.SufficientEvidence||!result.ResolvedCaseOnly) throw new Exception("An approved resolved case was not accepted as evidence");
        // De outro módulo, não vale.
        var other=SimulationService.Validate(Guid.NewGuid(),new("Faça como no PDV.","",true,["S1"],""),context,SimulationMode.CustomerSupport,"fixture","Consumer");
        if(other.SufficientEvidence||other.Answer.Contains("Faça como no PDV")) throw new Exception("A resolved case from another module was accepted");
        // Histórico de IA não validado continua sem valor de prova.
        var aiHistory=new RetrievalContext("","",[historical with {Kind="ai-history"}],[]);
        if(SimulationService.Validate(Guid.NewGuid(),new("O histórico diz.","",true,["S1"],""),aiHistory,SimulationMode.CustomerSupport,"fixture","PDV").SufficientEvidence)
            throw new Exception("Unvalidated AI history was accepted as evidence");
        return Task.CompletedTask;
    }
}
