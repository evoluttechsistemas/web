using System.Globalization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EvolutTech.IA.Painel.Services;

// Read declarations as data. No source from the ERP is compiled or executed.
public static class DesignerLayout
{
    private static readonly HashSet<string> Types=["Panel","PanelPersonalizado","Label","TextBox","txtQtd","txtInteiro","txtValor","txtDataBranco","GroupBox","RadioButton","ComboBox","MetroComboBox","LinkLabel","RichTextBox","TabControl","TabPage","ToolStrip","ToolStripButton","Button","MetroButton","PictureBox","DataGridView","MetroGrid","DataGridViewCellStyle","DataGridViewTextBoxColumn","MenuStrip","ToolStripMenuItem","ToolStripSeparator","StatusStrip","ToolStripStatusLabel","CheckBox"];
    private static readonly HashSet<string> Properties=["Location","Size","ClientSize","BackColor","ForeColor","Font","Text","Visible","Enabled","Anchor","Dock","AutoSize","BorderStyle","TextAlign","ReadOnly","Multiline","CornerRadius","BorderColor","BorderWidth","HeaderText","Width","Height","AllowUserToAddRows","RowHeadersVisible","ColumnHeadersVisible","CellBorderStyle","ColumnHeadersBorderStyle","ColumnHeadersHeight","ColumnHeadersDefaultCellStyle","DefaultCellStyle","RowHeadersDefaultCellStyle","Alignment","WrapMode","SelectedIndex","DisplayStyle"];
    public static XDocument Read(string designer,string runtime,IReadOnlyDictionary<string,string>? resources=null)
    {
        var document=new XDocument(new XElement("layout",new XElement("node",new XAttribute("id","root"),new XAttribute("type","Form"))));
        var nodes=new Dictionary<string,XElement> { ["root"]=document.Root!.Element("node")! };
        var syntax=CSharpSyntaxTree.ParseText(designer).GetRoot();
        var method=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="InitializeComponent");
        foreach(var variable in method.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            if(variable.Initializer?.Value is ObjectCreationExpressionSyntax style&&style.Type.ToString()=="System.Windows.Forms.DataGridViewCellStyle")
            {var node=new XElement("node",new XAttribute("id",variable.Identifier.ValueText),new XAttribute("type","DataGridViewCellStyle"));nodes[variable.Identifier.ValueText]=node;document.Root!.Add(node);}
        foreach(var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            if(assignment.Left is MemberAccessExpressionSyntax {Expression:ThisExpressionSyntax} member&&assignment.Right is ObjectCreationExpressionSyntax creation)
            {
                var type=creation.Type.ToString().Split('.').Last();
                if(!Types.Contains(type)) continue;
                var node=new XElement("node",new XAttribute("id",member.Name.Identifier.ValueText),new XAttribute("type",type));
                nodes[member.Name.Identifier.ValueText]=node;document.Root!.Add(node);
            }
        foreach(var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>()) AddProperty(nodes,assignment);
        foreach(var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            if(assignment.Left is MemberAccessExpressionSyntax {Name.Identifier.ValueText:"Text",Expression:MemberAccessExpressionSyntax {Expression:ThisExpressionSyntax} target}&&
                assignment.Right is InvocationExpressionSyntax call&&call.Expression.ToString()=="resources.GetString"&&
                call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax key&&resources?.TryGetValue(key.Token.ValueText,out var text)==true&&nodes.TryGetValue(target.Name.Identifier.ValueText,out var node))
                node.Add(new XElement("property",new XAttribute("name","Text"),new XAttribute("kind","literal"),text));
        foreach(var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if(call.Expression is not MemberAccessExpressionSyntax action||action.Name.Identifier.ValueText is not ("Add" or "AddRange")||action.Expression is not MemberAccessExpressionSyntax collection) continue;
            if(collection.Name.Identifier.ValueText is not ("Controls" or "Items" or "DropDownItems" or "Columns")) continue;
            var owner=Id(collection.Expression);
            if(!nodes.TryGetValue(owner,out var parent)) continue;
            foreach(var child in call.ArgumentList.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Where(m=>m.Expression is ThisExpressionSyntax))
                if(nodes.ContainsKey(child.Name.Identifier.ValueText)) parent.Add(new XElement("child",new XAttribute("id",child.Name.Identifier.ValueText)));
        }
        // Only literal assignments directly in the load method: never evaluate conditional database/configuration logic.
        var className=method.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText;
        var load=CSharpSyntaxTree.ParseText(runtime).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m=>m.Identifier.ValueText==className+"_Load");
        foreach(var statement in load?.Body?.Statements.OfType<ExpressionStatementSyntax>()??[])
            if(statement.Expression is AssignmentExpressionSyntax assignment&&assignment.Left is MemberAccessExpressionSyntax property&&property.Expression is IdentifierNameSyntax id&&nodes.TryGetValue(id.Identifier.ValueText,out var node)&&property.Name.Identifier.ValueText is "Visible" or "Text")
                AddLiteral(node,property.Name.Identifier.ValueText,assignment.Right);
        return document;
    }
    private static string Id(ExpressionSyntax expression)=>expression switch {ThisExpressionSyntax=>"root",IdentifierNameSyntax id=>id.Identifier.ValueText,MemberAccessExpressionSyntax {Expression:ThisExpressionSyntax} m=>m.Name.Identifier.ValueText,_=>""};
    private static void AddProperty(Dictionary<string,XElement> nodes,AssignmentExpressionSyntax assignment)
    {
        if(assignment.Left is not MemberAccessExpressionSyntax property||!Properties.Contains(property.Name.Identifier.ValueText)||!nodes.TryGetValue(Id(property.Expression),out var node)) return;
        if(assignment.Right is IdentifierNameSyntax id&&nodes.ContainsKey(id.Identifier.ValueText))
        {node.Add(new XElement("reference",new XAttribute("name",property.Name.Identifier.ValueText),id.Identifier.ValueText));return;}
        AddLiteral(node,property.Name.Identifier.ValueText,assignment.Right);
    }
    private static void AddLiteral(XElement node,string name,ExpressionSyntax expression)
    {
        string? value=null;string kind="literal";
        if(expression is LiteralExpressionSyntax literal) value=Convert.ToString(literal.Token.Value,CultureInfo.InvariantCulture);
        else if(int.TryParse(expression.ToString(),out var integer)) value=integer.ToString(CultureInfo.InvariantCulture);
        else if(expression is ObjectCreationExpressionSyntax creation)
        {
            var args=creation.ArgumentList?.Arguments.Select(a=>a.Expression).ToArray()??[];
            var type=creation.Type.ToString().Split('.').Last();
            if(type is "Point" or "Size"&&args.Length==2&&args.All(a=>int.TryParse(a.ToString(),out _))) {kind="pair";value=string.Join(",",args.Select(a=>a.ToString()));}
            if(type=="Font"&&args.Length>=2&&args[0] is LiteralExpressionSyntax fontName&&args[1] is LiteralExpressionSyntax fontSize)
            {kind="font";value=$"{fontName.Token.ValueText}|{fontSize.Token.ValueText}|{(creation.ToString().Contains("FontStyle.Bold")?"Bold":"Regular")}";}
        }
        else if(expression.ToString().Contains("System.Drawing.Color."))
        {
            kind="color";
            if(expression is InvocationExpressionSyntax invocation&&invocation.Expression.ToString()=="System.Drawing.Color.FromArgb")
            {
                var values=invocation.ArgumentList.Arguments.Select(a=>a.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().LastOrDefault()?.Token.ValueText).ToArray();
                if(values.Length is 3 or 4&&values.All(v=>byte.TryParse(v,out _))) value=string.Join(",",values);
            }
            else if(expression is MemberAccessExpressionSyntax color&&color.Expression.ToString()=="System.Drawing.Color") value=color.Name.Identifier.ValueText;
        }
        else
        {
            var enumValues=expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Where(m=>m.Expression.ToString().StartsWith("System.Windows.Forms.")&&!m.Name.Identifier.ValueText.EndsWith("Styles")).Select(m=>m.Name.Identifier.ValueText).ToArray();
            if(enumValues.Length>0) {kind="enum";value=string.Join(",",enumValues);}
        }
        if(value is null||value.Length>500) return;
        node.Elements("property").Where(p=>(string?)p.Attribute("name")==name).Remove();
        node.Add(new XElement("property",new XAttribute("name",name),new XAttribute("kind",kind),value));
    }
    public static (int X,int Y,int Width,int Height) Bounds(XDocument layout,string id)
    {
        var nodes=layout.Root!.Elements("node").ToDictionary(n=>(string)n.Attribute("id")!);
        (int,int) Pair(XElement node,string property)
        {
            var parts=node.Elements("property").Single(p=>(string?)p.Attribute("name")==property).Value.Split(',');return(int.Parse(parts[0]),int.Parse(parts[1]));
        }
        var node=nodes[id];var (width,height)=Pair(node,"Size");var x=0;var y=0;
        var visited=new HashSet<string>();
        while((string?)node.Attribute("id")!="root")
        {
            if(!visited.Add((string)node.Attribute("id")!)) throw new InvalidDataException("Ciclo no layout.");
            var position=Pair(node,"Location");x+=position.Item1;y+=position.Item2;
            node=nodes.Values.Single(n=>n.Elements("child").Any(c=>(string?)c.Attribute("id")== (string?)node.Attribute("id")));
        }
        return(x,y,width,height);
    }
}
