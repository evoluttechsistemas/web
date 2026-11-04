using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EvolutTech.IA.Core;

namespace EvolutTech.IA.Painel.Services;

public sealed class DesignerScreenService(PanelStore store,IndexingService indexing,IHostEnvironment environment,
    ILogger<DesignerScreenService> logger)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public Task<(ScreenReference Reference,string Commit)?> TryCreateAsync(string question,CancellationToken token)=>BuildAsync(question,true,token);
    public Task<(ScreenReference Reference,string Commit)?> TryInspectAsync(string question,CancellationToken token)=>BuildAsync(question,false,token);
    public Task<(ScreenReference Reference,string Commit)?> TryCreateAsync(string question,IReadOnlyList<KnowledgeEvidence> evidence,CancellationToken token)=>BuildAsync(question,true,token,evidence);
    public Task<(ScreenReference Reference,string Commit)?> TryInspectAsync(string question,IReadOnlyList<KnowledgeEvidence> evidence,CancellationToken token)=>BuildAsync(question,false,token,evidence);
    public static (string Form,string Control,string Keywords,string Caption)? SelectTarget(string question)
    {
        if(ApplicationSearchIntent.PreferredForm(question)=="CadProduto"&&KnowledgeSearch.Terms(question).Contains("balanca"))
            return("CadProduto","txbCodigoBal","codigo,balanca","Aba Gerais > Dados de Balança > Código Balança. Se o campo estiver bloqueado, peça ao suporte avaliar a liberação da digitação manual.");
        if(KnowledgeSearch.ModuleOf(question)!="PDV") return null;
        var terms=KnowledgeSearch.Terms(question);
        if(terms.Contains("caixa")&&terms.Any(t=>t.StartsWith("fech",StringComparison.Ordinal)))
            return("FuncoesPDV","txbFuncao","fechar,caixa","Pressione F11 no PDV. Nesta tela, digite 8 no campo destacado e pressione Enter para acessar Fechar Caixa.");
        if(terms.Contains("quantidade")) return("PDV","txbProduto","quantidade","Digite a quantidade no campo destacado, pressione * e depois passe o produto.");
        return null;
    }
    private async Task<(ScreenReference Reference,string Commit)?> BuildAsync(string question,bool requireQuantity,CancellationToken token,IReadOnlyList<KnowledgeEvidence>? evidence=null)
    {
        if(!OperatingSystem.IsWindows()) return null;
        var result=indexing.Result;if(result is null) return null;
        var selection=await store.ReadAsync<KnowledgeSettings>("knowledge-settings.json");
        var module=KnowledgeSearch.ModuleOf(question);
        var mainProject=selection?.ModuleProjects.GetValueOrDefault("Aplicacao")??selection?.ModuleProjects.GetValueOrDefault("PDV");
        var target=SelectTarget(question);
        ScreenScene? scene=null;
        if(target is null)
        {
            if(evidence is null||!evidence.Any(e=>e.Kind=="interface"))
                evidence=await InterfaceEvidenceSearch.ReadAsync(result,question,"",token,selection?.ModuleProjects.GetValueOrDefault(module)??mainProject);
            scene=ScreenSceneResolver.Resolve(question,evidence);
        }
        if(requireQuantity&&target is null&&scene is null) return null;
        if(target is null&&scene is null&&module!="PDV"&&ApplicationSearchIntent.PreferredForm(question) is null) return null;
        var binding=target??(scene is not null?(scene.Form,"",string.Join(",",KnowledgeSearch.FocusTerms(question)),scene.Caption):
            ApplicationSearchIntent.PreferredForm(question)=="CadProduto"?("CadProduto","txbCodigo","produto","Cadastro de produto."):("PDV","txbProduto","quantidade","Tela principal do PDV."));
        var project=binding.Item1=="CadProduto"?selection?.ModuleProjects.GetValueOrDefault("Cadastros")??mainProject:selection?.ModuleProjects.GetValueOrDefault(module)??mainProject;
        if(string.IsNullOrWhiteSpace(project)) return null;
        var file=scene?.Locator.Length>0?result.Files.SingleOrDefault(f=>Path.GetRelativePath(result.Root,f.Path).Equals(scene.Locator,StringComparison.OrdinalIgnoreCase)):
            result.Files.SingleOrDefault(f=>Path.GetFileName(f.Path).Equals(binding.Item1+".Designer.cs",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(Path.TrimEndingDirectorySeparator(f.Scope)).Equals(project,StringComparison.OrdinalIgnoreCase));
        if(file is null) return null;
        var runtime=result.Files.SingleOrDefault(f=>f.Path.Equals(file.Path.Replace(".Designer.cs",".cs",StringComparison.OrdinalIgnoreCase),StringComparison.OrdinalIgnoreCase));
        if(runtime is null) return null;
        await gate.WaitAsync(token);
        try
        {
            var designerBytes=await VerifiedBytes(result,file,token);var runtimeBytes=await VerifiedBytes(result,runtime,token);
            if(designerBytes is null||runtimeBytes is null) return null;
            var toolSource=Path.Combine(environment.ContentRootPath,"tools","ScreenRenderer.cs");
            if(!File.Exists(toolSource)) toolSource=Path.Combine(AppContext.BaseDirectory,"tools","ScreenRenderer.cs");
            if(!File.Exists(toolSource)) return null;
            var toolBytes=await File.ReadAllBytesAsync(toolSource,token);
            var toolHash=Convert.ToHexString(SHA256.HashData(toolBytes));
            var resources=await InterfaceEvidenceSearch.ReadResources(result.Root,Path.GetRelativePath(result.Root,file.Path),token);
            // Resources and the target binding also participate in cache invalidation.
            var resourceFingerprint=System.Text.Json.JsonSerializer.Serialize(resources.OrderBy(p=>p.Key,StringComparer.Ordinal));
            var cacheKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Sha256+runtime.Sha256+toolHash+resourceFingerprint+binding+"native-layout-v4")));
            var cacheFile=Path.Combine("designer-screens",cacheKey+".json");
            var commit=indexing.History.FirstOrDefault(r=>result.Root.StartsWith(store.JobDirectory(r.Id),StringComparison.OrdinalIgnoreCase))?.Commit??"";
            if(await store.ReadAsync<ScreenReference>(cacheFile) is { } cached) return(cached,commit);
            var directory=Path.Combine(store.Root,"designer-screens",cacheKey);Directory.CreateDirectory(directory);
            var toolDirectory=Path.Combine(store.Root,"designer-screens","renderer-"+toolHash);Directory.CreateDirectory(toolDirectory);
            var exe=Path.Combine(toolDirectory,"ScreenRenderer.exe");
            if(!File.Exists(exe))
            {
                var compiler=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET","Framework64","v4.0.30319","csc.exe");
                if(!File.Exists(compiler)) return null;
                await Run(compiler,["/nologo","/target:exe","/out:"+exe,"/reference:System.dll","/reference:System.Core.dll","/reference:System.Drawing.dll","/reference:System.Windows.Forms.dll","/reference:System.Xml.dll","/reference:System.Xml.Linq.dll",toolSource],token);
            }
            static string Decode(byte[] bytes) {using var reader=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,true);return reader.ReadToEnd();}
            var runtimeText=Decode(runtimeBytes);
            if(binding.Item1=="FuncoesPDV"&&(!resources.Values.Any(v=>v.Contains("8 - Fechar Caixa",StringComparison.OrdinalIgnoreCase))||!runtimeText.Contains("Keys.Enter",StringComparison.Ordinal))) return null;
            var layout=DesignerLayout.Read(Decode(designerBytes),runtimeText,resources);
            var bounds=binding.Item2.Length>0?DesignerLayout.Bounds(layout,binding.Item2):(X:0,Y:0,Width:1,Height:1);
            if(binding.Item2.Length>0) layout.Root!.SetAttributeValue("target",binding.Item2);
            var xml=Path.Combine(directory,"layout.xml");layout.Save(xml);
            var png=Path.Combine(directory,"pdv.png");
            await Run(exe,[xml,png],token);
            if(File.Exists(png+".bounds.xml"))
            {
                var rendered=System.Xml.Linq.XDocument.Load(png+".bounds.xml").Root!;
                bounds=((int)rendered.Attribute("x")!,(int)rendered.Attribute("y")!,(int)rendered.Attribute("width")!,(int)rendered.Attribute("height")!);
            }
            var bytes=await File.ReadAllBytesAsync(png,token);var dimensions=ScreenIllustration.Dimensions(bytes);
            if(binding.Item2.Length==0) bounds=(0,0,dimensions.Width,dimensions.Height);
            var caption=ExternalText.CustomerAnswer(binding.Item4);if(caption.Length>180) caption=caption[..180];
            var reference=new ScreenReference(Guid.NewGuid(),scene?.Module??(binding.Item1=="CadProduto"?"Cadastros":"PDV"),binding.Item3,"",caption,
                "data:image/png;base64,"+Convert.ToBase64String(bytes),100d*bounds.X/dimensions.Width,100d*bounds.Y/dimensions.Height,100d*bounds.Width/dimensions.Width,100d*bounds.Height/dimensions.Height,Highlight:binding.Item2.Length>0);
            ScreenIllustration.Validate(reference);await store.WriteAsync(cacheFile,reference);
            return(reference,commit);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {logger.LogWarning("Reconstrução do Designer indisponível: {ErrorType}",ex.GetType().Name);return null;}
        finally {gate.Release();}
    }
    private static async Task<byte[]?> VerifiedBytes(AnalysisResult result,FileInfoResult file,CancellationToken token)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.Root))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(file.Path).StartsWith(root,StringComparison.OrdinalIgnoreCase)) return null;
        var bytes=await File.ReadAllBytesAsync(file.Path,token);
        return Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)?bytes:null;
    }
    private static async Task Run(string filename,string[] arguments,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start=new ProcessStartInfo(filename) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var argument in arguments) start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Renderizador não iniciou.");
        var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
        try {await process.WaitForExitAsync(timeout.Token);await output;await error;}
        catch(OperationCanceledException) {if(!process.HasExited) process.Kill(entireProcessTree:true);if(token.IsCancellationRequested) throw;throw new IOException("Tempo limite da reconstrução da tela.");}
        if(process.ExitCode!=0) throw new IOException("Falha no renderizador isolado.");
    }
}
