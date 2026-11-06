using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Respostas rápidas do ticket: o atendente digita "/" + atalho (ex.: /pix) na caixa de mensagem
/// e o texto pronto entra no lugar, com {cliente}, {primeiro_nome}, {atendente} e {saudacao} preenchidos.
/// Ficam em dbo.RespostaRapida, por empresa, e valem para toda a equipe.
/// </summary>
public sealed class RespostaRapidaService
{
    private readonly string _cs;
    private readonly UserState _state;
    private static volatile bool _tabelaOk;

    public RespostaRapidaService(IConfiguration config, UserState state)
    {
        _cs = config.GetConnectionString("Connection")!;
        _state = state;
    }

    public sealed class RespostaRapida
    {
        public int Codigo { get; set; }
        public string Atalho { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Texto { get; set; } = "";
        public bool Ativo { get; set; } = true;
        public int QtdUsos { get; set; }
        public string AlteradoPor { get; set; } = "";
        public DateTime? AlteradoEm { get; set; }

        /// <summary>Quantas imagens a resposta tem (as imagens ficam no texto como [[img:ID]], onde foram coladas).</summary>
        public int QtdImagens { get; set; }
        public bool TemImagem => QtdImagens > 0;
        /// <summary>Ao salvar: imagens novas coladas no editor (chave temporária → data URL). A chave aparece no texto como [[img:chave]].</summary>
        public Dictionary<string, string> ImagensNovas { get; set; } = new();
    }

    private int Emp => _state.CurrentCompanyId;

    private static async Task GarantirTabelaAsync(SqlConnection conn)
    {
        if (_tabelaOk) return;
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.RespostaRapida', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RespostaRapida
    (
        Codigo      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RespostaRapida PRIMARY KEY,
        CodEmp      INT            NOT NULL,
        Atalho      VARCHAR(30)    NOT NULL,
        Titulo      NVARCHAR(100)  NOT NULL,
        Texto       NVARCHAR(MAX)  NOT NULL,
        Ativo       CHAR(1)        NOT NULL CONSTRAINT DF_RespostaRapida_Ativo DEFAULT ('S'),
        QtdUsos     INT            NOT NULL CONSTRAINT DF_RespostaRapida_Usos DEFAULT (0),
        CriadoPor   VARCHAR(100)   NOT NULL,
        CriadoEm    DATETIME       NOT NULL CONSTRAINT DF_RespostaRapida_Criado DEFAULT (GETDATE()),
        AlteradoPor VARCHAR(100)   NULL,
        AlteradoEm  DATETIME       NULL
    );
    CREATE UNIQUE INDEX UX_RespostaRapida_Atalho ON dbo.RespostaRapida (CodEmp, Atalho);
END;
-- Imagens das respostas (coladas com Ctrl+V no ponto do texto onde devem aparecer)
IF OBJECT_ID('dbo.RespostaRapidaImagem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RespostaRapidaImagem
    (
        Codigo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RespostaRapidaImagem PRIMARY KEY,
        CodEmp       INT            NOT NULL,
        CodResposta  INT            NOT NULL,
        Imagem       VARBINARY(MAX) NOT NULL,
        Tipo         VARCHAR(40)    NOT NULL,
        CriadoEm     DATETIME       NOT NULL CONSTRAINT DF_RespostaRapidaImagem_Criado DEFAULT (GETDATE())
    );
    CREATE INDEX IX_RespostaRapidaImagem_Resposta ON dbo.RespostaRapidaImagem (CodEmp, CodResposta);
END", conn);
        await cmd.ExecuteNonQueryAsync();
        _tabelaOk = true;
    }

    /// <summary>"/Pix Mensal" → "pix-mensal" (sem acento, minúsculo, só letras, números, - e _).</summary>
    public static string NormalizarAtalho(string? atalho)
    {
        var s = (atalho ?? "").Trim().TrimStart('/').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        var r = Regex.Replace(sb.ToString(), @"\s+", "-");
        r = Regex.Replace(r, @"[^a-z0-9_-]", "");
        return r.Length > 30 ? r[..30] : r;
    }

    public async Task<List<RespostaRapida>> ListarAsync(bool somenteAtivas)
    {
        var lista = new List<RespostaRapida>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);

        await using var cmd = new SqlCommand($@"
SELECT Codigo, Atalho, Titulo, Texto, Ativo, QtdUsos, ISNULL(AlteradoPor, CriadoPor), ISNULL(AlteradoEm, CriadoEm),
       (SELECT COUNT(*) FROM dbo.RespostaRapidaImagem I WHERE I.CodEmp = R.CodEmp AND I.CodResposta = R.Codigo)
FROM dbo.RespostaRapida R
WHERE CodEmp = @e {(somenteAtivas ? "AND Ativo = 'S'" : "")}
ORDER BY QtdUsos DESC, Atalho;", conn);
        cmd.Parameters.AddWithValue("@e", Emp);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(new RespostaRapida
            {
                Codigo = rd.GetInt32(0),
                Atalho = rd.GetString(1),
                Titulo = rd.GetString(2),
                Texto = rd.GetString(3),
                Ativo = rd.GetString(4) == "S",
                QtdUsos = rd.GetInt32(5),
                AlteradoPor = rd.GetString(6),
                AlteradoEm = rd.GetDateTime(7),
                QtdImagens = rd.GetInt32(8)
            });
        return lista;
    }

    /// <summary>Grava (nova ou alteração). Devolve null quando deu certo, ou a mensagem do problema.</summary>
    public async Task<string?> SalvarAsync(RespostaRapida r, string usuario)
    {
        var atalho = NormalizarAtalho(r.Atalho);
        var titulo = (r.Titulo ?? "").Trim();
        var texto = (r.Texto ?? "").Trim();
        if (atalho.Length < 2) return "O atalho precisa ter pelo menos 2 letras ou números (ex.: pix).";
        if (titulo.Length == 0) return "Dê um título para a resposta.";

        // Imagens novas: só as que continuam no texto
        var novas = new Dictionary<string, (byte[] Bytes, string Tipo)>();
        foreach (var (chave, dataUrl) in r.ImagensNovas ?? new())
        {
            if (!texto.Contains($"[[img:{chave}]]")) continue;
            var m = Regex.Match(dataUrl ?? "", @"^data:(image/(?:png|jpeg|jpg|gif|webp));base64,(.+)$", RegexOptions.Singleline);
            if (!m.Success) return "Formato de imagem não aceito. Use PNG, JPG, GIF ou WEBP.";
            byte[] bytes;
            try { bytes = Convert.FromBase64String(m.Groups[2].Value); }
            catch { return "Não consegui ler uma das imagens coladas."; }
            if (bytes.Length > 3 * 1024 * 1024) return "Uma das imagens é muito grande (máximo 3 MB cada).";
            novas[chave] = (bytes, m.Groups[1].Value == "image/jpg" ? "image/jpeg" : m.Groups[1].Value);
        }
        if (TextoSemImagens(texto).Length == 0 && !Regex.IsMatch(texto, @"\[\[img:[^\]]+\]\]"))
            return "Escreva o texto da resposta ou cole uma imagem.";
        if (titulo.Length > 100) titulo = titulo[..100];
        var usu = (usuario ?? "").Trim().ToUpperInvariant();

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);

        await using (var dup = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.RespostaRapida WHERE CodEmp = @e AND Atalho = @a AND Codigo <> @c;", conn))
        {
            dup.Parameters.AddWithValue("@e", Emp);
            dup.Parameters.Add("@a", SqlDbType.VarChar, 30).Value = atalho;
            dup.Parameters.AddWithValue("@c", r.Codigo);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync()) > 0)
                return $"Já existe uma resposta com o atalho /{atalho}.";
        }

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        await using var cmd = new SqlCommand(r.Codigo > 0
            ? @"UPDATE dbo.RespostaRapida
                SET Atalho = @a, Titulo = @t, Texto = @x, Ativo = @at, AlteradoPor = @u, AlteradoEm = GETDATE()
                WHERE Codigo = @c AND CodEmp = @e;
                SELECT @c;"
            : @"INSERT INTO dbo.RespostaRapida (CodEmp, Atalho, Titulo, Texto, Ativo, CriadoPor)
                VALUES (@e, @a, @t, @x, @at, @u);
                SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx);
        cmd.Parameters.AddWithValue("@e", Emp);
        cmd.Parameters.AddWithValue("@c", r.Codigo);
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 30).Value = atalho;
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 100).Value = titulo;
        cmd.Parameters.Add("@x", SqlDbType.NVarChar, -1).Value = texto;
        cmd.Parameters.Add("@at", SqlDbType.Char, 1).Value = r.Ativo ? "S" : "N";
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = usu;
        var codigo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        // Grava as imagens novas e troca a chave temporária pelo código no texto
        foreach (var (chave, img) in novas)
        {
            await using var ins = new SqlCommand(@"
INSERT INTO dbo.RespostaRapidaImagem (CodEmp, CodResposta, Imagem, Tipo) VALUES (@e, @r, @i, @t);
SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx);
            ins.Parameters.AddWithValue("@e", Emp);
            ins.Parameters.AddWithValue("@r", codigo);
            ins.Parameters.Add("@i", SqlDbType.VarBinary, -1).Value = img.Bytes;
            ins.Parameters.Add("@t", SqlDbType.VarChar, 40).Value = img.Tipo;
            var idImg = Convert.ToInt32(await ins.ExecuteScalarAsync());
            texto = texto.Replace($"[[img:{chave}]]", $"[[img:{idImg}]]");
        }

        // Texto final + apaga as imagens que saíram do texto
        var usados = Regex.Matches(texto, @"\[\[img:(\d+)\]\]").Select(m => m.Groups[1].Value).Distinct().ToList();
        await using (var fim = new SqlCommand($@"
UPDATE dbo.RespostaRapida SET Texto = @x WHERE Codigo = @r AND CodEmp = @e;
DELETE FROM dbo.RespostaRapidaImagem
WHERE CodEmp = @e AND CodResposta = @r {(usados.Count > 0 ? "AND Codigo NOT IN (" + string.Join(",", usados) + ")" : "")};", conn, tx))
        {
            fim.Parameters.AddWithValue("@e", Emp);
            fim.Parameters.AddWithValue("@r", codigo);
            fim.Parameters.Add("@x", SqlDbType.NVarChar, -1).Value = texto;
            await fim.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return null;
    }

    // ── Imagens ──
    private static readonly Regex RxImagem = new(@"\[\[img:([A-Za-z0-9_-]+)\]\]", RegexOptions.Compiled);

    /// <summary>Texto sem as marcas de imagem (para busca, lista e validação).</summary>
    public static string TextoSemImagens(string? texto) => RxImagem.Replace(texto ?? "", " ").Trim();

    /// <summary>Quebra o texto nos pontos das imagens: (texto, null) e (null, códigoImagem), na ordem.</summary>
    public static List<(string? Texto, int? Imagem)> Partes(string? texto)
    {
        var partes = new List<(string?, int?)>();
        var t = texto ?? "";
        var pos = 0;
        foreach (Match m in RxImagem.Matches(t))
        {
            var antes = t[pos..m.Index].Trim();
            if (antes.Length > 0) partes.Add((antes, null));
            if (int.TryParse(m.Groups[1].Value, out var cod)) partes.Add((null, cod));
            pos = m.Index + m.Length;
        }
        var resto = t[pos..].Trim();
        if (resto.Length > 0) partes.Add((resto, null));
        return partes;
    }

    /// <summary>Imagens da resposta: código → (bytes, tipo).</summary>
    public async Task<Dictionary<int, (byte[] Bytes, string Tipo)>> ListarImagensAsync(int codResposta)
    {
        var r = new Dictionary<int, (byte[], string)>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);
        await using var cmd = new SqlCommand(
            "SELECT Codigo, Imagem, Tipo FROM dbo.RespostaRapidaImagem WHERE CodEmp = @e AND CodResposta = @r;", conn);
        cmd.Parameters.AddWithValue("@e", Emp);
        cmd.Parameters.AddWithValue("@r", codResposta);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) r[rd.GetInt32(0)] = ((byte[])rd[1], rd.GetString(2));
        return r;
    }

    public async Task<RespostaRapida?> BuscarAsync(int codigo) =>
        (await ListarAsync(false)).FirstOrDefault(x => x.Codigo == codigo);

    /// <summary>Troca {saudacao}, {primeiro_nome}, {cliente} e {atendente} (mesma regra do navegador).</summary>
    public static string ResolverVariaveis(string texto, string? cliente, string? atendente)
    {
        var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
        string Cap(string? s) => ti.ToTitleCase((s ?? "").Trim().ToLowerInvariant());
        var cli = (cliente ?? "").Trim();
        var primeiro = Cap(cli.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
        var h = TimeZoneInfo.ConvertTime(DateTime.UtcNow, ZonaBrasil).Hour;
        var saud = h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
        return texto
            .Replace("{saudacao}", saud, StringComparison.OrdinalIgnoreCase)
            .Replace("{primeiro_nome}", primeiro, StringComparison.OrdinalIgnoreCase)
            .Replace("{cliente}", Cap(cli), StringComparison.OrdinalIgnoreCase)
            .Replace("{atendente}", Cap(atendente), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly TimeZoneInfo ZonaBrasil = Zona();
    private static TimeZoneInfo Zona()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
            catch { return TimeZoneInfo.Local; }
        }
    }

    public async Task ExcluirAsync(int codigo)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);
        await using var cmd = new SqlCommand(@"DELETE FROM dbo.RespostaRapidaImagem WHERE CodResposta = @c AND CodEmp = @e;
DELETE FROM dbo.RespostaRapida WHERE Codigo = @c AND CodEmp = @e;", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@e", Emp);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RegistrarUsoAsync(int codigo)
    {
        try
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await GarantirTabelaAsync(conn);
            await using var cmd = new SqlCommand(
                "UPDATE dbo.RespostaRapida SET QtdUsos = QtdUsos + 1 WHERE Codigo = @c AND CodEmp = @e;", conn);
            cmd.Parameters.AddWithValue("@c", codigo);
            cmd.Parameters.AddWithValue("@e", Emp);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* contador não pode atrapalhar o atendimento */ }
    }

    /// <summary>Exemplos para começar (só cria os atalhos que ainda não existem).</summary>
    public async Task<int> CriarExemplosAsync(string usuario)
    {
        var exemplos = new[]
        {
            new RespostaRapida { Atalho = "ola", Titulo = "Saudação",
                Texto = "{saudacao}, {primeiro_nome}! Aqui é {atendente}, do suporte da EvolutTech. Como posso te ajudar?" },
            new RespostaRapida { Atalho = "acesso", Titulo = "Pedir acesso remoto",
                Texto = "{primeiro_nome}, para eu verificar direto no seu computador, me passe por favor o ID e a senha do acesso remoto." },
            new RespostaRapida { Atalho = "aguarde", Titulo = "Pedir um momento",
                Texto = "Só um momento, {primeiro_nome}, já estou verificando para você." },
            new RespostaRapida { Atalho = "encerrar", Titulo = "Encerramento",
                Texto = "Que bom que deu certo, {primeiro_nome}! Se precisar de mais alguma coisa, é só chamar. Tenha um ótimo dia!" },
        };

        var existentes = (await ListarAsync(false)).Select(x => x.Atalho).ToHashSet();
        var criados = 0;
        foreach (var e in exemplos.Where(e => !existentes.Contains(e.Atalho)))
            if (await SalvarAsync(e, usuario) == null) criados++;
        return criados;
    }
}
