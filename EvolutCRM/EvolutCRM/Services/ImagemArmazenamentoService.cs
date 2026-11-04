using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Services;

/// <summary>
/// Grava as imagens de documentações e do blog em arquivo, dentro de {Uploads:PastaFisica}
/// (servida em /uploads), para que o banco guarde só o caminho.
///
/// • O nome do arquivo é o hash do conteúdo: a mesma imagem nunca é gravada duas vezes
///   e o arquivo nunca muda (cache de 1 ano no navegador e backup simples).
/// • Só grava arquivo em PRODUÇÃO. Rodando no computador de desenvolvimento (que usa o
///   mesmo banco), as imagens continuam embutidas em base64 — senão o banco de produção
///   apontaria para arquivos que só existem no PC. O servidor converte depois
///   (MigracaoImagensService). Para forçar em dev: "Uploads:GravarArquivoEmDev": true.
/// </summary>
public class ImagemArmazenamentoService
{
    public const long MaxImagemBytes = 8 * 1024 * 1024;

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public ImagemArmazenamentoService(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    /// <summary>True quando as imagens vão para arquivo (servidor de produção).</summary>
    public bool GravaEmArquivo =>
        _env.IsProduction() || _config.GetValue<bool>("Uploads:GravarArquivoEmDev");

    private string PastaUploads => _config["Uploads:PastaFisica"] ?? @"C:\FTP\Site\Help\uploads";

    /// <summary>Endereço público do HELP (para URLs absolutas usadas pelo site).</summary>
    public string UrlBase => (_config["App:BaseUrl"] ?? "").TrimEnd('/');

    // data:image/png;base64,....  — vale para atributo HTML, CSS e texto JSON
    // (no JSON o '+' pode vir como \u002B e a '/' como \/).
    private static readonly Regex RegexDataUri = new(
        @"data:(?<mime>image/[a-z0-9.+-]+);base64,(?<b64>(?:[A-Za-z0-9+/=]|\\u002[Bb]|\\u003[Dd]|\\/|\s)+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? ExtensaoDoMime(string? mime) => (mime ?? "").ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => null // SVG e outros ficam de fora (SVG pode conter script)
    };

    /// <summary>
    /// Grava a imagem em /uploads/{subpasta}/ e devolve a URL (com <paramref name="prefixoUrl"/> na frente, se informado).
    /// Fora de produção devolve a própria imagem em data URI (base64), como antes.
    /// </summary>
    public async Task<string> SalvarAsync(byte[] dados, string mime, string subpasta, string? prefixoUrl = null)
    {
        if (dados.Length == 0) throw new InvalidOperationException("Imagem vazia.");
        if (dados.Length > MaxImagemBytes) throw new InvalidOperationException("Imagem muito grande (máx 8 MB).");
        var ext = ExtensaoDoMime(mime)
                  ?? throw new InvalidOperationException("Use imagem em PNG, JPG, GIF, WebP ou BMP.");

        if (!GravaEmArquivo)
            return $"data:{mime.ToLowerInvariant()};base64,{Convert.ToBase64String(dados)}";

        subpasta = subpasta.Replace('\\', '/').Trim('/');
        if (subpasta.Contains("..")) throw new InvalidOperationException("Pasta inválida.");

        // 32 caracteres do SHA-256 (128 bits): único na prática e cabe em colunas pequenas
        var nome = Convert.ToHexString(SHA256.HashData(dados)).ToLowerInvariant()[..32] + ext;
        var pasta = Path.Combine(PastaUploads, subpasta.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, nome);

        if (!File.Exists(caminho))
        {
            // grava num temporário e renomeia: ninguém lê o arquivo pela metade
            var temp = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, dados);
            try { File.Move(temp, caminho); }
            catch (IOException) { File.Delete(temp); } // outra requisição gravou a mesma imagem
        }

        return $"{prefixoUrl}/uploads/{subpasta}/{nome}";
    }

    /// <summary>
    /// Troca cada imagem em base64 (data:image/...) do texto por um arquivo em /uploads/{subpasta}/.
    /// Funciona em HTML e em JSON. Fora de produção não altera nada.
    /// </summary>
    public async Task<(string Texto, bool Alterado)> ExtrairBase64Async(string? texto, string subpasta, string? prefixoUrl = null)
    {
        if (string.IsNullOrEmpty(texto) || !GravaEmArquivo
            || texto.IndexOf("data:image", StringComparison.OrdinalIgnoreCase) < 0)
            return (texto ?? "", false);

        var sb = new StringBuilder(texto.Length / 4);
        var ultimo = 0;
        var alterado = false;

        foreach (Match m in RegexDataUri.Matches(texto))
        {
            string? url = null;
            try
            {
                var mime = m.Groups["mime"].Value;
                if (ExtensaoDoMime(mime) != null)
                {
                    var b64 = m.Groups["b64"].Value
                        .Replace("\\u002B", "+").Replace("\\u002b", "+")
                        .Replace("\\u003D", "=").Replace("\\u003d", "=")
                        .Replace("\\/", "/");
                    if (b64.Any(char.IsWhiteSpace))
                        b64 = new string(b64.Where(c => !char.IsWhiteSpace(c)).ToArray());
                    url = await SalvarAsync(Convert.FromBase64String(b64), mime, subpasta, prefixoUrl);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[IMAGEM] Base64 mantido ({subpasta}): {ex.Message}");
            }

            if (url == null) continue; // mantém como estava

            // espaços/quebras depois do base64 não fazem parte da imagem: devolve ao texto
            var bruto = m.Groups["b64"].Value;
            var sobra = bruto[bruto.TrimEnd().Length..];

            sb.Append(texto, ultimo, m.Index - ultimo);
            sb.Append(url).Append(sobra);
            ultimo = m.Index + m.Length;
            alterado = true;
        }

        if (!alterado) return (texto, false);
        sb.Append(texto, ultimo, texto.Length - ultimo);
        return (sb.ToString(), true);
    }
}
