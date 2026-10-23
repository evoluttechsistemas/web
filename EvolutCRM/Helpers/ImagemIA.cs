using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace EvolutCRM.Helpers;

/// <summary>Prepara imagens (prints do ticket) para mandar à IA: reduz e converte para JPEG em data URL.</summary>
public static class ImagemIA
{
    private static readonly string[] Extensoes = { "", ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    /// <summary>Pelo nome do arquivo, é imagem? (sem nome = imagem de WhatsApp)</summary>
    public static bool PareceImagem(string? nomeArquivo) =>
        Extensoes.Contains(Path.GetExtension(nomeArquivo ?? "").ToLowerInvariant());

    /// <summary>Converte os bytes em "data:image/jpeg;base64,...", com no máximo <paramref name="max"/> px. Null se não for imagem.</summary>
    public static async Task<string?> ParaDataUrlAsync(byte[]? bytes, int max = 1600, CancellationToken ct = default)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using var img = Image.Load(bytes);
            if (img.Width > max || img.Height > max)
                img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(max, max), Mode = ResizeMode.Max }));

            using var ms = new MemoryStream();
            await img.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 }, ct);
            return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null; // PDF, planilha ou arquivo corrompido
        }
    }
}
