using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using EvolutTech.IA.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EvolutTech.IA.Indexador;

// Never loads an MSBuild project, analyzer, source generator or ERP assembly for execution.
public sealed class SourceAnalyzer : ISourceAnalyzer
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "packages", "node_modules" };

    public async Task<AnalysisResult> AnalyzeAsync(AnalysisOptions options, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(options.SourceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Pasta de origem inexistente.");
        var result = new AnalysisResult(root, DateTimeOffset.UtcNow, [], [], [], [], []);
        options.Progress?.Report(new("scan", 0, null, "arquivos"));
        var paths = Scan(root, result.Issues, cancellationToken).Order(StringComparer.OrdinalIgnoreCase).ToList();
        result.Solutions.AddRange(paths.Where(p => p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)));
        result.Projects.AddRange(paths.Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));
        var projectDirs = result.Projects.Select(p => Path.GetDirectoryName(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Scope(string path)
        {
            for (var dir = Path.GetDirectoryName(path); dir is not null; dir = Path.GetDirectoryName(dir))
            {
                if (projectDirs.Contains(dir)) return dir;
                if (dir.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            }
            return root;
        }
        var references = ReadReferences(options.ReferenceDirectory, result.Issues);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest,
            preprocessorSymbols: options.Defines ?? []);
        var groups = paths.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).GroupBy(Scope).ToArray();
        var totalFiles = groups.Sum(group => group.Count());
        var filesRead = 0;
        var filesMapped = 0;
        var scopeIndex = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Analisando: {group.Key}");
            options.Progress?.Report(new("parse", filesRead, totalFiles, "arquivos C#"));
            var trees = new List<SyntaxTree>();
            foreach (var path in group)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    using var stream = new MemoryStream(bytes);
                    var source = SourceText.From(stream, encoding: Encoding.UTF8);
                    var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path, cancellationToken);
                    trees.Add(tree);
                    result.Files.Add(new(path, Convert.ToHexString(SHA256.HashData(bytes)), group.Key));
                    foreach (var diagnostic in tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error))
                        result.Issues.Add(Issue("syntax", diagnostic));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { result.Issues.Add(new("read", path, 0, ex.GetType().Name)); }
                filesRead++;
                if (filesRead % 25 == 0 || filesRead == totalFiles)
                    options.Progress?.Report(new("parse", filesRead, totalFiles, "arquivos C#"));
            }
            var compilation = CSharpCompilation.Create("SourceInspection", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            // Diagnostic text is omitted because it can contain literals/secrets from the source.
            options.Progress?.Report(new("semantic", scopeIndex, groups.Length, "escopos"));
            foreach (var d in compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error))
                result.Issues.Add(Issue("semantic", d));
            var classes = new Dictionary<string, ClassInfoResult>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                if (filesMapped % 10 == 0) options.Progress?.Report(new("symbols", filesMapped, totalFiles, "arquivos C#"));
                var model = compilation.GetSemanticModel(tree);
                var syntaxRoot = await tree.GetRootAsync(cancellationToken);
                foreach (var node in syntaxRoot.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var symbol = model.GetDeclaredSymbol(node, cancellationToken);
                    var name = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? FallbackName(node);
                    // Partial declarations share an identity; distinct non-partial declarations remain separate.
                    var key = node.Modifiers.Any(SyntaxKind.PartialKeyword) ? name : $"{name}@{tree.FilePath}:{node.SpanStart}";
                    if (!classes.TryGetValue(key, out var item))
                    {
                        item = new(name, group.Key, [], [], [], FormEvidence(symbol, node));
                        classes.Add(key, item);
                    }
                    else if (item.FormEvidence is null && FormEvidence(symbol, node) is { } evidence)
                    { item = item with { FormEvidence = evidence }; classes[key] = item; }
                    item.Sources.Add(Location(node));
                    foreach (var method in node.Members.OfType<BaseMethodDeclarationSyntax>())
                    {
                        if (method is not (MethodDeclarationSyntax or ConstructorDeclarationSyntax)) continue;
                        var methodSymbol = model.GetDeclaredSymbol(method, cancellationToken);
                        item.Methods.Add(new(methodSymbol?.ToDisplayString() ?? method.Kind().ToString(),
                            method is ConstructorDeclarationSyntax ? "constructor" : "method", Location(method)));
                    }
                    foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                        .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression) && a.Ancestors().OfType<ClassDeclarationSyntax>().First() == node))
                    {
                        var targetSymbol = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                        // Only known events are facts. Unresolved += is preserved as a candidate.
                        if (targetSymbol is not null && targetSymbol is not IEventSymbol) continue;
                        item.Events.Add(new(SafeExpressionName(assignment.Left), Handler(assignment.Right),
                            targetSymbol is IEventSymbol ? "semantic-event" : "syntax-candidate", Location(assignment)));
                    }
                }
                filesMapped++;
            }
            result.Classes.AddRange(classes.Values);
            scopeIndex++;
            options.Progress?.Report(new("semantic", scopeIndex, groups.Length, "escopos"));
        }
        options.Progress?.Report(new("analyzed", result.Files.Count, totalFiles, "arquivos C#"));
        return result;
    }

    private static string Handler(ExpressionSyntax expression) => expression switch
    {
        ObjectCreationExpressionSyntax creation when creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression is { } arg => Handler(arg),
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax member => SafeExpressionName(member),
        ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax => "<anonymous>",
        _ => "<expression>"
    };

    private static string SafeExpressionName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        ThisExpressionSyntax => "this",
        BaseExpressionSyntax => "base",
        MemberAccessExpressionSyntax member => $"{SafeExpressionName(member.Expression)}.{member.Name.Identifier.ValueText}",
        _ => "<expression>"
    };

    private static string? FormEvidence(INamedTypeSymbol? symbol, ClassDeclarationSyntax node)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        for (var type = symbol?.BaseType; type is not null && seen.Add(type); type = type.BaseType)
            if (type.TypeKind != TypeKind.Error && type.ToDisplayString() == "System.Windows.Forms.Form" &&
                type.Locations.All(l => !l.IsInSource)) return "semantic-inheritance";
        // Qualified names/usings are evidence of intent, never proof when dependencies are missing.
        var usings = node.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(u => u.Parent is CompilationUnitSyntax || node.Ancestors().Contains(u.Parent))
            .ToList();
        foreach (var b in node.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var text = b.Type.ToString().Replace("global::", "");
            if (text == "System.Windows.Forms.Form" || text == "Form" && usings.Any(u => u.Alias is null && u.Name?.ToString() == "System.Windows.Forms") ||
                usings.Any(u => u.Alias?.Name.Identifier.ValueText == text && u.Name?.ToString() == "System.Windows.Forms.Form"))
                return "syntax-candidate";
        }
        return null;
    }

    private static string FallbackName(ClassDeclarationSyntax node) => string.Join(".",
        node.Ancestors().Reverse().Select(n => n switch { BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
            TypeDeclarationSyntax t => t.Identifier.ValueText, _ => "" }).Where(s => s.Length > 0).Append(node.Identifier.ValueText));
    private static SourceLocation Location(SyntaxNode node) => new(node.SyntaxTree.FilePath,
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
    private static AnalysisIssue Issue(string kind, Diagnostic d) => new(kind,
        d.Location.SourceTree?.FilePath ?? "", d.Location.IsInSource ? d.Location.GetLineSpan().StartLinePosition.Line + 1 : 0, d.Id);

    private static IEnumerable<string> Scan(string root, List<AnalysisIssue> issues, CancellationToken token)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var dir))
        {
            token.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { issues.Add(new("directory", dir, 0, ex.GetType().Name)); continue; }
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { issues.Add(new("entry", entry, 0, ex.GetType().Name)); continue; }
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                { if (!Excluded.Contains(Path.GetFileName(entry))) stack.Push(entry); }
                else yield return entry;
            }
        }
    }

    private static List<MetadataReference> ReadReferences(string? directory, List<AnalysisIssue> issues)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8");
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.dll") : [];
        if (files.Length == 0) issues.Add(new("references", directory, 0, "NET48_REFERENCE_ASSEMBLIES_MISSING"));
        var references = new List<MetadataReference>();
        foreach (var file in files)
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                // Targeting packs can also contain native helper DLLs: not compiler references.
                if (!pe.HasMetadata || !pe.GetMetadataReader().IsAssembly) continue;
                references.Add(MetadataReference.CreateFromFile(file));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            { issues.Add(new("reference", file, 0, ex.GetType().Name)); }
        return references;
    }
}
