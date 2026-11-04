using System.IO.Compression;
using System.Net;
using System.Text;
using EvolutTech.IA.Painel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var tests = new (string Name, Func<Task> Run)[] {
    ("HELP login respects company, active access, password hashes and session revocation", AuthenticationTests.Eligibility),
    ("Password-only HTTP login enforces antiforgery, cookies, logout and account revocation without SMTP", AuthenticationTests.HttpSession),
    ("Guidance persists without losing concurrent additions; archives preserve records", Persistence),
    ("Protected secrets round-trip without plaintext on disk", Secrets),
    ("GitHub resolves immutable commit and does not forward credentials to archive host", GitHub),
    ("Archive rejects traversal and inflated extraction limits", Archives),
    ("Invalid repository names are rejected", RepositoryValidation),
    ("Historical notes mask credential patterns and e-mail addresses", Masking),
    ("ERP versions use numeric components and handle older machines and invalid values", Versions),
    ("Responses API request is bounded, stateless and omits identifiers and secrets", SimulationTests.Provider),
    ("Missing key and empty retrieval never fabricate model responses", SimulationTests.Absence),
    ("Answer rejects fabricated citations and separates customer presentation", SimulationTests.Citations),
    ("Evidence ranking respects relevance and audience", SimulationTests.Ranking),
    ("Code evidence requires an unchanged indexed file hash", SimulationTests.CodeHash),
    ("External context masks secrets, personal identifiers and paths", SimulationTests.Redaction),
    ("Home renders while index initialization is still pending", StartupTests.HomeWithoutReady),
    ("Corrupted saved result does not terminate the index service or host", StartupTests.CorruptedResult),
    ("Download and extraction report actual byte and entry counts", ProgressTests.Transfer),
    ("Roslyn reports file and scope progress and progress persists", ProgressTests.Analysis),
    ("PDV and Consumer cannot share module-specific evidence", ModuleTests.Separation),
    ("Procedural code retrieval finds bodies and same-class call sites", ModuleTests.MethodBodies),
    ("Customer parameter guidance preserves names and hands adjustment to support", ModuleTests.ParameterHandoff),
    ("Explanation progresses from text to flow to image and resets on a new topic", ExplanationTests.Progression),
    ("Screen illustrations preserve the original capture, match context and require evidence", ExplanationTests.EvidenceAndImage),
    ("Designer reads only static layout and support always asks for resolution", DesignerTests.LayoutAndClosure),
    ("Interface retrieval resolves menu paths, shortcuts, footers and safe RESX strings", InterfaceTests.MenusAndResources),
    ("Similar finalized cases require approved solutions and corroborating evidence", ResolvedCaseTests.Corroboration),
    ("Product screen and code parameter survive a topic change from PDV", ProductAndVideoTests.Product),
    ("Function screen resources and verified YouTube recommendations are contextual", ProductAndVideoTests.ResourcesAndVideos),
    ("Configuration searches inspect contents before shortlisting and complete menu and parameter group", ConfigurationSearchTests.MenuAndConfiguration),
    ("Screen reconstruction resolves verified menu targets without a per-feature image registry", ScreenSceneTests.Discovery)
};
var failures = 0;
foreach (var test in tests) {
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} testes do painel aprovados.");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
static (PanelStore Store, IConfiguration Config, SecretStore Secrets) Setup()
{
    var environment = new TestEnvironment { ContentRootPath = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")) };
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Panel:DataDirectory"] = "data", ["GitHub:Repository"] = "evoluttechsistemas/sistema" }).Build();
    var store = new PanelStore(config, environment);
    return (store, config, new SecretStore(store, new EphemeralDataProtectionProvider()));
}
static async Task Persistence()
{
    var (store, _, _) = Setup();
    await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.SaveGuidanceAsync(new() { Title = $"Orientação {i}", Segment = "Vestuário", Module = "Vendas", Content = "Procedimento validado." }, "Teste")));
    var entries = await store.GetGuidanceAsync();
    Check(entries.Count == 12 && entries.All(g => !g.CustomerVisible && g.Author == "Teste"));
    await store.ArchiveGuidanceAsync(entries[0].Id);
    Check((await store.GetGuidanceAsync()).Count == 12 && (await store.GetGuidanceAsync()).Count(g => g.Archived) == 1);
    await store.SaveSourceAsync(new("evoluttechsistemas/sistema", "release/production"));
    Check((await store.GetSourceAsync(new ConfigurationBuilder().Build())).Reference == "release/production");
}
static async Task Secrets()
{
    var (store, _, secrets) = Setup();
    const string marker = "test-only-secret-marker";
    await secrets.SaveSqlAsync($"Server=example.invalid;Database=Fixture;User Id=fixture;Password={marker};TrustServerCertificate=True");
    Check(secrets.HasSql && (await secrets.GetSqlAsync())!.Contains(marker));
    Check(!File.ReadAllText(Path.Combine(store.Root, "secrets", "sql.protected")).Contains(marker));
    await secrets.SaveGitHubAsync(marker);
    Check((await secrets.GetGitHubAsync()) == marker && !File.ReadAllText(Path.Combine(store.Root, "secrets", "github.protected")).Contains(marker));
}
static byte[] Archive(params (string Name, string Text)[] files)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open(), Encoding.UTF8); writer.Write(file.Text); }
    return stream.ToArray();
}
static async Task GitHub()
{
    var (store, config, secrets) = Setup();
    await secrets.SaveGitHubAsync("test-only-token-marker");
    var handler = new GitHubHandler(Archive(("repo-sha/App.cs", "class Real {}"), ("repo-sha/bin/ignored.cs", "class Ignore {}"), ("repo-sha/README.md", "ignored")));
    var source = new GitHubSource(new HttpClient(handler), config, secrets);
    var commit = await source.DownloadAsync(new("evoluttechsistemas/sistema", "release/prod"), store.JobDirectory(Guid.NewGuid()), default);
    Check(commit == GitHubHandler.Sha && handler.Calls == 3 && handler.ArchiveHadNoCredential && handler.ApiHadCredential && handler.BranchEncoded);
    Check(Directory.GetFiles(store.Root, "*.cs", SearchOption.AllDirectories).Length == 1);
}
static async Task Archives()
{
    var (store, _, _) = Setup();
    var path = Path.Combine(store.Root, "unsafe.zip");
    await File.WriteAllBytesAsync(path, Archive(("../escape.cs", "class Escape {}")));
    try { await GitHubSource.ExtractSourcesAsync(path, Path.Combine(store.Root,"unsafe"), 1000, 10, default); throw new Exception(); }
    catch (InvalidDataException) { }
    Check(!File.Exists(Path.Combine(store.Root,"escape.cs")));
    var large = Path.Combine(store.Root,"large.zip");
    await File.WriteAllBytesAsync(large, Archive(("repo/Large.cs", new string('x', 10000))));
    try { await GitHubSource.ExtractSourcesAsync(large, Path.Combine(store.Root,"large"), 100, 10, default); throw new Exception(); }
    catch (InvalidDataException) { }
}
static Task RepositoryValidation()
{
    foreach (var repository in new[] { "https://evil.invalid/repo", "../repo", "owner/repo/other", "owner/repo?token=x" })
        try { GitHubSource.Validate(new(repository,"")); throw new Exception(); } catch (ArgumentException) { }
    return Task.CompletedTask;
}
static Task Masking()
{
    var redacted = SensitiveText.Redact("Senha=private-marker; Token: token-marker\nContato fixture@example.invalid\nBearer bearer-marker");
    Check(!redacted.Contains("private-marker") && !redacted.Contains("token-marker") && !redacted.Contains("fixture@example.invalid") && !redacted.Contains("bearer-marker"));
    Check(SensitiveText.Redact("Problema resolvido ao conferir a configuração.") == "Problema resolvido ao conferir a configuração.");
    return Task.CompletedTask;
}
static Task Versions()
{
    Check(ErpVersion.Latest(["26.9.30", "26.10.06", "25.12.31"]) == "26.10.06");
    Check(ErpVersion.Latest(["26.10.9", "26.10.10", "26.10.06"]) == "26.10.10");
    Check(ErpVersion.Latest(["26.10.06", "26.10.06.1"]) == "26.10.06.1");
    Check(ErpVersion.Latest([null, "", "  ", "desconhecida", "26..10", "-26.10", "26.10.beta"]) == "");
    Check(ErpVersion.Latest([" 26.10.06 ", "26.10.6.0"]) == "26.10.06");
    Check(ErpVersion.Latest(["26.999999999999999999999999999999999999", "26.10"]) == "26.999999999999999999999999999999999999");
    return Task.CompletedTask;
}
sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Tests";
    public string ApplicationName { get; set; } = "AtendimentoIA.Tests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class GitHubHandler(byte[] archive) : HttpMessageHandler
{
    public const string Sha = "1234567890abcdef1234567890abcdef12345678";
    public int Calls { get; private set; }
    public bool ArchiveHadNoCredential { get; private set; }
    public bool ApiHadCredential { get; private set; }
    public bool BranchEncoded { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (request.RequestUri!.Host == "codeload.github.com") {
            ArchiveHadNoCredential = request.Headers.Authorization is null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
        ApiHadCredential |= request.Headers.Authorization?.Parameter == "test-only-token-marker";
        if (request.RequestUri.AbsolutePath.Contains("commits")) {
            BranchEncoded = request.RequestUri.AbsoluteUri.Contains("release%2Fprod", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"sha\":\"{Sha}\"}}") });
        }
        CheckCommit(request.RequestUri.AbsolutePath.EndsWith(Sha));
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("https://codeload.github.com/fixture/archive");
        return Task.FromResult(redirect);
    }
    private static void CheckCommit(bool valid) { if (!valid) throw new InvalidOperationException(); }
}
