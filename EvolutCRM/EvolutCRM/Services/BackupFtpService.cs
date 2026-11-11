using Dapper;
using FluentFTP;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Lista e baixa os backups que os clientes enviam para o FTP (pasta BackupClientes).
/// Host, porta, usuário e senha ficam no appsettings (BackupFtp) e nunca vão para o navegador.
/// O download passa pelo HELP: o navegador recebe um link assinado e com validade, nunca a senha do FTP.
/// </summary>
public sealed class BackupFtpService(IConfiguration config)
{
    private string Host => (config["BackupFtp:Host"] ?? "").Trim();
    private int Porta => config.GetValue<int?>("BackupFtp:Porta") ?? 21;
    private string Usuario => config["BackupFtp:Usuario"] ?? "";
    private string Senha => config["BackupFtp:Senha"] ?? "";
    private string PastaRaiz => "/" + (config["BackupFtp:Pasta"] ?? "BackupClientes").Trim().Trim('/');

    public bool Configurado => Host.Length > 0 && Usuario.Length > 0 && Senha.Length > 0;

    public static readonly TimeSpan ValidadeLink = TimeSpan.FromHours(2);

    private AsyncFtpClient NovoCliente()
    {
        var c = new AsyncFtpClient(Host, Usuario, Senha, Porta);
        c.Config.ConnectTimeout = 15000;
        c.Config.ReadTimeout = 30000;
        c.Config.DataConnectionConnectTimeout = 15000;
        c.Config.DataConnectionReadTimeout = 120000;
        return c;
    }

    /// <summary>Caminho relativo à pasta de backups, sem "..", sem barra invertida. "" = raiz.</summary>
    public static string NormalizarRelativo(string? caminho)
    {
        var partes = (caminho ?? "").Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Any(p => p == "." || p == ".."))
            throw new InvalidOperationException("Caminho inválido.");
        return string.Join('/', partes);
    }

    private string Completo(string relativo) =>
        relativo.Length == 0 ? PastaRaiz : PastaRaiz + "/" + relativo;

    public async Task<List<BackupFtpItem>> ListarAsync(string? pasta, CancellationToken ct = default)
    {
        if (!Configurado) throw new InvalidOperationException("FTP de backups não configurado (appsettings: BackupFtp).");
        var rel = NormalizarRelativo(pasta);
        using var ftp = NovoCliente();
        await ftp.Connect(ct);
        var itens = await ftp.GetListing(Completo(rel), FtpListOption.Auto, ct);
        return itens
            .Where(i => i.Type is FtpObjectType.File or FtpObjectType.Directory && i.Name is not ("." or ".."))
            .Select(i => new BackupFtpItem
            {
                Nome = i.Name,
                Caminho = rel.Length == 0 ? i.Name : rel + "/" + i.Name,
                Pasta = i.Type == FtpObjectType.Directory,
                Tamanho = i.Type == FtpObjectType.File ? i.Size : 0,
                Modificado = i.Modified == DateTime.MinValue ? null : i.Modified
            })
            .ToList();
    }

    // ── Link de download assinado ──
    // Assina caminho + validade + usuário: não dá para trocar o arquivo nem reaproveitar o link depois que vence.
    public static string LinkDownload(string caminhoRelativo, string usuario)
    {
        var rel = NormalizarRelativo(caminhoRelativo);
        var exp = DateTimeOffset.UtcNow.Add(ValidadeLink).ToUnixTimeSeconds();
        var u = (usuario ?? "").Trim().ToUpperInvariant();
        var s = AssinaturaHelp.Assinar("backupftp", $"{rel}|{exp}|{u}");
        return $"/api/backups-clientes/download?p={Uri.EscapeDataString(rel)}&e={exp}&u={Uri.EscapeDataString(u)}&s={s}";
    }

    public static bool LinkValido(string? p, long e, string? u, string? s) =>
        !string.IsNullOrWhiteSpace(p) && e >= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        && AssinaturaHelp.Confere("backupftp", $"{p}|{e}|{(u ?? "").Trim().ToUpperInvariant()}", s);

    /// <summary>Copia o arquivo do FTP direto para a resposta HTTP, sem guardar no servidor do HELP.</summary>
    public async Task BaixarParaAsync(string caminhoRelativo, HttpResponse resposta, CancellationToken ct)
    {
        var rel = NormalizarRelativo(caminhoRelativo);
        if (rel.Length == 0) throw new InvalidOperationException("Informe o arquivo.");
        using var ftp = NovoCliente();
        await ftp.Connect(ct);
        var completo = Completo(rel);
        if (!await ftp.FileExists(completo, ct)) throw new FileNotFoundException("Arquivo não encontrado no FTP.");
        var tamanho = await ftp.GetFileSize(completo, -1, ct);

        var nome = Path.GetFileName(rel);
        resposta.ContentType = "application/octet-stream";
        resposta.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
        { FileNameStar = nome, FileName = nome }.ToString();
        if (tamanho > 0) resposta.ContentLength = tamanho;

        await using (var origem = await ftp.OpenRead(completo, FtpDataType.Binary, 0, false, ct))
            await origem.CopyToAsync(resposta.Body, 1024 * 256, ct);
        await ftp.GetReply(ct);
    }

    /// <summary>CPF/CNPJ (só números) → nome do cliente, para mostrar de quem é cada backup.</summary>
    public async Task<Dictionary<string, string>> ClientesPorDocumentoAsync(int codEmp)
    {
        await using var conn = new SqlConnection(config.GetConnectionString("Connection"));
        var linhas = await conn.QueryAsync<(string? Cnpj, string? Cpf, string? Nome, string? Apelido)>("""
            SELECT CnpjLimpo, CpfLimpo, Nome, Apelido FROM Cliente WHERE CodEmp = @CodEmp;
            """, new { CodEmp = codEmp });
        var mapa = new Dictionary<string, string>();
        foreach (var l in linhas)
        {
            var nome = !string.IsNullOrWhiteSpace(l.Apelido) ? $"{l.Nome?.Trim()} ({l.Apelido.Trim()})" : l.Nome?.Trim() ?? "";
            foreach (var doc in new[] { l.Cnpj, l.Cpf })
            {
                var d = new string((doc ?? "").Where(char.IsAsciiDigit).ToArray());
                if (d.Length is 11 or 14) mapa.TryAdd(d, nome);
            }
        }
        return mapa;
    }
}

public sealed class BackupFtpItem
{
    public string Nome { get; set; } = "";
    public string Caminho { get; set; } = "";
    public bool Pasta { get; set; }
    public long Tamanho { get; set; }
    public DateTime? Modificado { get; set; }
    public string Cliente { get; set; } = "";
}
