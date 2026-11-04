using System.Security.Cryptography;
using EvolutTech.IA.Core;
using EvolutTech.IA.Painel.Services;

internal static class ConfigurationSearchTests
{
    public static async Task MenuAndConfiguration()
    {
        const string question="Como configuro perdas no sistema?";
        if(!KnowledgeSearch.FocusTerms(question).SequenceEqual(new[]{"perda"})) throw new Exception("Configuration verbs or plural forms diluted the subject");
        var query=KnowledgeSearch.RetrievalQuestion(new(question,SimulationMode.CustomerSupport,History:[new("Fechar caixa no PDV","F11"),new("Código de balança no cadastro de produto","Resposta")]));
        if(query!=question) throw new Exception("A new subject inherited a previous module");
        var root=Path.Combine(AppContext.BaseDirectory,"configuration-fixtures",Guid.NewGuid().ToString("N"));var scope=Path.Combine(root,"Principal");Directory.CreateDirectory(scope);
        var files=new List<FileInfoResult>();
        async Task Add(string name,string code)
        {
            var path=Path.Combine(scope,name);await File.WriteAllTextAsync(path,code);
            files.Add(new(path,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),scope));
        }
        // More filename matches than the old shortlist size: the real menu and settings still must be found.
        for(var i=0;i<40;i++) await Add($"PerdasAuxiliar{i}.Designer.cs","class Auxiliar {void InitializeComponent(){this.label.Text=\"Perdas\";}}");
        await Add("Menu.Designer.cs","""
            class Menu {void InitializeComponent(){
                this.operacoes.Text="Operações";this.ajustes.Text="Perdas";this.registrar.Text="Registrar Perdas";
                this.operacoes.DropDownItems.Add(this.ajustes);this.ajustes.DropDownItems.Add(this.registrar);
                this.registrar.Click+=new EventHandler(this.Abrir);
                this.marca.Text="EvolutTech";this.importar.Text="Importar Produtos";
                this.marca.DropDownItems.Add(this.importar);this.importar.Click+=new EventHandler(this.Importar);
            }}
            """);
        await Add("Preparacao.cs","""
            class Preparacao {void Iniciar(){
                p.buscaParametroDinamico("FORNECEDOR",empresa,"PERDA","Fornecedor para registrar perda","");
                p.buscaParametroDinamico("PLANO",empresa,"PERDA","Plano de contas para registrar perda","");
                p.buscaParametroDinamico("CENTRO",empresa,"PERDA","Centro de custos para registrar perda","");
            }}
            """);
        // An unrelated screen shares a word but belongs to another branch; it must not become the operation.
        await Add("Menu.cs","class Menu {void Abrir(){var tela=new RegistroPerdas();tela.Show();}void Importar(){var tela=new ImportarExcel();tela.Show();}}");
        await Add("ImportarExcel.Designer.cs","class ImportarExcel {void InitializeComponent(){this.label.Text=\"Importar Produtos\";}}");
        await Add("RegistroPerdas.Designer.cs","class RegistroPerdas {void InitializeComponent(){this.label.Text=\"Registrar Perdas\";}}");
        await Add("OutraOperacao.Designer.cs","class OutraOperacao {void InitializeComponent(){this.label.Text=\"Percentual de Perdas\";}}");
        var result=new AnalysisResult(root,DateTimeOffset.UtcNow,[],[],files,[],[]);
        foreach(var variant in new[]{"Como lançar perdas?","Como criar perdas?","eu consigo criar perdas no sistema","Como faço para lançar perdas no ERP EvolutTech?","Quero registrar perdas de mercadorias no estoque"})
        {
            var screens=await InterfaceEvidenceSearch.ReadAsync(result,variant,"fixture",default,"Principal");
            if(!screens.Any(s=>s.Locator.EndsWith("Menu.Designer.cs"))||!screens.Any(s=>s.Locator.EndsWith("RegistroPerdas.Designer.cs"))||screens.Any(s=>s.Locator.EndsWith("OutraOperacao.Designer.cs")))
                throw new Exception("An equivalent user phrasing lost the menu or target screen: "+variant);
            if(ScreenSceneResolver.Resolve(variant,screens)?.Form!="RegistroPerdas") throw new Exception("The matching menu did not resolve its screen: "+variant);
        }
        var evidence=await KnowledgeRetriever.ReadCodeAsync(result,question,"fixture",default,"Principal");
        if(evidence.Any(s=>s.Kind=="interface"&&s.Locator.EndsWith("OutraOperacao.Designer.cs"))) throw new Exception("Another menu branch contaminated the operation");
        var ranked=KnowledgeSearch.Rank(evidence,question,SimulationMode.CustomerSupport);
        var menu=ranked.Single(s=>s.Locator.EndsWith("Menu.Designer.cs"));
        if(!menu.Text.Contains("Operações → Perdas → Registrar Perdas")) throw new Exception("The menu was lost before content inspection");
        if(ranked.Count(s=>s.Parameter?.Group=="PERDA")!=3) throw new Exception("A group-only parameter match was lost");
        var context=new RetrievalContext("","",ranked,[]);
        var answer=SimulationService.Validate(Guid.NewGuid(),new("Abra Registrar Perdas.","",true,[menu.Id],""),context,SimulationMode.CustomerSupport,"fixture",question:question);
        if(!answer.Answer.Contains("Operações → Perdas → Registrar Perdas")||!new[]{"FORNECEDOR","PLANO","CENTRO"}.All(answer.Answer.Contains)||!answer.Answer.Contains("suporte"))
            throw new Exception("Configuration reply omitted the menu or parameter checklist");
        var unrelated=new KnowledgeEvidence("extra","interface","Outros cadastros","another","",true,Actions:[new("Fornecedor","Cadastros → Fornecedor","")]);
        var focused=NavigationPresentation.Complete(answer with {Answer=answer.Answer+" Fornecedor padrão da perda."},[unrelated],"",question);
        if(focused.Answer.Contains("Cadastros → Fornecedor")) throw new Exception("An incidental noun added an unrelated menu");
    }
}
