namespace EvolutTech.IA.Core;

public sealed record SourceLocation(string File, int Line);
public sealed record FileInfoResult(string Path, string Sha256, string Scope);
public sealed record MethodInfoResult(string Name, string Kind, SourceLocation Source);
public sealed record EventInfoResult(string Target, string Handler, string Evidence, SourceLocation Source);
public sealed record ClassInfoResult(string Name, string Scope, List<SourceLocation> Sources,
    List<MethodInfoResult> Methods, List<EventInfoResult> Events, string? FormEvidence);
public sealed record AnalysisIssue(string Kind, string File, int Line, string Code);
public sealed record AnalysisResult(string Root, DateTimeOffset StartedAt, List<string> Solutions,
    List<string> Projects, List<FileInfoResult> Files, List<ClassInfoResult> Classes,
    List<AnalysisIssue> Issues)
{
    public int MethodCount => Classes.Sum(c => c.Methods.Count(m => m.Kind == "method"));
    public int ConstructorCount => Classes.Sum(c => c.Methods.Count(m => m.Kind == "constructor"));
    public IEnumerable<ClassInfoResult> Forms => Classes.Where(c => c.FormEvidence is not null);
}

public sealed record ProcessingProgress(string Stage, long Completed, long? Total, string Unit);
public sealed record AnalysisOptions(string SourceRoot, string? ReferenceDirectory = null,
    IReadOnlyList<string>? Defines = null, IProgress<ProcessingProgress>? Progress = null);
public interface ISourceAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisOptions options, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
