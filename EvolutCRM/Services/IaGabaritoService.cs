using System.Text;
using System.Text.RegularExpressions;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Gabarito da IA: perguntas reais (com as palavras do cliente) e a resposta certa conferida
// pelo time. Roda antes de cada mudança de prompt ou de modelo para saber se melhorou ou piorou.
// Tabelas criadas sozinhas: AiGabarito, AiGabaritoExecucao, AiGabaritoResultado
// (mesmo conteúdo de Scripts/2026-10-28_IA_Gabarito.sql).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class IaGabaritoItem
{
    public int Codigo { get; set; }
    public string Pergunta { get; set; } = "";
    public string RespostaEsperada { get; set; } = "";
    public string Assunto { get; set; } = "";
    public int? CodTicketOrigem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    /// <summary>Nota na última execução concluída (2 certa, 1 parcial, 0 errada, -1 falhou) ou null.</summary>
    public int? UltimaNota { get; set; }
}

public sealed class IaGabaritoExecucao
{
    public int Codigo { get; set; }
    public DateTime DataHora { get; set; }
    public string Usuario { get; set; } = "";
    public string Modelo { get; set; } = "";
    public int Total { get; set; }
    public int Certas { get; set; }
    public int Parciais { get; set; }
    public int Erradas { get; set; }
    public int Falhas { get; set; }
    public bool Concluida { get; set; }
    /// <summary>Certa vale 1, parcial vale 0,5.</summary>
    public double Pontuacao => Total - Falhas <= 0 ? 0 : Math.Round(100.0 * (Certas + 0.5 * Parciais) / (Total - Falhas), 1);
}

public sealed class IaGabaritoResultado
{
    public int CodGabarito { get; set; }
    public string Pergunta { get; set; } = "";
    public string RespostaEsperada { get; set; } = "";
    public string Resposta { get; set; } = "";
    public string Confianca { get; set; } = "";
    public int Nota { get; set; }
    public string Motivo { get; set; } = "";
    /// <summary>Nota na execução anterior (para mostrar o que melhorou/piorou).</summary>
    public int? NotaAnterior { get; set; }
}

public sealed class IaGabaritoService
{
    private readonly string _cs;
    private static volatile bool _tabelasOk;

    public IaGabaritoService(IConfiguration config) => _cs = config.GetConnectionString("Connection")!;

    public const string SqlTabelas = @"
IF OBJECT_ID('dbo.AiGabarito', 'U') IS NULL
    CREATE TABLE dbo.AiGabarito
    (
        Codigo           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabarito PRIMARY KEY,
        CodEmp           INT            NOT NULL,
        Pergunta         NVARCHAR(2000) NOT NULL,   -- como o cliente escreveu (com erro de digitação e tudo)
        RespostaEsperada NVARCHAR(2000) NOT NULL,   -- o que de fato resolve, conferido pelo time
        Assunto          NVARCHAR(200)  NULL,
        CodTicketOrigem  INT            NULL,       -- a busca ignora os casos deste ticket na prova
        Ativo            BIT            NOT NULL CONSTRAINT DF_AiGabarito_Ativo DEFAULT (1),
        CriadoPor        NVARCHAR(100)  NULL,
        CriadoEm         DATETIME       NOT NULL CONSTRAINT DF_AiGabarito_CriadoEm DEFAULT (GETDATE())
    );
IF OBJECT_ID('dbo.AiGabaritoExecucao', 'U') IS NULL
    CREATE TABLE dbo.AiGabaritoExecucao
    (
        Codigo     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabaritoExecucao PRIMARY KEY,
        CodEmp     INT           NOT NULL,
        DataHora   DATETIME      NOT NULL CONSTRAINT DF_AiGabaritoExecucao_DataHora DEFAULT (GETDATE()),
        Usuario    NVARCHAR(100) NULL,
        Modelo     VARCHAR(120)  NULL,
        Total      INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Total DEFAULT (0),
        Certas     INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Certas DEFAULT (0),
        Parciais   INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Parciais DEFAULT (0),
        Erradas    INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Erradas DEFAULT (0),
        Falhas     INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Falhas DEFAULT (0),
        Concluida  BIT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Concluida DEFAULT (0)
    );
IF OBJECT_ID('dbo.AiGabaritoResultado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiGabaritoResultado
    (
        Codigo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabaritoResultado PRIMARY KEY,
        CodExecucao  INT           NOT NULL,
        CodGabarito  INT           NOT NULL,
        Nota         SMALLINT      NOT NULL,      -- 2 certa, 1 parcial, 0 errada, -1 falhou
        Motivo       NVARCHAR(500) NULL,
        Resposta     NVARCHAR(MAX) NULL,
        Confianca    VARCHAR(10)   NULL
    );
    CREATE INDEX IX_AiGabaritoResultado_Exec ON dbo.AiGabaritoResultado (CodExecucao, CodGabarito);
END";

    public static async Task GarantirTabelasAsync(SqlConnection conn)
    {
        if (_tabelasOk) return;
        await using var cmd = new SqlCommand(SqlTabelas, conn);
        await cmd.ExecuteNonQueryAsync();
        _tabelasOk = true;
    }

    private async Task<SqlConnection> AbrirAsync()
    {
        var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelasAsync(conn);
        return conn;
    }

    // ───────────────────────────── Itens ─────────────────────────────

    public async Task<List<IaGabaritoItem>> ListarAsync(int codEmp, bool incluirInativos = false)
    {
        var lista = new List<IaGabaritoItem>();
        await using var conn = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
DECLARE @ultima INT = (SELECT TOP 1 Codigo FROM dbo.AiGabaritoExecucao
                       WHERE CodEmp = @emp AND Concluida = 1 ORDER BY Codigo DESC);
SELECT G.Codigo, G.Pergunta, G.RespostaEsperada, ISNULL(G.Assunto, ''), G.CodTicketOrigem, G.Ativo, G.CriadoEm, R.Nota
FROM dbo.AiGabarito G
LEFT JOIN dbo.AiGabaritoResultado R ON R.CodExecucao = @ultima AND R.CodGabarito = G.Codigo
WHERE G.CodEmp = @emp AND (@todos = 1 OR G.Ativo = 1)
ORDER BY G.Ativo DESC, G.Codigo DESC;", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@todos", incluirInativos);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IaGabaritoItem
            {
                Codigo = rd.GetInt32(0),
                Pergunta = rd.GetString(1),
                RespostaEsperada = rd.GetString(2),
                Assunto = rd.GetString(3),
                CodTicketOrigem = rd.IsDBNull(4) ? null : rd.GetInt32(4),
                Ativo = rd.GetBoolean(5),
                CriadoEm = rd.GetDateTime(6),
                UltimaNota = rd.IsDBNull(7) ? null : Convert.ToInt32(rd.GetValue(7))
            });
        }
        return lista;
    }

    public async Task SalvarAsync(int codEmp, IaGabaritoItem item, string usuario)
    {
        var pergunta = (item.Pergunta ?? "").Trim();
        var resposta = (item.RespostaEsperada ?? "").Trim();
        if (pergunta.Length < 5) throw new InvalidOperationException("Escreva a pergunta do jeito que o cliente mandou.");
        if (resposta.Length < 5) throw new InvalidOperationException("Escreva a resposta certa (o que de fato resolve).");

        await using var conn = await AbrirAsync();
        await using var cmd = new SqlCommand(item.Codigo > 0
            ? @"UPDATE dbo.AiGabarito SET Pergunta = @p, RespostaEsperada = @r, Assunto = @a, CodTicketOrigem = @t, Ativo = @at
                 WHERE Codigo = @c AND CodEmp = @emp;"
            : @"INSERT INTO dbo.AiGabarito (CodEmp, Pergunta, RespostaEsperada, Assunto, CodTicketOrigem, Ativo, CriadoPor)
                VALUES (@emp, @p, @r, @a, @t, @at, @u);", conn);
        cmd.Parameters.AddWithValue("@c", item.Codigo);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@p", Cortar(pergunta, 2000));
        cmd.Parameters.AddWithValue("@r", Cortar(resposta, 2000));
        cmd.Parameters.AddWithValue("@a", string.IsNullOrWhiteSpace(item.Assunto) ? DBNull.Value : Cortar(item.Assunto.Trim(), 200));
        cmd.Parameters.AddWithValue("@t", item.CodTicketOrigem is > 0 ? item.CodTicketOrigem.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@at", item.Ativo);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExcluirAsync(int codEmp, int codigo)
    {
        await using var conn = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
DELETE FROM dbo.AiGabaritoResultado WHERE CodGabarito = @c;
DELETE FROM dbo.AiGabarito WHERE Codigo = @c AND CodEmp = @emp;", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Sugestões para montar o gabarito rápido: tickets com solução CONFIRMADA pelo cliente que ainda não estão
    /// no gabarito. A pergunta vem das primeiras mensagens reais do cliente (sem saudação) e a resposta
    /// da solução registrada. O time confere e edita antes de salvar.
    /// </summary>
    public async Task<List<IaGabaritoItem>> SugerirAsync(int codEmp, int quantidade = 20)
    {
        var sugestoes = new List<IaGabaritoItem>();
        await using var conn = await AbrirAsync();

        var casos = new List<(int Ticket, string Resumo, string Solucao)>();
        await using (var cmd = new SqlCommand(@"
SELECT TOP (@qtd) X.CodTicketChamadoC, X.Resumo, X.Solucao
FROM (
    SELECT I.CodTicketChamadoC, ISNULL(I.Resumo, '') AS Resumo, ISNULL(I.Solucao, '') AS Solucao,
           ROW_NUMBER() OVER (PARTITION BY I.CodTicketChamadoC ORDER BY I.TopicoIndex) AS N
    FROM dbo.AiTicketInsights I
    JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
    WHERE I.SolucaoConfirmada = 1
      AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
      AND (@emp = 0 OR T.CodEmp = @emp)
      AND NOT EXISTS (SELECT 1 FROM dbo.AiGabarito G WHERE G.CodTicketOrigem = I.CodTicketChamadoC)
) X
WHERE X.N = 1
ORDER BY NEWID();", conn))
        {
            cmd.Parameters.AddWithValue("@qtd", Math.Clamp(quantidade, 1, 50));
            cmd.Parameters.AddWithValue("@emp", codEmp);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                casos.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));
        }

        foreach (var c in casos)
        {
            var msgs = new List<string>();
            await using (var cmd = new SqlCommand(@"
SELECT TOP 8 ISNULL(NULLIF(LTRIM(RTRIM(D.Anotacao)), ''), ISNULL(D.TranscricaoAudio, ''))
FROM TicketChamadoD D
WHERE D.CodTicketChamadoC = @t
  AND D.EnvioCliente = 'S'
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND (ISNULL(LTRIM(RTRIM(D.Anotacao)), '') <> '' OR ISNULL(LTRIM(RTRIM(D.TranscricaoAudio)), '') <> '')
ORDER BY D.DataHora, D.Codigo;", conn))
            {
                cmd.Parameters.AddWithValue("@t", c.Ticket);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var m = rd.GetString(0).Trim();
                    if (!EhSaudacao(m)) msgs.Add(m);
                    if (msgs.Count >= 3) break;
                }
            }

            var pergunta = Cortar(string.Join("\n", msgs), 800);
            if (pergunta.Length < 10) continue;   // cliente só cumprimentou: não serve de prova

            sugestoes.Add(new IaGabaritoItem
            {
                Pergunta = pergunta,
                RespostaEsperada = Cortar(c.Solucao, 2000),
                Assunto = Cortar(c.Resumo, 200),
                CodTicketOrigem = c.Ticket
            });
        }
        return sugestoes;
    }

    // ─────────────────────────── Execuções ───────────────────────────

    public async Task<List<IaGabaritoExecucao>> ListarExecucoesAsync(int codEmp, int quantidade = 15)
    {
        var lista = new List<IaGabaritoExecucao>();
        await using var conn = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
SELECT TOP (@qtd) Codigo, DataHora, ISNULL(Usuario, ''), ISNULL(Modelo, ''), Total, Certas, Parciais, Erradas, Falhas, Concluida
FROM dbo.AiGabaritoExecucao
WHERE CodEmp = @emp
ORDER BY Codigo DESC;", conn);
        cmd.Parameters.AddWithValue("@qtd", quantidade);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IaGabaritoExecucao
            {
                Codigo = rd.GetInt32(0),
                DataHora = rd.GetDateTime(1),
                Usuario = rd.GetString(2),
                Modelo = rd.GetString(3),
                Total = rd.GetInt32(4),
                Certas = rd.GetInt32(5),
                Parciais = rd.GetInt32(6),
                Erradas = rd.GetInt32(7),
                Falhas = rd.GetInt32(8),
                Concluida = rd.GetBoolean(9)
            });
        }
        return lista;
    }

    /// <summary>Resultados de uma execução, com a nota da execução concluída anterior para comparar.</summary>
    public async Task<List<IaGabaritoResultado>> ResultadosAsync(int codEmp, int codExecucao)
    {
        var lista = new List<IaGabaritoResultado>();
        await using var conn = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
DECLARE @anterior INT = (SELECT TOP 1 Codigo FROM dbo.AiGabaritoExecucao
                         WHERE CodEmp = @emp AND Concluida = 1 AND Codigo < @exec ORDER BY Codigo DESC);
SELECT R.CodGabarito, G.Pergunta, G.RespostaEsperada, ISNULL(R.Resposta, ''), ISNULL(R.Confianca, ''),
       R.Nota, ISNULL(R.Motivo, ''), A.Nota
FROM dbo.AiGabaritoResultado R
JOIN dbo.AiGabarito G ON G.Codigo = R.CodGabarito
LEFT JOIN dbo.AiGabaritoResultado A ON A.CodExecucao = @anterior AND A.CodGabarito = R.CodGabarito
WHERE R.CodExecucao = @exec
ORDER BY R.Nota, R.CodGabarito;", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@exec", codExecucao);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IaGabaritoResultado
            {
                CodGabarito = rd.GetInt32(0),
                Pergunta = rd.GetString(1),
                RespostaEsperada = rd.GetString(2),
                Resposta = rd.GetString(3),
                Confianca = rd.GetString(4),
                Nota = Convert.ToInt32(rd.GetValue(5)),
                Motivo = rd.GetString(6),
                NotaAnterior = rd.IsDBNull(7) ? null : Convert.ToInt32(rd.GetValue(7))
            });
        }
        return lista;
    }

    private static readonly Regex Saudacao = new(
        @"^(oi+|ol[aá]|bom dia|boa tarde|boa noite|tudo bem|td bem|ok|obrigad[oa]|blz|beleza|opa|e a[ií])[\s!.,?]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool EhSaudacao(string m)
        => m.Length < 4 || Saudacao.IsMatch(m.Trim());

    private static string Cortar(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}

/// <summary>
/// Roda o gabarito em segundo plano (um por vez): para cada pergunta ativa gera a sugestão como a tela do
/// ticket gera (ignorando os casos do ticket de origem) e a IA juíza compara com a resposta esperada.
/// </summary>
public sealed class IaGabaritoJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly object _trava = new();

    public bool Rodando { get; private set; }
    public int Total { get; private set; }
    public int Feitos { get; private set; }
    public int CodExecucao { get; private set; }
    public string UltimoErro { get; private set; } = "";

    public IaGabaritoJob(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _config = config;
    }

    /// <summary>false se já tem uma execução rodando.</summary>
    public bool Iniciar(int codEmp, string usuario)
    {
        lock (_trava)
        {
            if (Rodando) return false;
            Rodando = true;
            Total = 0;
            Feitos = 0;
            CodExecucao = 0;
            UltimoErro = "";
        }
        _ = Task.Run(() => RodarAsync(codEmp, usuario));
        return true;
    }

    private async Task RodarAsync(int codEmp, string usuario)
    {
        int certas = 0, parciais = 0, erradas = 0, falhas = 0;
        var cs = _config.GetConnectionString("Connection")!;
        try
        {
            List<IaGabaritoItem> itens;
            using (var scope = _scopeFactory.CreateScope())
                itens = await scope.ServiceProvider.GetRequiredService<IaGabaritoService>().ListarAsync(codEmp);

            Total = itens.Count;
            if (Total == 0) { UltimoErro = "O gabarito não tem perguntas ativas."; return; }

            var modelo = $"{_config["OpenAI:ModeloResposta"] ?? "gpt-4.1"} (busca/reordenação: {_config["OpenAI:Model"] ?? "gpt-4.1-mini"})";
            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(@"
INSERT INTO dbo.AiGabaritoExecucao (CodEmp, Usuario, Modelo, Total)
OUTPUT INSERTED.Codigo VALUES (@emp, @u, @m, @t);", conn);
                cmd.Parameters.AddWithValue("@emp", codEmp);
                cmd.Parameters.AddWithValue("@u", usuario.Length > 100 ? usuario[..100] : usuario);
                cmd.Parameters.AddWithValue("@m", modelo.Length > 120 ? modelo[..120] : modelo);
                cmd.Parameters.AddWithValue("@t", Total);
                CodExecucao = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            foreach (var item in itens)
            {
                int nota;
                string motivo, resposta = "", confianca = "";
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ai = scope.ServiceProvider.GetRequiredService<AiService>();
                    var r = await ai.SugerirAsync(new AiPedidoSugestao
                    {
                        CodEmp = codEmp,
                        MensagensCliente = item.Pergunta,
                        IgnorarTicketNaBusca = item.CodTicketOrigem ?? 0,
                        Origem = "gabarito",
                        Usuario = "gabarito"
                    });

                    if (!r.Ok)
                    {
                        nota = -1;
                        motivo = r.Resposta;
                    }
                    else
                    {
                        resposta = r.Resposta;
                        confianca = r.Confianca;
                        (nota, motivo) = await ai.JulgarRespostaAsync(item.Pergunta, item.RespostaEsperada, r.Resposta);
                    }
                }
                catch (Exception ex)
                {
                    nota = -1;
                    motivo = ex.Message;
                }

                if (nota == 2) certas++;
                else if (nota == 1) parciais++;
                else if (nota == 0) erradas++;
                else falhas++;

                await using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    await using var cmd = new SqlCommand(@"
INSERT INTO dbo.AiGabaritoResultado (CodExecucao, CodGabarito, Nota, Motivo, Resposta, Confianca)
VALUES (@e, @g, @n, @m, @r, @c);
UPDATE dbo.AiGabaritoExecucao SET Certas = @ce, Parciais = @pa, Erradas = @er, Falhas = @fa WHERE Codigo = @e;", conn);
                    cmd.Parameters.AddWithValue("@e", CodExecucao);
                    cmd.Parameters.AddWithValue("@g", item.Codigo);
                    cmd.Parameters.AddWithValue("@n", nota);
                    cmd.Parameters.AddWithValue("@m", motivo.Length > 500 ? motivo[..500] : motivo);
                    cmd.Parameters.AddWithValue("@r", resposta);
                    cmd.Parameters.AddWithValue("@c", confianca.Length > 10 ? confianca[..10] : confianca);
                    cmd.Parameters.AddWithValue("@ce", certas);
                    cmd.Parameters.AddWithValue("@pa", parciais);
                    cmd.Parameters.AddWithValue("@er", erradas);
                    cmd.Parameters.AddWithValue("@fa", falhas);
                    await cmd.ExecuteNonQueryAsync();
                }

                Feitos++;
                await Task.Delay(300);   // não estourar o limite da OpenAI
            }

            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("UPDATE dbo.AiGabaritoExecucao SET Concluida = 1 WHERE Codigo = @e;", conn);
                cmd.Parameters.AddWithValue("@e", CodExecucao);
                await cmd.ExecuteNonQueryAsync();
            }
            LogHelper.Log($"[GABARITO] Execução {CodExecucao}: {certas} certas, {parciais} parciais, {erradas} erradas, {falhas} falhas de {Total}.");
        }
        catch (Exception ex)
        {
            UltimoErro = ex.Message;
            LogHelper.Log("[GABARITO] Erro: " + ex);
        }
        finally
        {
            Rodando = false;
        }
    }
}
