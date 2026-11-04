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
SELECT Codigo, Atalho, Titulo, Texto, Ativo, QtdUsos, ISNULL(AlteradoPor, CriadoPor), ISNULL(AlteradoEm, CriadoEm)
FROM dbo.RespostaRapida
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
                AlteradoEm = rd.GetDateTime(7)
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
        if (texto.Length == 0) return "Escreva o texto da resposta.";
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

        await using var cmd = new SqlCommand(r.Codigo > 0
            ? @"UPDATE dbo.RespostaRapida
                SET Atalho = @a, Titulo = @t, Texto = @x, Ativo = @at, AlteradoPor = @u, AlteradoEm = GETDATE()
                WHERE Codigo = @c AND CodEmp = @e;"
            : @"INSERT INTO dbo.RespostaRapida (CodEmp, Atalho, Titulo, Texto, Ativo, CriadoPor)
                VALUES (@e, @a, @t, @x, @at, @u);", conn);
        cmd.Parameters.AddWithValue("@e", Emp);
        cmd.Parameters.AddWithValue("@c", r.Codigo);
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 30).Value = atalho;
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 100).Value = titulo;
        cmd.Parameters.Add("@x", SqlDbType.NVarChar, -1).Value = texto;
        cmd.Parameters.Add("@at", SqlDbType.Char, 1).Value = r.Ativo ? "S" : "N";
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = usu;
        await cmd.ExecuteNonQueryAsync();
        return null;
    }

    public async Task ExcluirAsync(int codigo)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);
        await using var cmd = new SqlCommand("DELETE FROM dbo.RespostaRapida WHERE Codigo = @c AND CodEmp = @e;", conn);
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
