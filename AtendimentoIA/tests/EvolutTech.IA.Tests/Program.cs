using System.Security.Cryptography;
using EvolutTech.IA.Core;
using EvolutTech.IA.Indexador;

// Self-contained regression runner: failure returns nonzero; no external test framework required.
var tests = new (string Name, Func<Task> Run)[]
{
    ("Partial Forms, inheritance, aliases, methods and real events", Forms),
    ("Missing dependencies preserve syntax candidates and diagnostics", MissingReferences),
    ("Broken source does not stop healthy files", BrokenSource),
    ("Preprocessor symbols control active declarations", Defines),
    ("Duplicate names across projects stay separate", ProjectScopes),
    ("Source bytes remain unchanged and output folders are ignored", ReadOnly),
    ("Event expressions do not expose embedded literals", SafeEventExpressions),
    ("Cancellation and invalid path are explicit", InvalidInput)
};
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} testes aprovados.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); }
static async Task WithFixture(Func<string, Task> action)
{
    var root = Path.Combine(Path.GetTempPath(), "EvolutTech.IA.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    // Preserve fixtures in the temp directory for inspection; never delete an arbitrary path.
    await action(root);
}
static Task Put(string root, string relative, string content)
{
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    return File.WriteAllTextAsync(path, content);
}
static Task<AnalysisResult> Analyze(string root, string? references = null, string[]? defines = null) =>
    new SourceAnalyzer().AnalyzeAsync(new(root, references, defines));

static Task Forms() => WithFixture(async root =>
{
    await Put(root, "Product.cs", """
        using System;
        using System.Windows.Forms;
        namespace ERP;
        public partial class Product : Form {
          public Product() { InitializeComponent(); }
          void Save(object sender, EventArgs e) {}
          void Save(int value) {}
          class Nested { void NestedMethod() {} }
        }
        class Derived : Product {}
        class NotAForm { int Total; void Add() { Total += 1; } }
        """);
    await Put(root, "Product.Designer.cs", """
        using System; using System.Windows.Forms;
        namespace ERP;
        public partial class Product {
          Button save;
          void InitializeComponent() {
            save = new Button();
            save.Click += new EventHandler(Save);
            this.Load += (s,e) => {};
          }
        }
        """);
    await Put(root, "Alias.cs", "using F = System.Windows.Forms.Form; namespace ERP; class Alias : F {}");
    var result = await Analyze(root);
    Check(result.Files.Count == 3, "file count");
    Check(result.Classes.Count == 5, "partial merge / nested class count");
    Check(result.MethodCount == 5 && result.ConstructorCount == 1, "method / constructor count");
    Check(result.Forms.Count(f => f.FormEvidence == "semantic-inheritance") == 3, "real reference assemblies required / forms");
    Check(!result.Issues.Any(i => i.Code is "CS0009" or "CS1509"), "native DLLs are not assembly references");
    var form = result.Classes.Single(c => c.Name == "global::ERP.Product");
    Check(form.Sources.Count == 2, "partial provenance");
    Check(form.Events.Count == 2 && form.Events.All(e => e.Evidence == "semantic-event"), "semantic event binding");
    Check(form.Events.Any(e => e.Handler == "Save") && form.Events.Any(e => e.Handler == "<anonymous>"), "handlers");
    Check(result.Classes.Single(c => c.Name == "global::ERP.NotAForm").Events.Count == 0, "numeric += is not an event");
});
static Task MissingReferences() => WithFixture(async root =>
{
    await Put(root, "Form.cs", "using System.Windows.Forms; class Screen : Form { void Bind() { unknown.Click += Handler; } }");
    var result = await Analyze(root, Path.Combine(root, "missing"));
    Check(result.Forms.Single().FormEvidence == "syntax-candidate", "no invented semantic relation");
    Check(result.Forms.Single().Events.Single().Evidence == "syntax-candidate", "unresolved event candidate");
    Check(result.Issues.Any(i => i.Kind == "references") && result.Issues.Any(i => i.Kind == "semantic"), "limitations recorded");
});
static Task BrokenSource() => WithFixture(async root =>
{
    await Put(root, "Bad.cs", "class Bad { void Broken( ");
    await Put(root, "Good.cs", "class Good { void Work() {} }");
    var result = await Analyze(root);
    Check(result.Files.Count == 2 && result.Classes.Any(c => c.Name == "global::Good"), "continues analysis");
    Check(result.Issues.Any(i => i.Kind == "syntax" && i.Line > 0), "syntax diagnostic with source");
});
static Task Defines() => WithFixture(async root =>
{
    await Put(root, "Conditional.cs", "#if FEATURE\nclass Active {}\n#else\nclass Default {}\n#endif");
    var result = await Analyze(root, defines: ["FEATURE"]);
    Check(result.Classes.Single().Name == "global::Active", "active branch");
});
static Task ProjectScopes() => WithFixture(async root =>
{
    foreach (var project in new[] { "A", "B" })
    {
        await Put(root, $"{project}/App.csproj", "<Project />");
        await Put(root, $"{project}/Class.cs", "partial class Same { void Work() {} }");
    }
    var result = await Analyze(root);
    Check(result.Classes.Count == 2 && result.Classes.Select(c => c.Scope).Distinct().Count() == 2, "project isolation");
});
static Task ReadOnly() => WithFixture(async root =>
{
    await Put(root, "Class.cs", "class Safe { void Work() {} }");
    await Put(root, "bin/Ignore.cs", "class Ignore {}");
    await Put(root, "obj/Ignore.cs", "class Ignore {}");
    var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "Class.cs"));
    var result = await Analyze(root);
    Check(result.Files.Count == 1, "excluded directories");
    Check(result.Files[0].Sha256 == Convert.ToHexString(SHA256.HashData(bytes)), "hash");
    Check(bytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(root, "Class.cs"))), "source unchanged");
});
static Task InvalidInput() => WithFixture(async root =>
{
    try { await Analyze(Path.Combine(root, "missing")); throw new Exception("missing path accepted"); }
    catch (DirectoryNotFoundException) {}
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await new SourceAnalyzer().AnalyzeAsync(new(root), cancellationToken: cts.Token); throw new Exception("cancellation ignored"); }
    catch (OperationCanceledException) {}
});

static Task SafeEventExpressions() => WithFixture(async root =>
{
    await Put(root, "Events.cs", """
        class Screen {
          void Bind() { Factory("secret-marker").Click += Factory("private-marker").Handler; }
        }
        """);
    var result = await Analyze(root);
    var ev = result.Classes.Single().Events.Single();
    Check(ev.Target == "<expression>.Click" && ev.Handler == "<expression>.Handler", "literals must be omitted");
});
