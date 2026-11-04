using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using EvolutTech.IA.Core;

namespace EvolutTech.IA.Painel.Services;

// Search verified file contents before applying a shortlist limit.
public static class IndexedSourceDiscovery
{
    public sealed record Content(string Normalized,bool HasMenus)
    {
        public int Score(IReadOnlyList<string> terms)=>terms.Sum(t=>Normalized.Contains(t,StringComparison.Ordinal)?1:0);
    }
    private static readonly ConditionalWeakTable<AnalysisResult,ConcurrentDictionary<string,Lazy<Task<Dictionary<string,Content>>>>> Cache=new();
    public static Task<Dictionary<string,Content>> ReadAsync(AnalysisResult result,string? project,CancellationToken token)
    {
        var projects=Cache.GetValue(result,_=>new(StringComparer.OrdinalIgnoreCase));
        return projects.GetOrAdd(project??"",p=>new(()=>BuildAsync(result,p),LazyThreadSafetyMode.ExecutionAndPublication)).Value.WaitAsync(token);
    }
    private static async Task<Dictionary<string,Content>> BuildAsync(AnalysisResult result,string project)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.Root))+Path.DirectorySeparatorChar;
        var entries=new Dictionary<string,Content>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in result.Files.Where(f=>f.Path.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)&&
            (project.Length==0||Path.GetFileName(Path.TrimEndingDirectorySeparator(f.Scope)).Equals(project,StringComparison.OrdinalIgnoreCase))))
        {
            if(!Path.GetFullPath(file.Path).StartsWith(root,StringComparison.OrdinalIgnoreCase)||IsBackup(file.Path)) continue;
            try
            {
                var bytes=await File.ReadAllBytesAsync(file.Path);
                if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)) continue;
                using var reader=new StreamReader(new MemoryStream(bytes),System.Text.Encoding.UTF8,true);
                var normalized=KnowledgeSearch.Normalize(await reader.ReadToEndAsync());
                if(file.Path.EndsWith(".Designer.cs",StringComparison.OrdinalIgnoreCase)&&normalized.Contains("resources.get string",StringComparison.Ordinal))
                {
                    var resources=await InterfaceEvidenceSearch.ReadResources(result.Root,Path.GetRelativePath(result.Root,file.Path),CancellationToken.None);
                    normalized+="\n"+KnowledgeSearch.Normalize(string.Join("\n",resources.Values));
                }
                entries[file.Path]=new(normalized,normalized.Contains(".drop down items.",StringComparison.Ordinal));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        }
        return entries;
    }
    private static bool IsBackup(string path)=>path.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(p=>
        p.Contains(" - antes ",StringComparison.OrdinalIgnoreCase)||p.EndsWith(" - Copia",StringComparison.OrdinalIgnoreCase)||p.EndsWith(" - Copy",StringComparison.OrdinalIgnoreCase));
}
