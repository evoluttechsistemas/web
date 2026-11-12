using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EvolutTech.IA.Painel.Services;

public sealed record SourceSettings(string Repository, string Reference);
public sealed record IndexRun(Guid Id, DateTimeOffset StartedAt, string Repository, string Reference,
    string State, string Message, string? Commit = null, int Files = 0, int Classes = 0,
    int Methods = 0, int Forms = 0, int Issues = 0, string? Stage = null, long Processed = 0,
    long? Total = null, string? Unit = null, DateTimeOffset? UpdatedAt = null, DateTimeOffset? FinishedAt = null);
public sealed class GuidanceInput : IValidatableObject
{
    [Required(ErrorMessage = "Informe o título."), StringLength(160)] public string Title { get; set; } = "";
    [Required(ErrorMessage = "Informe o segmento ou use Todos."), StringLength(100)] public string Segment { get; set; } = "Todos";
    [Required(ErrorMessage = "Informe o módulo."), StringLength(100)] public string Module { get; set; } = "";
    [StringLength(12000)] public string Content { get; set; } = "";
    /// <summary>Observação do responsável para a IA (o que faltou, o que trocou). Guia a resposta, não é citada ao cliente.</summary>
    [StringLength(4000)] public string Note { get; set; } = "";
    public bool CustomerVisible { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Content) && string.IsNullOrWhiteSpace(Note))
            yield return new("Escreva a orientação ou a observação para a IA.", [nameof(Content)]);
    }
}
public sealed record Guidance(Guid Id, string Title, string Segment, string Module, string Content,
    bool CustomerVisible, DateTimeOffset UpdatedAt, string Author, bool Archived = false, string Note = "");

public sealed class PanelStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public PanelStore(IConfiguration configuration, IHostEnvironment environment)
    {
        Root = Path.GetFullPath(configuration["Panel:DataDirectory"] ?? "App_Data", environment.ContentRootPath);
        var webRoot = Path.GetFullPath("wwwroot", environment.ContentRootPath);
        if (Root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || Root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("App_Data deve ficar fora da pasta pública.");
        Directory.CreateDirectory(Root);
    }
    public string JobDirectory(Guid id) => Path.Combine(Root, "runs", id.ToString("N"));
    public async Task<SourceSettings> GetSourceAsync(IConfiguration configuration) =>
        await ReadAsync<SourceSettings>("source.json") ?? new(configuration["GitHub:Repository"] ?? "evoluttechsistemas/sistema", configuration["GitHub:Reference"] ?? "");
    public async Task SaveSourceAsync(SourceSettings source)
    {
        GitHubSource.Validate(source);
        await WriteAsync("source.json", source);
    }
    public async Task<List<IndexRun>> GetRunsAsync()
    {
        var results = new List<IndexRun>();
        var dir = Path.Combine(Root, "runs");
        if (!Directory.Exists(dir)) return results;
        foreach (var path in Directory.EnumerateDirectories(dir).Select(folder => Path.Combine(folder, "status.json")))
            if (await ReadFileAsync<IndexRun>(path) is { } run) results.Add(run);
        return results.OrderByDescending(r => r.StartedAt).ToList();
    }
    public Task SaveRunAsync(IndexRun run) => WriteAsync(Path.Combine("runs", run.Id.ToString("N"), "status.json"), run);
    public async Task<List<Guidance>> GetGuidanceAsync() => await ReadAsync<List<Guidance>>("guidance.json") ?? [];
    public async Task SaveGuidanceAsync(GuidanceInput input, string author)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        await gate.WaitAsync();
        try
        {
            var entries = await GetGuidanceAsync();
            entries.Add(new(Guid.NewGuid(), input.Title.Trim(), input.Segment.Trim(), input.Module.Trim(),
                (input.Content ?? "").Trim(), input.CustomerVisible, DateTimeOffset.UtcNow, author, Note: (input.Note ?? "").Trim()));
            await WriteUnlockedAsync("guidance.json", entries);
        }
        finally { gate.Release(); }
    }
    public async Task ArchiveGuidanceAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var entries = await GetGuidanceAsync();
            var index = entries.FindIndex(g => g.Id == id);
            if (index >= 0) entries[index] = entries[index] with { Archived = true, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteUnlockedAsync("guidance.json", entries);
        }
        finally { gate.Release(); }
    }
    public Task<T?> ReadAsync<T>(string file) => ReadFileAsync<T>(Path.Combine(Root, file));
    private static async Task<T?> ReadFileAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<T>(stream);
    }
    public async Task WriteAsync<T>(string file, T value)
    {
        await gate.WaitAsync();
        try { await WriteUnlockedAsync(file, value); }
        finally { gate.Release(); }
    }
    private async Task WriteUnlockedAsync<T>(string file, T value)
    {
        var destination = Path.Combine(Root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, value, Json);
        File.Move(temporary, destination, overwrite: true);
    }
}
