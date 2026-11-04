using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using EvolutTech.IA.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EvolutTech.IA.Painel.Services;

public static class InterfaceEvidenceSearch
{
    public static async Task<List<KnowledgeEvidence>> ReadAsync(AnalysisResult result,string question,string commit,CancellationToken token,string? project=null)
    {
        var module=KnowledgeSearch.ModuleOf(question);var terms=KnowledgeSearch.FocusTerms(question);
        var content=await IndexedSourceDiscovery.ReadAsync(result,project,token);
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.Root))+Path.DirectorySeparatorChar;
        var eligible=result.Files.Where(f=>f.Path.EndsWith(".Designer.cs",StringComparison.OrdinalIgnoreCase)&&
            (string.IsNullOrWhiteSpace(project)||Path.GetFileName(Path.TrimEndingDirectorySeparator(f.Scope)).Equals(project,StringComparison.OrdinalIgnoreCase)))
            .Select(f=>new {File=f,Relative=Path.GetRelativePath(result.Root,f.Path),Module=KnowledgeSearch.ModuleOf(Path.GetRelativePath(result.Root,f.Path))})
            .Where(f=>(module.Length==0||f.Module.Length==0||f.Module==module)&&!f.Relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(p=>p.Contains(" - antes ",StringComparison.OrdinalIgnoreCase)||p.EndsWith(" - Copia",StringComparison.OrdinalIgnoreCase)||p.EndsWith(" - Copy",StringComparison.OrdinalIgnoreCase))).ToList();
        var files=eligible.Where(f=>(content.GetValueOrDefault(f.File.Path)?.Score(terms)??0)>0||KnowledgeSearch.Score(f.Relative,"",terms)>0)
            .OrderByDescending(f=>ApplicationSearchIntent.FormBoost(f.Relative,question)+(f.Module==module&&module.Length>0?30:0)+KnowledgeSearch.Score(f.Relative,"",terms)+
                (content.GetValueOrDefault(f.File.Path)?.Score(terms)??0)*4+(content.GetValueOrDefault(f.File.Path)?.HasMenus==true?20:0)).Take(32).ToList();
        var found=new List<KnowledgeEvidence>();
        for(var fileIndex=0;fileIndex<files.Count;fileIndex++)
        {
            var file=files[fileIndex];
            token.ThrowIfCancellationRequested();
            if(!Path.GetFullPath(file.File.Path).StartsWith(root,StringComparison.OrdinalIgnoreCase)) continue;
            var bytes=await File.ReadAllBytesAsync(file.File.Path,token);
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.File.Sha256,StringComparison.OrdinalIgnoreCase)) continue;
            using var input=new MemoryStream(bytes);
            var source=Microsoft.CodeAnalysis.Text.SourceText.From(input);
            var tree=CSharpSyntaxTree.ParseText(source,cancellationToken:token);var syntax=await tree.GetRootAsync(token);
            var method=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m=>m.Identifier.ValueText=="InitializeComponent");
            if(method is null) continue;
            var texts=new Dictionary<string,string>();var shortcuts=new Dictionary<string,string>();var events=new Dictionary<string,string>();var parents=new Dictionary<string,string>();var menuItems=new HashSet<string>();
            Dictionary<string,string>? resources=null;
            foreach(var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if(assignment.Left is not MemberAccessExpressionSyntax {Expression:MemberAccessExpressionSyntax {Expression:ThisExpressionSyntax} control} property) continue;
                var id=control.Name.Identifier.ValueText;var name=property.Name.Identifier.ValueText;
                if(name=="Text")
                {
                    if(assignment.Right is LiteralExpressionSyntax literal&&literal.IsKind(SyntaxKind.StringLiteralExpression)) texts[id]=literal.Token.ValueText;
                    else if(assignment.Right is InvocationExpressionSyntax call&&call.Expression.ToString()=="resources.GetString"&&call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax key)
                    {resources??=await ReadResources(result.Root,file.Relative,token);if(resources.TryGetValue(key.Token.ValueText,out var text)) texts[id]=text;}
                }
                if(name=="ShortcutKeys")
                {
                    var keys=assignment.Right.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Where(m=>m.Expression.ToString()=="System.Windows.Forms.Keys")
                        .Select(m=>m.Name.Identifier.ValueText switch {"Control"=>"Ctrl","Menu"=>"Alt",var value=>value}).Distinct();
                    shortcuts[id]=string.Join(" + ",keys);
                }
                if(name is "Click" or "KeyDown" or "KeyPress")
                {
                    var handler=assignment.Right.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault(m=>m.Expression is ThisExpressionSyntax)?.Name.Identifier.ValueText;
                    if(handler is not null) events[id]=handler;
                }
            }
            foreach(var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                if(call.Expression is MemberAccessExpressionSyntax {Name.Identifier.ValueText:"AddRange" or "Add",Expression:MemberAccessExpressionSyntax {Expression:MemberAccessExpressionSyntax {Expression:ThisExpressionSyntax} owner} collection}&&collection.Name.Identifier.ValueText is "Items" or "DropDownItems" or "Controls")
                    foreach(var child in call.ArgumentList.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Where(m=>m.Expression is ThisExpressionSyntax))
                    {parents[child.Name.Identifier.ValueText]=owner.Name.Identifier.ValueText;if(collection.Name.Identifier.ValueText=="DropDownItems") menuItems.Add(child.Name.Identifier.ValueText);}
            string Label(string id)=>texts.GetValueOrDefault(id,"") is { } text&&text!=id?text.Replace("&","").Trim():"";
            string PathOf(string id)
            {
                var parts=new List<string>();var visited=new HashSet<string>();
                while(visited.Add(id)) {if(Label(id) is {Length:>0} label) parts.Insert(0,label);if(!parents.TryGetValue(id,out id!)) break;}
                return string.Join(" → ",parts);
            }
            var allText=KnowledgeSearch.Normalize(string.Join(" ",texts.Values));
            // A user verb ("criar", "lançar") need not appear verbatim in a visible noun ("Lançamento").
            // Require a real label match, then disambiguate through the verified menu hierarchy below.
            if(!terms.Any(t=>allText.Contains(t,StringComparison.Ordinal))) continue;
            var hits=texts.Where(t=>KnowledgeSearch.Score("",t.Value,terms)>0).OrderByDescending(t=>KnowledgeSearch.Score("",t.Value,terms)).Take(5).ToArray();
            if(hits.Length==0) continue;
            var detail=new List<string>();
            foreach(var hit in hits)
            {
                var route=PathOf(hit.Key);var line=method.DescendantNodes().OfType<AssignmentExpressionSyntax>().FirstOrDefault(a=>a.Left.ToString()==$"this.{hit.Key}.Text")?.GetLocation().GetLineSpan().StartLinePosition.Line+1;
                detail.Add($"Rótulo/caminho visível: {route}."+(shortcuts.TryGetValue(hit.Key,out var shortcut)?$" Atalho explícito: {shortcut}.":"")+(events.TryGetValue(hit.Key,out var handler)?$" Evento vinculado (análise interna): {handler}.":"")+$" Linha {line}.");
            }
            // Include the same screen's shortcut legend and action labels, even when they don't repeat the search words.
            var related=texts.Where(t=>!hits.Any(h=>h.Key==t.Key)&&(shortcuts.ContainsKey(t.Key)||t.Value.Contains("ENTER",StringComparison.OrdinalIgnoreCase)||t.Key.StartsWith("toolStripStatusLabel",StringComparison.Ordinal)))
                .Take(26).Select(t=>$"{PathOf(t.Key)}{(shortcuts.TryGetValue(t.Key,out var shortcut)?$" [atalho: {shortcut}]":"")}");
            detail.Add("Outras ações e legendas desta mesma tela: "+string.Join("; ",related)+".");
            var title=$"{Path.GetFileName(file.File.Path).Replace(".Designer.cs","")} · interface · {string.Join("; ",hits.Take(2).Select(h=>PathOf(h.Key)))}";
            var targets=await ReadTargets(result,file.File.Path,events.Values,token);
            var actions=shortcuts.Keys.Union(events.Keys.Where(menuItems.Contains)).Where(id=>Label(id).Length>0)
                .Select(id=>new NavigationAction(ExternalText.Clean(Label(id),100),ExternalText.Clean(PathOf(id),300),shortcuts.GetValueOrDefault(id,""),targets.GetValueOrDefault(events.GetValueOrDefault(id,""),""))).ToArray();
            // Follow the actual menu destination even if similar filenames filled the initial shortlist.
            if(Path.GetFileName(file.File.Path).Equals("Menu.Designer.cs",StringComparison.OrdinalIgnoreCase))
                foreach(var action in actions.Where(a=>a.TargetForm.Length>0&&KnowledgeSearch.Score(a.Label,a.Path,terms)>0).Take(16))
                {
                    var linked=eligible.FirstOrDefault(f=>f.File.Scope.Equals(file.File.Scope,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(f.File.Path).Equals(action.TargetForm+".Designer.cs",StringComparison.OrdinalIgnoreCase));
                    if(linked is not null&&!files.Any(f=>f.File.Path.Equals(linked.File.Path,StringComparison.OrdinalIgnoreCase))) files.Add(linked);
                }
            found.Add(new("","interface",title,file.Relative,ExternalText.Clean(string.Join("\n",detail),2800),true,commit,file.Module,actions));
        }
        // A subject that names a menu branch is scoped to the forms opened by that branch.
        // Other fields containing the same word (such as percentages in a different module) are not that operation.
        var branches=found.Where(s=>Path.GetFileName(s.Locator).Equals("Menu.Designer.cs",StringComparison.OrdinalIgnoreCase))
            .SelectMany(s=>s.Actions??[]).Where(a=>a.TargetForm.Length>0)
            .Select(a=>new {Action=a,ParentMatches=terms.Count(t=>a.Path.Split(" → ").SkipLast(1).SelectMany(KnowledgeSearch.Terms).Contains(t)),
                LabelMatches=KnowledgeSearch.Score(a.Label,"",terms)})
            .Where(a=>a.ParentMatches>0).ToList();
        var strongestParent=branches.Count==0?0:branches.Max(a=>a.ParentMatches);
        var strongestLabel=branches.Where(a=>a.ParentMatches==strongestParent).Select(a=>a.LabelMatches).DefaultIfEmpty().Max();
        var branch=branches.Where(a=>a.ParentMatches==strongestParent&&a.LabelMatches==strongestLabel)
            .Select(a=>a.Action.TargetForm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(ApplicationSearchIntent.PreferredForm(question) is not null) branch.Clear();
        if(ApplicationSearchIntent.IsConfiguration(question))
        {
            var configurable=branch.Where(form=>result.Files.Any(f=>Path.GetFileName(f.Path).Equals(form+".cs",StringComparison.OrdinalIgnoreCase)&&
                content.TryGetValue(f.Path,out var body)&&body.Normalized.Contains("busca parametro dinamico",StringComparison.Ordinal)&&terms.Any(t=>body.Normalized.Contains("\""+t+"\"",StringComparison.Ordinal)))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(configurable.Count>0) branch=configurable;
        }
        return branch.Count==0?found:found.Where(s=>Path.GetFileName(s.Locator).Equals("Menu.Designer.cs",StringComparison.OrdinalIgnoreCase)||
            branch.Contains(Path.GetFileName(s.Locator).Replace(".Designer.cs","",StringComparison.OrdinalIgnoreCase)))
            .Select(s=>Path.GetFileName(s.Locator).Equals("Menu.Designer.cs",StringComparison.OrdinalIgnoreCase)?s with {
                Text=s.Text.Split("Outras ações e legendas desta mesma tela:")[0].Trim(),Actions=s.Actions?.Where(a=>branch.Contains(a.TargetForm)).ToArray()}:s).ToList();
    }
    private static async Task<Dictionary<string,string>> ReadTargets(AnalysisResult result,string designer,IEnumerable<string> handlers,CancellationToken token)
    {
        var targets=new Dictionary<string,string>();
        var runtime=result.Files.FirstOrDefault(f=>f.Path.Equals(designer.Replace(".Designer.cs",".cs",StringComparison.OrdinalIgnoreCase),StringComparison.OrdinalIgnoreCase));
        if(runtime is null) return targets;
        var bytes=await File.ReadAllBytesAsync(runtime.Path,token);
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(runtime.Sha256,StringComparison.OrdinalIgnoreCase)) return targets;
        using var input=new MemoryStream(bytes);
        var tree=CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(input),cancellationToken:token);
        var methods=(await tree.GetRootAsync(token)).DescendantNodes().OfType<MethodDeclarationSyntax>().GroupBy(m=>m.Identifier.ValueText).ToDictionary(g=>g.Key,g=>g.First());
        var projectFiles=result.Files.Where(f=>f.Scope.Equals(runtime.Scope,StringComparison.OrdinalIgnoreCase)).ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        string FormName(string path)=>Path.GetFileName(path).Replace(".Designer.cs","",StringComparison.OrdinalIgnoreCase).Replace(".cs","",StringComparison.OrdinalIgnoreCase);
        var forms=projectFiles.Values.Where(f=>f.Path.EndsWith(".Designer.cs",StringComparison.OrdinalIgnoreCase)).Select(f=>FormName(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliases=result.Classes.SelectMany(c=>c.Sources.Where(s=>projectFiles.ContainsKey(s.File)&&forms.Contains(FormName(s.File))).Select(s=>(Class:c.Name.Split('.').Last(),Form:FormName(s.File))))
            .GroupBy(p=>p.Class,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Form,StringComparer.Ordinal);
        foreach(var handler in handlers.Distinct())
        {
            if(!methods.TryGetValue(handler,out var method)) continue;
            foreach(var creation in method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var name=creation.Type.ToString().Split('.').Last();
                var form=aliases.GetValueOrDefault(name,name);
                if(forms.Contains(form)) targets[handler]=form;
            }
        }
        return targets;
    }
    public static async Task<Dictionary<string,string>> ReadResources(string root,string relative,CancellationToken token)
    {
        var result=new Dictionary<string,string>();var path=Path.Combine(root,relative.Replace(".Designer.cs",".resx",StringComparison.OrdinalIgnoreCase));
        Stream? stream=null;ZipArchive? archive=null;
        try
        {
            if(File.Exists(path)) stream=File.OpenRead(path);
            else
            {
                var zip=Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!,"source.zip");
                if(!File.Exists(zip)) return result;
                archive=ZipFile.OpenRead(zip);var entry=archive.GetEntry(relative.Replace(Path.DirectorySeparatorChar,'/').Replace(".Designer.cs",".resx",StringComparison.OrdinalIgnoreCase));
                if(entry is null||entry.Length>8_000_000) return result;stream=entry.Open();
            }
            using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8_000_000,Async=true});
            var xml=await XDocument.LoadAsync(reader,LoadOptions.None,token);
            foreach(var data in xml.Root?.Elements("data")??[])
                if(data.Attribute("type") is null&&data.Attribute("mimetype") is null&&(string?)data.Attribute("name") is { } name&&data.Element("value") is { } value&&value.Value.Length<=8000)
                    result[name]=ExternalText.Plain(value.Value);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or XmlException) { }
        finally {stream?.Dispose();archive?.Dispose();}
        return result;
    }
}
