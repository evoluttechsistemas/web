using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EvolutTech.IA.Core;

namespace EvolutTech.IA.Painel.Services;

public sealed class GitHubSource(HttpClient client, IConfiguration configuration, SecretStore secrets)
{
    public static void Validate(SourceSettings settings)
    {
        if (!Regex.IsMatch(settings.Repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z") ||
            settings.Repository.Split('/').Any(s => s is "." or "..") || settings.Reference.Length > 200 ||
            settings.Reference.Any(char.IsControl)) throw new ArgumentException("Informe um repositório no formato organização/nome e uma referência válida.");
    }
    /// <summary>Commit atual da referência configurada (usado pela indexação automática para saber se mudou algo).</summary>
    public async Task<string> LatestCommitAsync(SourceSettings settings, CancellationToken token)
    {
        Validate(settings);
        var reference = string.IsNullOrWhiteSpace(settings.Reference) ? "HEAD" : settings.Reference;
        using var commitResponse = await SendApiAsync($"repos/{settings.Repository}/commits/{Uri.EscapeDataString(reference)}", token);
        await using var commitStream = await commitResponse.Content.ReadAsStreamAsync(token);
        using var commitJson = await JsonDocument.ParseAsync(commitStream, cancellationToken: token);
        var commit = commitJson.RootElement.GetProperty("sha").GetString()!;
        if (!Regex.IsMatch(commit, @"\A[0-9a-fA-F]{40,64}\z")) throw new InvalidDataException("Commit inválido recebido do GitHub.");
        return commit;
    }
    public async Task<string> DownloadAsync(SourceSettings settings, string jobDirectory, CancellationToken token,
        IProgress<ProcessingProgress>? progress = null)
    {
        Validate(settings);
        progress?.Report(new("resolve", 0, null, "etapas"));
        // Resolve a branch once, then download that immutable commit. No Git hooks or source execution.
        var commit = await LatestCommitAsync(settings, token);
        using var response = await SendApiAsync($"repos/{settings.Repository}/zipball/{commit}", token, allowRedirect: true);
        HttpResponseMessage? redirected = null;
        try
        {
            var download = response;
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || !location.IsAbsoluteUri || location.Scheme != "https" || location.Host != "codeload.github.com")
                    throw new InvalidDataException("Redirecionamento inesperado do GitHub.");
                // Never forward the GitHub token to another host.
                redirected = await client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token);
                if (!redirected.IsSuccessStatusCode) throw new HttpRequestException("Falha no download do arquivo do GitHub.");
                download = redirected;
            }
            Directory.CreateDirectory(jobDirectory);
            var archivePath = Path.Combine(jobDirectory, "source.zip");
            var totalBytes = download.Content.Headers.ContentLength;
            progress?.Report(new("download", 0, totalBytes, "bytes"));
            await using (var incoming = await download.Content.ReadAsStreamAsync(token))
            await using (var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
                await CopyLimitedAsync(incoming, target, configuration.GetValue<long>("GitHub:MaxArchiveBytes", 268435456), token,
                    count => progress?.Report(new("download", count, totalBytes, "bytes")));
            await ExtractSourcesAsync(archivePath, Path.Combine(jobDirectory, "source"),
                configuration.GetValue<long>("GitHub:MaxSourceBytes", 536870912),
                configuration.GetValue<int>("GitHub:MaxEntries", 100000), token, progress,
                configuration.GetValue<bool>("Indexing:IncludeResources"));
            return commit;
        }
        finally { redirected?.Dispose(); }
    }
    private async Task<HttpResponseMessage> SendApiAsync(string path, CancellationToken token, bool allowRedirect = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/" + path));
        request.Headers.UserAgent.ParseAdd("EvolutTech-IA/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if ((await secrets.GetGitHubAsync() ?? configuration["GitHub:Token"]) is { Length: > 0 } secret)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode && !(allowRedirect && (int)response.StatusCode is >= 300 and < 400))
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"GitHub retornou HTTP {status}. Verifique acesso, referência e credencial no servidor.");
        }
        return response;
    }
    public static async Task ExtractSourcesAsync(string archivePath, string destination, long maxBytes,
        int maxEntries, CancellationToken token, IProgress<ProcessingProgress>? progress = null, bool includeResources = false)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (Directory.Exists(destination)) throw new IOException("O destino da extração já existe.");
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > maxEntries) throw new InvalidDataException("Arquivo excede o limite de entradas.");
        long total = 0;
        progress?.Report(new("extract", 0, zip.Entries.Count, "entradas do arquivo"));
        for (var entryIndex = 0; entryIndex < zip.Entries.Count; entryIndex++)
        {
            var entry = zip.Entries[entryIndex];
            if (entryIndex % 100 == 0) progress?.Report(new("extract", entryIndex, zip.Entries.Count, "entradas do arquivo"));
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (name.Split('/').Any(p => p is "." or ".." || p.Contains(':')) || name.StartsWith('/'))
                throw new InvalidDataException("Caminho inseguro no arquivo recebido.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) continue; // Unix symlink
            if (name.EndsWith('/')) continue;
            var parts = name.Split('/');
            if (parts.Any(p => new[] { "bin", "obj", ".git", ".vs", "packages", "node_modules" }.Contains(p, StringComparer.OrdinalIgnoreCase))) continue;
            var extension = Path.GetExtension(name);
            var isResource = extension.Equals(".resx", StringComparison.OrdinalIgnoreCase);
            if (!new[] { ".cs", ".csproj", ".sln" }.Contains(extension, StringComparer.OrdinalIgnoreCase) && !(includeResources && isResource)) continue;
            total = checked(total + entry.Length);
            if (total > maxBytes) throw new InvalidDataException("Fontes excedem o limite de extração.");
            var path = Path.GetFullPath(Path.Combine(destination, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho fora da área de indexação.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await CopyLimitedAsync(input, output, entry.Length, token);
        }
        progress?.Report(new("extract", zip.Entries.Count, zip.Entries.Count, "entradas do arquivo"));
    }
    private static async Task CopyLimitedAsync(Stream input, Stream output, long maximum, CancellationToken token,
        Action<long>? report = null)
    {
        var buffer = new byte[81920];
        long total = 0;
        long reported = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            total = checked(total + count);
            if (total > maximum) throw new InvalidDataException("Download ou extração excede o limite configurado.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            if (total - reported >= 1024 * 1024) { report?.Invoke(total); reported = total; }
        }
        report?.Invoke(total);
    }
}
