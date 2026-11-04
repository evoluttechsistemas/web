using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services;

public class ClienteWhatsAppFotoService
{
    private readonly string _conn;
    private readonly IConfiguration _cfg;

    public ClienteWhatsAppFotoService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("Connection") ?? "";
        _cfg = cfg;
    }

    private string PastaFisica =>
        _cfg["FotoPerfilWhatsApp:PastaFisica"] ?? @"C:\FTP\Site\FotoPerfil";

    private string UrlBase =>
        _cfg["FotoPerfilWhatsApp:UrlBase"] ?? "/api/whatsapp/cliente-foto/arquivo";

    public async Task<string> SalvarFotoAsync(int codEmp, string telefone, string jid, string fotoBase64, string contentType)
    {
        telefone = LimparTelefone(telefone);
        jid = (jid ?? "").Trim();

        if (codEmp <= 0)
            throw new ArgumentException("CodEmp invalido.");

        if (string.IsNullOrWhiteSpace(telefone))
            throw new ArgumentException("Telefone invalido.");

        if (string.IsNullOrWhiteSpace(fotoBase64))
            throw new ArgumentException("FotoBase64 invalida.");

        var bytes = Convert.FromBase64String(RemoverPrefixoBase64(fotoBase64));
        var extensao = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";

        var pastaEmpresa = Path.Combine(PastaFisica, codEmp.ToString());
        Directory.CreateDirectory(pastaEmpresa);

        var nomeArquivo = $"{telefone}{extensao}";
        var caminhoFisico = Path.Combine(pastaEmpresa, nomeArquivo);

        await File.WriteAllBytesAsync(caminhoFisico, bytes);

        var fotoUrl = $"{UrlBase}?codEmp={codEmp}&telefone={telefone}";

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM ClienteWhatsAppFoto WHERE CodEmp = @CodEmp AND Telefone = @Telefone)
BEGIN
    UPDATE ClienteWhatsAppFoto
    SET Jid = @Jid,
        FotoUrl = @FotoUrl,
        CaminhoFisico = @CaminhoFisico,
        DataAtualizacao = GETDATE(),
        UsuarioUltimaGravacao = 'BAILEYS',
        DataHoraUltimaGravacao = GETDATE()
    WHERE CodEmp = @CodEmp
      AND Telefone = @Telefone;
END
ELSE
BEGIN
    INSERT INTO ClienteWhatsAppFoto
        (CodEmp, Telefone, Jid, FotoUrl, CaminhoFisico, DataAtualizacao, UsuarioUltimaGravacao, DataHoraUltimaGravacao)
    VALUES
        (@CodEmp, @Telefone, @Jid, @FotoUrl, @CaminhoFisico, GETDATE(), 'BAILEYS', GETDATE());
END", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
        cmd.Parameters.Add("@Telefone", SqlDbType.VarChar, 20).Value = telefone;
        cmd.Parameters.Add("@Jid", SqlDbType.VarChar, 100).Value = string.IsNullOrWhiteSpace(jid) ? DBNull.Value : jid;
        cmd.Parameters.Add("@FotoUrl", SqlDbType.VarChar, 500).Value = fotoUrl;
        cmd.Parameters.Add("@CaminhoFisico", SqlDbType.VarChar, 500).Value = caminhoFisico;

        await cmd.ExecuteNonQueryAsync();

        return fotoUrl;
    }

    public async Task<bool> PrecisaAtualizarFotoAsync(int codEmp, string telefone, int diasCache = 7)
    {
        telefone = LimparTelefone(telefone);

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM ClienteWhatsAppFoto
        WHERE CodEmp = @CodEmp
          AND Telefone = @Telefone
          AND FotoUrl IS NOT NULL
          AND DataAtualizacao >= DATEADD(DAY, -@DiasCache, GETDATE())
    )
    THEN 1 ELSE 0 END", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
        cmd.Parameters.Add("@Telefone", SqlDbType.VarChar, 20).Value = telefone;
        cmd.Parameters.Add("@DiasCache", SqlDbType.Int).Value = diasCache;

        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    public async Task<(byte[] Bytes, string ContentType)?> ObterArquivoAsync(int codEmp, string telefone)
    {
        telefone = LimparTelefone(telefone);

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT TOP 1 CaminhoFisico
FROM ClienteWhatsAppFoto
WHERE CodEmp = @CodEmp
  AND Telefone = @Telefone
  AND CaminhoFisico IS NOT NULL", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
        cmd.Parameters.Add("@Telefone", SqlDbType.VarChar, 20).Value = telefone;

        var caminho = (await cmd.ExecuteScalarAsync())?.ToString();

        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            return null;

        var bytes = await File.ReadAllBytesAsync(caminho);
        var contentType = caminho.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        return (bytes, contentType);
    }

    private static string LimparTelefone(string? telefone)
    {
        return new string((telefone ?? "").Where(char.IsDigit).ToArray());
    }

    private static string RemoverPrefixoBase64(string base64)
    {
        var idx = base64.IndexOf(',');
        return idx >= 0 ? base64[(idx + 1)..] : base64;
    }
}