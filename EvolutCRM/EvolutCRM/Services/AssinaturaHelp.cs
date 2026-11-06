using System.Security.Cryptography;
using System.Text;

namespace EvolutCRM.Services;

/// <summary>
/// Assinatura (HMAC) de links e pedidos que o navegador faz sem login de API.
/// Usada nos anexos/áudios/vídeos dos tickets (o link só abre com a assinatura certa, então não dá para
/// trocar o número e baixar arquivo de outro cliente) e no aviso de "fechei o HELP" (marcar-offline).
/// A chave é derivada do Jwt:Key do appsettings e nunca sai do servidor.
/// </summary>
public static class AssinaturaHelp
{
    private static byte[]? _chave;

    public static void Configurar(string segredo)
    {
        if (string.IsNullOrWhiteSpace(segredo)) throw new InvalidOperationException("Chave de assinatura vazia.");
        _chave = SHA256.HashData(Encoding.UTF8.GetBytes("help-assinatura-v1|" + segredo));
    }

    public static string Assinar(string finalidade, string valor)
    {
        if (_chave == null) throw new InvalidOperationException("AssinaturaHelp não configurada.");
        using var hmac = new HMACSHA256(_chave);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(finalidade + "|" + valor));
        return Convert.ToBase64String(hash, 0, 18).Replace('+', '-').Replace('/', '_');
    }

    public static bool Confere(string finalidade, string valor, string? assinatura)
    {
        if (string.IsNullOrWhiteSpace(assinatura) || _chave == null) return false;
        var esperado = Encoding.ASCII.GetBytes(Assinar(finalidade, valor));
        var recebido = Encoding.ASCII.GetBytes(assinatura.Trim());
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }

    // ── Anexos dos tickets ──
    public static string LinkAnexo(int codigo) => $"/api/tickets/anexo/{codigo}?s={Assinar("anexo", codigo.ToString())}";
    public static string LinkAudio(int codigo) => $"/api/tickets/audio/{codigo}?t={codigo}&s={Assinar("audio", codigo.ToString())}";
    public static string LinkVideo(int codigo) => $"/api/tickets/video/{codigo}?t={codigo}&s={Assinar("video", codigo.ToString())}";

    // ── Aviso de saída (marcar offline ao fechar o HELP) ──
    public static string AssinarSaida(string usuario, int codEmp) =>
        Assinar("offline", $"{(usuario ?? "").Trim().ToUpperInvariant()}|{codEmp}");
}
