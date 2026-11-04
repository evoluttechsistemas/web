using System.Text;
using System.Text.Json;
using EvolutTech.IA.Core;
using EvolutTech.IA.Indexador;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("Uso: EvolutTech.IA.Indexador <pasta-ERP> [--json <arquivo>] [--references <pasta-dlls>] [--define DEBUG,TRACE]");
    return args.Length == 0 ? 2 : 0;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    string? output = null, references = null;
    var defines = new List<string>();
    for (var i = 1; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Opção sem valor.");
        switch (args[i])
        {
            case "--json": output = Path.GetFullPath(args[i + 1]); break;
            case "--references": references = Path.GetFullPath(args[i + 1]); break;
            case "--define": defines.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
            default: throw new ArgumentException("Opção desconhecida.");
        }
    }
    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0]));
    if (output is not null && (output.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        throw new ArgumentException("O relatório deve ficar fora da pasta do ERP.");
    if (output is not null)
        for (var path = Path.GetDirectoryName(output); path is not null; path = Path.GetDirectoryName(path))
            if (Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("O destino não pode atravessar links ou junções.");
    ISourceAnalyzer analyzer = new SourceAnalyzer();
    var result = await analyzer.AnalyzeAsync(new(root, references, defines),
        new Progress<string>(s => Console.Error.WriteLine(s)), cancellation.Token);
    Console.WriteLine($"Soluções: {result.Solutions.Count} | Projetos: {result.Projects.Count}");
    Console.WriteLine($"Arquivos C# lidos: {result.Files.Count} | Classes: {result.Classes.Count}");
    Console.WriteLine($"Métodos: {result.MethodCount} | Construtores: {result.ConstructorCount}");
    Console.WriteLine($"Forms confirmados: {result.Forms.Count(f => f.FormEvidence == "semantic-inheritance")} | Candidatos: {result.Forms.Count(f => f.FormEvidence == "syntax-candidate")}");
    foreach (var form in result.Forms.OrderBy(f => f.Name))
    {
        Console.WriteLine($"\nFORM {form.Name} [{form.FormEvidence}]");
        foreach (var source in form.Sources) Console.WriteLine($"  {source.File}:{source.Line}");
        foreach (var ev in form.Events) Console.WriteLine($"  {ev.Target} += {ev.Handler} [{ev.Evidence}] ({ev.Source.File}:{ev.Source.Line})");
    }
    Console.WriteLine($"\nDiagnósticos/limitações: {result.Issues.Count} (sem literais do código)");
    foreach (var issue in result.Issues.Take(20)) Console.WriteLine($"  {issue.Kind}: {issue.Code} {issue.File}:{issue.Line}");
    if (output is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(stream, result, new JsonSerializerOptions { WriteIndented = true }, cancellation.Token);
        Console.WriteLine($"Relatório: {output}");
    }
    return result.Issues.Any(i => i.Kind is "read" or "directory" or "entry" or "syntax") ? 3 : 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Análise cancelada."); return 130; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{ Console.Error.WriteLine($"Falha de entrada/saída ou configuração: {ex.GetType().Name}. Verifique argumentos, acesso e destino do relatório."); return 1; }
