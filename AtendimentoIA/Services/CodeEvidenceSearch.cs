using System.Security.Cryptography;
using EvolutTech.IA.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EvolutTech.IA.Painel.Services;

public static class CodeEvidenceSearch
{
    private sealed record Candidate(KnowledgeEvidence Evidence,int Score,string ClassName,string MethodName,string[] Calls,int LineCount,SourceText Source,MethodDeclarationSyntax Method);
    public static async Task<List<KnowledgeEvidence>> ReadAsync(AnalysisResult result,string question,string commit,CancellationToken token,string? sourceProject=null)
    {
        var module=KnowledgeSearch.ModuleOf(question);
        var terms=KnowledgeSearch.FocusTerms(question);
        var content=await IndexedSourceDiscovery.ReadAsync(result,sourceProject,token);
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.Root))+Path.DirectorySeparatorChar;
        var names=result.Classes.SelectMany(c=>c.Sources.Select(s=>(s.File,Name:c.Name+" "+string.Join(" ",c.Methods.Select(m=>m.Name)))))
            .GroupBy(c=>c.File,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>string.Join(" ",g.Select(c=>c.Name)),StringComparer.OrdinalIgnoreCase);
        var files=result.Files.Where(file=>!file.Path.EndsWith(".Designer.cs",StringComparison.OrdinalIgnoreCase)&&
            (string.IsNullOrWhiteSpace(sourceProject)||Path.GetFileName(Path.TrimEndingDirectorySeparator(file.Scope)).Equals(sourceProject,StringComparison.OrdinalIgnoreCase)))
            .Select(file=>new { File=file, Relative=Path.GetRelativePath(result.Root,file.Path),Names=names.GetValueOrDefault(file.Path,"") })
            .Where(file=>!IsBackup(file.Relative))
            .Select(file=>new { file.File,file.Relative,file.Names,Module=KnowledgeSearch.ModuleOf(file.Relative),
                Score=ApplicationSearchIntent.FormBoost(file.Relative,question)+KnowledgeSearch.Score(file.Relative,file.Names,terms)+(content.GetValueOrDefault(file.File.Path)?.Score(terms)??0)*4+(Path.GetFileNameWithoutExtension(file.Relative).Equals(module,StringComparison.OrdinalIgnoreCase)&&module.Length>0?40:0) })
            .Where(file=>(module.Length==0||file.Module.Length==0||file.Module.Equals(module,StringComparison.OrdinalIgnoreCase))&&
                (file.Score>0||module.Length>0&&file.Module==module))
            .OrderByDescending(file=>file.Score+(module.Length>0&&file.Module==module?20:0)).Take(24).ToArray();
        var candidates=new List<Candidate>();
        var parameters=new List<KnowledgeEvidence>();
        foreach(var selected in files)
        {
            token.ThrowIfCancellationRequested();
            var path=Path.GetFullPath(selected.File.Path);
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var bytes=await File.ReadAllBytesAsync(path,token);
                if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(selected.File.Sha256,StringComparison.OrdinalIgnoreCase)) continue;
                using var input=new MemoryStream(bytes);
                var source=SourceText.From(input,encoding:System.Text.Encoding.UTF8);
                var tree=CSharpSyntaxTree.ParseText(source,path:path,cancellationToken:token);
                var syntax=await tree.GetRootAsync(token);
                foreach(var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c=>c.Expression is MemberAccessExpressionSyntax member&&member.Name.Identifier.ValueText=="buscaParametroDinamico"))
                {
                    var args=call.ArgumentList.Arguments;
                    if(args.Count<4||args[0].Expression is not LiteralExpressionSyntax name||args[2].Expression is not LiteralExpressionSyntax group||args[3].Expression is not LiteralExpressionSyntax description) continue;
                    if(KnowledgeSearch.Score(name.Token.ValueText+" "+group.Token.ValueText,description.Token.ValueText,terms)==0) continue;
                    parameters.Add(new("","parameter",$"{name.Token.ValueText} · {group.Token.ValueText}",$"{selected.Relative}:{call.GetLocation().GetLineSpan().StartLinePosition.Line+1}",
                        $"Definição encontrada no código indexado: {description.Token.ValueText}. Grupo: {group.Token.ValueText}. O valor efetivo no cliente não foi consultado. A alteração do parâmetro é feita pelo suporte.",true,commit,KnowledgeSearch.ParameterModule(name.Token.ValueText,group.Token.ValueText),Parameter:new(name.Token.ValueText,group.Token.ValueText,ExternalText.Plain(description.Token.ValueText))));
                }
                foreach(var method in syntax.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var className=string.Join(".",method.Ancestors().OfType<ClassDeclarationSyntax>().Reverse().Select(c=>c.Identifier.ValueText));
                    var methodName=method.Identifier.ValueText;
                    var text=method.ToString();
                    var score=KnowledgeSearch.Score(methodName,text,terms);
                    if(score==0) continue;
                    score+=ApplicationSearchIntent.FormBoost(path,question);
                    if(module.Length>0&&className.Split('.').Last().Equals(module,StringComparison.OrdinalIgnoreCase)) score+=4;
                    var calls=method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Select(call=>call.Expression switch { IdentifierNameSyntax id=>id.Identifier.ValueText,
                            MemberAccessExpressionSyntax { Expression:ThisExpressionSyntax } member=>member.Name.Identifier.ValueText,_=>"" })
                        .Where(name=>name.Length>0).Distinct().ToArray();
                    var span=method.GetLocation().GetLineSpan();
                    var lineCount=span.EndLinePosition.Line-span.StartLinePosition.Line+1;
                    // Favor complete, focused methods; giant handlers are retained as linked call-site evidence.
                    var locator=$"{selected.Relative}:{span.StartLinePosition.Line+1}";
                    candidates.Add(new(new("","code",$"{className}.{methodName}",locator,
                        ExternalText.Clean(Excerpt(source,method,terms),2800),true,commit,selected.Module),score,className,methodName,calls,lineCount,source,method));
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        }
        var ordered=candidates.OrderByDescending(c=>c.Score).ThenBy(c=>c.LineCount).ToList();
        var chosen=ordered.Take(4).ToList();
        foreach(var seed in chosen.Take(3).ToArray())
        {
            // Related snippets are syntactic call sites in the same class, not invented semantic edges.
            var related=ordered.Where(c=>c.ClassName==seed.ClassName&&SameFile(c.Evidence.Locator,seed.Evidence.Locator)&&
                (c.Calls.Contains(seed.MethodName,StringComparer.Ordinal)||seed.Calls.Contains(c.MethodName,StringComparer.Ordinal)))
                .OrderByDescending(c=>c.Score).ThenBy(c=>c.LineCount);
            foreach(var item in related.Take(2))
                if(!chosen.Any(c=>c.Evidence.Locator==item.Evidence.Locator)) chosen.Add(item with {
                    Evidence=item.Evidence with { Text=ExternalText.Clean(Excerpt(item.Source,item.Method,terms.Append(KnowledgeSearch.Normalize(seed.MethodName)).ToArray()),2800) } });
        }
        return parameters.OrderByDescending(p=>KnowledgeSearch.Score(p.Title,p.Text,terms)).DistinctBy(p=>KnowledgeSearch.Normalize(p.Title)).Take(ApplicationSearchIntent.IsConfiguration(question)?12:4).Concat(chosen.Take(8).Select(c=>c.Evidence)).ToList();
    }
    private static bool SameFile(string left,string right)=>left[..left.LastIndexOf(':')].Equals(right[..right.LastIndexOf(':')],StringComparison.OrdinalIgnoreCase);
    private static bool IsBackup(string path)=>path.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
        .Any(part=>part.Contains(" - antes ",StringComparison.OrdinalIgnoreCase)||part.EndsWith(" - Copia",StringComparison.OrdinalIgnoreCase)||part.EndsWith(" - Copy",StringComparison.OrdinalIgnoreCase));
    private static string Excerpt(SourceText source,MethodDeclarationSyntax method,string[] terms)
    {
        var span=method.GetLocation().GetLineSpan();
        var first=span.StartLinePosition.Line;var last=span.EndLinePosition.Line;
        if(last-first<65) return method.ToString();
        var best=first;var bestScore=-1;
        for(var line=first;line<=last;line++)
        {
            var value=source.Lines[line].ToString();
            var score=KnowledgeSearch.Score("",value,terms);
            if(score>bestScore) { best=line;bestScore=score; }
        }
        var start=Math.Max(first,best-12);
        return $"// Método {method.Identifier.ValueText}; trecho nas linhas {start+1}–{Math.Min(last+1,start+55)}\n"+
            string.Join('\n',Enumerable.Range(start,Math.Min(55,last-start+1)).Select(i=>source.Lines[i].ToString()));
    }
}
