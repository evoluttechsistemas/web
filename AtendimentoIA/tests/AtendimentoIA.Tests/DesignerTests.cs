using EvolutTech.IA.Painel.Services;

internal static class DesignerTests
{
    public static Task LayoutAndClosure()
    {
        var layout=DesignerLayout.Read("""
            class PDV {void InitializeComponent() {
                this.panel = new PanelPersonalizado(); this.txbProduto = new System.Windows.Forms.TextBox();
                this.ClientSize = new System.Drawing.Size(1053,624);
                this.panel.Location = new System.Drawing.Point(381,62);this.panel.Size = new System.Drawing.Size(643,525);
                this.txbProduto.Location = new System.Drawing.Point(13,27);this.txbProduto.Size = new System.Drawing.Size(418,24);
                this.panel.Controls.Add(this.txbProduto);this.Controls.Add(this.panel);
                this.Load += EvilNetworkCall();this.txbProduto.Text = ReadProductionDatabase();
            }}
            ""","""
            class PDV {void PDV_Load() {panel.Visible=false; if(ReadDatabase()) {panel.Visible=true;} txbProduto.Text="";}}
            """);
        var bounds=DesignerLayout.Bounds(layout,"txbProduto");
        if(bounds!=(394,89,418,24)||layout.ToString().Contains("EvilNetworkCall")||layout.ToString().Contains("ReadProductionDatabase")) throw new Exception("Designer was not treated as static data");
        var visibility=layout.Descendants("node").Single(n=>(string?)n.Attribute("id")=="panel").Elements("property").Single(p=>(string?)p.Attribute("name")=="Visible");
        if(!visibility.Value.Equals("False",StringComparison.OrdinalIgnoreCase)) throw new Exception("Conditional runtime logic was evaluated");
        var answer=new SimulationAnswer(Guid.NewGuid(),"Digite a quantidade e pressione *.","",true,"",[],[],"","","fixture",0,0);
        foreach(var format in Enum.GetValues<ExplanationFormat>())
        {
            var confirmed=SupportResolution.Ask(answer with {Format=format},SimulationMode.CustomerSupport);
            if(!confirmed.FollowUp.Contains(SupportResolution.Question)||SupportResolution.Ask(confirmed,SimulationMode.CustomerSupport)!=confirmed) throw new Exception("Missing or repeated resolution question");
        }
        var last=new SimulationTurn("Quantidade no PDV",answer.Answer+SupportResolution.Question,ExplanationFormat.Flow,"Quantidade no PDV");
        if(!SupportResolution.IsConfirmed(new("Sim, resolveu!",SimulationMode.CustomerSupport,History:[last]))||SupportResolution.IsConfirmed(new("Sim, mas ainda não funciona",SimulationMode.CustomerSupport,History:[last]))) throw new Exception("Invalid confirmation classification");
        var negative=new SimulationRequest("Não",SimulationMode.CustomerSupport,History:[last]);
        if(ExplanationPolicy.Format(negative)!=ExplanationFormat.Image||ExplanationPolicy.Topic(negative)!="Quantidade no PDV") throw new Exception("Negative confirmation lost context");
        return Task.CompletedTask;
    }
}
