using System.IO.Compression;
using EvolutTech.IA.Core;
using EvolutTech.IA.Indexador;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

internal static class ProgressTests
{
    private static void Check(bool condition) { if(!condition) throw new InvalidOperationException("Progress assertion failed"); }
    private static (PanelStore Store,IConfiguration Config,SecretStore Secrets) Setup()
    {
        var environment=new TestEnvironment { ContentRootPath=Path.Combine(AppContext.BaseDirectory,"progress-fixtures",Guid.NewGuid().ToString("N")) };
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Panel:DataDirectory"]="data" }).Build();
        var store=new PanelStore(configuration,environment);
        return (store,configuration,new SecretStore(store,new EphemeralDataProtectionProvider()));
    }
    public static async Task Transfer()
    {
        var (store,configuration,secrets)=Setup();
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,leaveOpen:true))
        {
            for(var i=0;i<110;i++) { using var writer=new StreamWriter(zip.CreateEntry($"repo/Code{i}.cs").Open()); writer.Write($"class Code{i} {{}}"); }
            using(var ignored=new StreamWriter(zip.CreateEntry("repo/ignored.txt").Open())) ignored.Write("ignored");
            using(var resource=new StreamWriter(zip.CreateEntry("repo/Screen.resx").Open())) resource.Write("<root />");
        }
        var archive=memory.ToArray();
        var progress=new Capture();
        var source=new GitHubSource(new HttpClient(new GitHubHandler(archive)),configuration,secrets);
        await source.DownloadAsync(new("fixture/repo","release/prod"),store.JobDirectory(Guid.NewGuid()),default,progress);
        var downloaded=progress.Values.Last(p=>p.Stage=="download");
        Check(downloaded.Completed==archive.Length);
        var extracted=progress.Values.Last(p=>p.Stage=="extract");
        Check(extracted.Completed==112&&extracted.Total==112);
        Check(progress.Values.Any(p=>p.Stage=="extract"&&p.Completed==100));
        Check(Directory.GetFiles(store.Root,"*.resx",SearchOption.AllDirectories).Length==0);
        var archivePath=Directory.GetFiles(store.Root,"source.zip",SearchOption.AllDirectories).Single();
        await GitHubSource.ExtractSourcesAsync(archivePath,Path.Combine(store.Root,"with-resources"),1000000,1000,default,includeResources:true);
        Check(Directory.GetFiles(Path.Combine(store.Root,"with-resources"),"*.resx",SearchOption.AllDirectories).Length==1);
        var unknown=IndexingService.DescribeProgress(new("download",1000000,null,"bytes"));
        Check(unknown.Contains("não informado")&&!unknown.Contains('%'));
    }
    public static async Task Analysis()
    {
        var (store,_,_)=Setup();
        var source=Path.Combine(store.Root,"source");Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source,"One.cs"),"class One { void Run() {} }");
        await File.WriteAllTextAsync(Path.Combine(source,"Two.cs"),"class Two { void Run() {} }");
        var progress=new Capture();
        var result=await new SourceAnalyzer().AnalyzeAsync(new(source,Progress:progress));
        Check(result.Files.Count==2);
        Check(progress.Values.Any(p=>p.Stage=="parse"&&p.Completed==2&&p.Total==2));
        Check(progress.Values.Any(p=>p.Stage=="semantic"&&p.Completed==1&&p.Total==1));
        Check(progress.Values.Last().Stage=="analyzed"&&progress.Values.Last().Completed==2);
        var id=Guid.NewGuid();
        await store.SaveRunAsync(new(id,DateTimeOffset.UtcNow,"fixture/repo","main","Running","progress",Stage:"parse",Processed:2,Total:10,Unit:"arquivos C#",UpdatedAt:DateTimeOffset.UtcNow));
        var saved=(await store.GetRunsAsync()).Single();
        Check(saved.Stage=="parse"&&saved.Processed==2&&saved.Total==10&&saved.UpdatedAt is not null);
    }
    private sealed class Capture:IProgress<ProcessingProgress>
    {
        public List<ProcessingProgress> Values {get;}=[];
        public void Report(ProcessingProgress value)=>Values.Add(value);
    }
}
