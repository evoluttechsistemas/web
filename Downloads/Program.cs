using Downloads.Components;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

const string downloadsDirectory = "C:/FTP/Downloads";
var contentTypeProvider = new FileExtensionContentTypeProvider();

IResult DownloadFile(string requestedPath)
{
    var rootPath = Path.GetFullPath(downloadsDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    var relativePath = Uri.UnescapeDataString(requestedPath ?? string.Empty)
        .Replace('/', Path.DirectorySeparatorChar)
        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

    // Impede que caminhos como ../../ acessem arquivos fora de Downloads.
    if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ||
        !System.IO.File.Exists(fullPath))
    {
        return Results.NotFound("Arquivo não encontrado.");
    }

    if (!contentTypeProvider.TryGetContentType(fullPath, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    var fileInfo = new FileInfo(fullPath);
    var stream = new FileStream(
        fullPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite,
        bufferSize: 1024 * 1024,
        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    return Results.File(
        stream,
        contentType,
        fileDownloadName: fileInfo.Name,
        lastModified: fileInfo.LastWriteTimeUtc,
        enableRangeProcessing: true);
}

// Compatível com links no formato: /downloads/pasta/arquivo.zip
app.MapGet("/downloads/{**filePath}", (string filePath) =>
    DownloadFile(filePath));

// Compatível com a nova página: /api/downloads?path=pasta%2Farquivo.zip
app.MapGet("/api/downloads", (string path) =>
    DownloadFile(path));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
