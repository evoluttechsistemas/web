namespace EvolutTech.IA.Painel.Services;

public sealed class ApplicationInspectionService(DesignerScreenService screens)
{
    public static bool NeedsNavigation(string question)=>ApplicationSearchIntent.IsConfiguration(question)||System.Text.RegularExpressions.Regex.IsMatch(KnowledgeSearch.Normalize(question),@"\b(onde|caminho|atalho|menu|acessar|acesso|qual tecla|qual botao|em qual tela)\b");
    public async Task<RetrievalContext> InspectAsync(SimulationRequest request,RetrievalContext context,CancellationToken token)
    {
        var ui=context.Sources.Count(s=>s.Kind=="interface");
        var visual=ui>0?await screens.TryInspectAsync(KnowledgeSearch.RetrievalQuestion(request),context.Sources,token):null;
        var review=new InterfaceReview(ui>0,visual is not null,ui,ui==0?
            "Não há evidência da interface para confirmar caminhos, rótulos ou atalhos.":visual is null?
            "Interface conferida nas declarações do Designer e recursos textuais. Imagem reconstruída indisponível; não houve inspeção visual por imagem.":
            "Interface conferida no Designer e recursos textuais; imagem reconstruída incluída para análise junto com os eventos e métodos. Não é uma sessão do cliente.");
        List<InspectionImage> images=visual is { } v?[new($"{v.Reference.Module} reconstruído do Designer indexado, sem dados de cliente",v.Reference.ImageDataUri,v.Commit)]:[];
        var warnings=context.Warnings.ToList();
        if(ui==0&&NeedsNavigation(request.Question)) warnings.Add("A interface não foi localizada para conferir o caminho solicitado. Não é possível confirmar menus ou atalhos por suposição.");
        return context with {InterfaceReview=review,Images=images,Warnings=warnings};
    }
}
