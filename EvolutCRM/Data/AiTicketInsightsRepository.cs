using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Data;

public sealed class AiTicketInsightsRepository
{
    private readonly string _cs;

    public AiTicketInsightsRepository(IConfiguration config)
    {
        _cs = config.GetConnectionString("Connection");
        if (string.IsNullOrWhiteSpace(_cs))
            throw new InvalidOperationException("ConnectionStrings:Connection não encontrada no appsettings.json");
    }

    /// <summary>
    /// Retorna TODOS os insights do ticket (ordenados por TopicoIndex).
    /// </summary>
    public async Task<List<TicketInsight>> ListByTicketIdAsync(int ticketId)
    {
        const string sql = @"
SELECT
  TopicoIndex,
  TopicoTitulo,
  PerguntaCliente,
  Resumo,
  Categoria,
  Intencao,
  Gravidade,
  Tags,
  CausaProvavel,
  Solucao,
  SolucaoConfirmada,
  Confianca
FROM dbo.AiTicketInsights
WHERE CodTicketChamadoC = @CodTicketChamadoC
ORDER BY TopicoIndex;";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CodTicketChamadoC", SqlDbType.Int).Value = ticketId;

        var list = new List<TicketInsight>();

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var tags = (rd["Tags"] as string ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            list.Add(new TicketInsight
            {
                topicoIndex = rd["TopicoIndex"] is int ti ? ti : 1,
                topicoTitulo = rd["TopicoTitulo"] as string,

                perguntaCliente = rd["PerguntaCliente"] as string,
                resumo = rd["Resumo"] as string,
                categoria = rd["Categoria"] as string,
                intencao = rd["Intencao"] as string,
                gravidade = rd["Gravidade"] as string,
                tags = tags.Count == 0 ? null : tags,
                causaProvavel = rd["CausaProvavel"] as string,

                solucao = rd["Solucao"] as string,

                solucaoConfirmada =
    rd["SolucaoConfirmada"] != DBNull.Value &&
    Convert.ToBoolean(rd["SolucaoConfirmada"]),

                confianca =
    rd["Confianca"] == DBNull.Value
        ? 0
        : Convert.ToInt32(rd["Confianca"])

            });
        }

        return list;
    }

    /// <summary>
    /// Opção A (profissional): substitui todos os insights do ticket.
    /// - DELETE de tudo do ticket
    /// - INSERT de 1 linha por insight
    /// Tudo dentro de TRANSACTION.
    /// </summary>
    public async Task ReplaceAllForTicketAsync(int ticketId, List<TicketInsight> insights)
    {
        if (insights == null) throw new ArgumentNullException(nameof(insights));

        // Se você preferir permitir "limpar" insights passando lista vazia, remova este if
        if (insights.Count == 0)
            throw new InvalidOperationException("Lista de insights vazia.");

        const string deleteSql = @"
DELETE FROM dbo.AiTicketInsights
WHERE CodTicketChamadoC = @CodTicketChamadoC;";

        const string insertSql = @"
INSERT INTO dbo.AiTicketInsights
(
  CodTicketChamadoC,
  TopicoIndex,
  TopicoTitulo,
  PerguntaCliente,
  Resumo,
  Categoria,
  Intencao,
  Gravidade,
  Tags,
  CausaProvavel,
  Solucao,
  SolucaoConfirmada,
  Confianca
)
VALUES
(
  @CodTicketChamadoC,
  @TopicoIndex,
  @TopicoTitulo,
  @PerguntaCliente,
  @Resumo,
  @Categoria,
  @Intencao,
  @Gravidade,
  @Tags,
  @CausaProvavel,
  @Solucao,
  @SolucaoConfirmada,
  @Confianca
);";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // 1) Delete tudo do ticket
            await using (var del = new SqlCommand(deleteSql, conn, (SqlTransaction)tx))
            {
                del.Parameters.Add("@CodTicketChamadoC", SqlDbType.Int).Value = ticketId;
                await del.ExecuteNonQueryAsync();
            }

            // 2) Insert 1 por insight
            for (int i = 0; i < insights.Count; i++)
            {
                var ins = insights[i];

                // garante topicoIndex válido (1..N)
                var topicoIndex = ins.topicoIndex > 0 ? ins.topicoIndex : (i + 1);

                var tagsStr = ins.tags == null
                    ? null
                    : string.Join(";", ins.tags
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => Normalize(t)!.Replace(";", ""))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12));

                await using var cmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);

                cmd.Parameters.Add("@CodTicketChamadoC", SqlDbType.Int).Value = ticketId;
                cmd.Parameters.Add("@TopicoIndex", SqlDbType.Int).Value = topicoIndex;

                cmd.Parameters.Add("@TopicoTitulo", SqlDbType.NVarChar, 120).Value =
                    ToDb(Trunc(Normalize(ins.topicoTitulo), 120));

                cmd.Parameters.Add("@PerguntaCliente", SqlDbType.NVarChar, 800).Value =
                    ToDb(Trunc(Normalize(ins.perguntaCliente), 800));

                cmd.Parameters.Add("@Resumo", SqlDbType.NVarChar, 300).Value =
                    ToDb(Trunc(Normalize(ins.resumo), 300));

                cmd.Parameters.Add("@Categoria", SqlDbType.NVarChar, 40).Value =
                    ToDb(Trunc(Normalize(ins.categoria), 40));

                cmd.Parameters.Add("@Intencao", SqlDbType.NVarChar, 20).Value =
                    ToDb(Trunc(Normalize(ins.intencao), 20));

                cmd.Parameters.Add("@Gravidade", SqlDbType.NVarChar, 10).Value =
                    ToDb(Trunc(Normalize(ins.gravidade), 10));

                cmd.Parameters.Add("@Tags", SqlDbType.NVarChar, 200).Value =
                    ToDb(Trunc(tagsStr, 200));

                cmd.Parameters.Add("@CausaProvavel", SqlDbType.NVarChar, 1000).Value =
    ToDb(Trunc(Normalize(ins.causaProvavel), 1000));

                cmd.Parameters.Add("@Solucao", SqlDbType.NVarChar, 1200).Value =
                    ToDb(Trunc(Normalize(ins.solucao), 1200));

                cmd.Parameters.Add("@SolucaoConfirmada", SqlDbType.Bit).Value =
    ins.solucaoConfirmada;

                cmd.Parameters.Add("@Confianca", SqlDbType.Int).Value =
                    ins.confianca;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// (Opcional) busca soluções antigas por categoria/tag.
    /// Agora pode retornar múltiplas linhas do mesmo TicketId (cada tópico).
    /// </summary>
    public async Task<List<(int ticketId, int topicoIndex, string? resumo, string? tags, string? solucao, DateTime criadoEm)>> FindSimilarAsync(
    string? categoria,
    string? tag,
    string? question,
    int top = 5)
    {
        var normalizedQuestion = Normalize(question);

        var words = string.IsNullOrWhiteSpace(normalizedQuestion)
    ? new List<string>()
    : Regex.Matches(normalizedQuestion!, @"\b[\p{L}0-9]+\b")
        .Select(m => m.Value.Trim().ToLowerInvariant())
        .Where(w => w.Length >= 3)
        .Where(w => w is not (
            "boa" or "tarde" or "tudo" or "bem" or
            "como" or "onde" or "qual" or "para" or "isso" or "aqui" or
            "com" or "sem" or "uma" or "uns" or "que" or "eu" or "consigo" or
            "apenas" or "pelo" or "pela"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(10)
        .ToList();

        var scoreSql = new StringBuilder();
        scoreSql.AppendLine("0");

        // Tag e categoria agora dão peso, mas NÃO filtram obrigatoriamente
        scoreSql.AppendLine("+ CASE WHEN @Tag IS NOT NULL AND (");
        scoreSql.AppendLine("      Tags LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine("   OR PerguntaCliente LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine("   OR Resumo LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine("   OR Solucao LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine("   OR CausaProvavel LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine("   OR TopicoTitulo LIKE '%' + @Tag + '%'");
        scoreSql.AppendLine(") THEN 8 ELSE 0 END");

        scoreSql.AppendLine("+ CASE WHEN @Categoria IS NOT NULL AND Categoria = @Categoria THEN 6 ELSE 0 END");

        for (int i = 0; i < words.Count; i++)
        {
            scoreSql.AppendLine($@"
+ CASE
    WHEN PerguntaCliente LIKE '%' + @W{i} + '%' THEN 10
    WHEN TopicoTitulo LIKE '%' + @W{i} + '%' THEN 9
    WHEN Resumo LIKE '%' + @W{i} + '%' THEN 8
    WHEN Tags LIKE '%' + @W{i} + '%' THEN 7
    WHEN CausaProvavel LIKE '%' + @W{i} + '%' THEN 7
    WHEN Solucao LIKE '%' + @W{i} + '%' THEN 6
    ELSE 0
  END");
        }

        var whereParts = new List<string>
    {
        "Solucao IS NOT NULL",
        "LTRIM(RTRIM(ISNULL(Solucao, ''))) <> ''"
    };

        if (!string.IsNullOrWhiteSpace(categoria))
            whereParts.Add("Categoria = @Categoria");

        if (words.Count > 0)
        {
            var wordConditions = new List<string>();

            for (int i = 0; i < words.Count; i++)
            {
                wordConditions.Add($@"
(
    PerguntaCliente LIKE '%' + @W{i} + '%'
    OR TopicoTitulo LIKE '%' + @W{i} + '%'
    OR Resumo LIKE '%' + @W{i} + '%'
    OR Tags LIKE '%' + @W{i} + '%'
    OR CausaProvavel LIKE '%' + @W{i} + '%'
    OR Solucao LIKE '%' + @W{i} + '%'
)");
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                wordConditions.Add(@"
(
    Tags LIKE '%' + @Tag + '%'
    OR PerguntaCliente LIKE '%' + @Tag + '%'
    OR TopicoTitulo LIKE '%' + @Tag + '%'
    OR Resumo LIKE '%' + @Tag + '%'
    OR Solucao LIKE '%' + @Tag + '%'
)");
            }

            whereParts.Add("(" + string.Join(" OR ", wordConditions) + ")");
        }
        else if (!string.IsNullOrWhiteSpace(tag))
        {
            whereParts.Add(@"
(
    Tags LIKE '%' + @Tag + '%'
    OR PerguntaCliente LIKE '%' + @Tag + '%'
    OR TopicoTitulo LIKE '%' + @Tag + '%'
    OR Resumo LIKE '%' + @Tag + '%'
    OR CausaProvavel LIKE '%' + @Tag + '%'
    OR Solucao LIKE '%' + @Tag + '%'
)");
        }

        var whereSql = string.Join("\nAND ", whereParts);

        var sql = $@"
SELECT TOP (@Top)
    CodTicketChamadoC,
    TopicoIndex,
    Resumo,
    Tags,
    Solucao,
    CriadoEm,
    ({scoreSql}) AS Score
FROM dbo.AiTicketInsights
WHERE {whereSql}
  AND ({scoreSql}) >= 12
ORDER BY Score DESC, CriadoEm DESC;";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
        cmd.Parameters.Add("@Categoria", SqlDbType.NVarChar, 40).Value = ToDb(Trunc(Normalize(categoria), 40));
        cmd.Parameters.Add("@Tag", SqlDbType.NVarChar, 50).Value = ToDb(Trunc(Normalize(tag), 50));

        for (int i = 0; i < words.Count; i++)
        {
            cmd.Parameters.Add($"@W{i}", SqlDbType.NVarChar, 50).Value = words[i];
        }

        System.Diagnostics.Debug.WriteLine("[INSIGHTS] Pergunta normalizada: " + (normalizedQuestion ?? "(null)"));
        System.Diagnostics.Debug.WriteLine("[INSIGHTS] Tag recebida: " + (tag ?? "(null)"));
        System.Diagnostics.Debug.WriteLine("[INSIGHTS] Categoria recebida: " + (categoria ?? "(null)"));
        System.Diagnostics.Debug.WriteLine("[INSIGHTS] Palavras usadas: " + (words.Count == 0 ? "(nenhuma)" : string.Join(", ", words)));
        System.Diagnostics.Debug.WriteLine("[INSIGHTS] SQL WHERE montado:");
        System.Diagnostics.Debug.WriteLine(whereSql);

        var list = new List<(int, int, string?, string?, string?, DateTime)>();

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var ticketId = (int)rd["CodTicketChamadoC"];
            var topicoIndex = rd["TopicoIndex"] is int ti ? ti : 1;
            var resumo = rd["Resumo"] as string;
            var tagsStr = rd["Tags"] as string;
            var solucao = rd["Solucao"] as string;
            var criadoEm = (DateTime)rd["CriadoEm"];
            var score = rd["Score"] is int s ? s : Convert.ToInt32(rd["Score"]);

            System.Diagnostics.Debug.WriteLine(
                $"[INSIGHTS] Match -> Ticket={ticketId} | Topico={topicoIndex} | Score={score} | Resumo={Trunc(resumo, 120)}");

            list.Add((ticketId, topicoIndex, resumo, tagsStr, solucao, criadoEm));
        }

        return list;
    }

    private static object ToDb(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    public static bool SolucaoEhReutilizavel(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.ToLowerInvariant();

        // frases de "resolvido sem dizer como"
        string[] vagas = {
        "via acesso remoto", "via remoto", "acesso remoto",
        "ajuste realizado", "ajuste feito", "configuração realizada",
        "configurado remotamente", "realizado com sucesso",
        "resolvido", "deu certo", "funcionou", "prontinho"
    };

        // sinais de ação concreta (o que torna útil)
        bool temAcao =
            t.Contains(">") ||                                  // caminho de menu
            Regex.IsMatch(t, @"\b(ctrl|f\d{1,2}|esc|enter)\b") || // tecla/comando
            Regex.IsMatch(t, @"\b(cfop|cst|csosn|ncm)\b") ||      // código fiscal
            Regex.IsMatch(t, @"\b(acesse|clique|aperte|pressione|selecione|v[áa] em|menu|cadastr|informe|preencha|marque)\b");

        bool soVaga = vagas.Any(v => t.Contains(v)) && !temAcao;
        return temAcao && !soVaga;
    }


    private static string? Trunc(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

    public async Task<List<TicketInsight>> ListByCodTicketChamadoCAsync(int codTicketChamadoC)
    {
        const string sql = @"
SELECT
  TopicoIndex,
  TopicoTitulo,
  PerguntaCliente,
  Resumo,
  Categoria,
  Intencao,
  Gravidade,
  Tags,
  CausaProvavel,
  Solucao,
  SolucaoConfirmada,
  Confianca
FROM dbo.AiTicketInsights
WHERE CodTicketChamadoC = @CodTicketChamadoC
ORDER BY TopicoIndex;";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CodTicketChamadoC", SqlDbType.Int).Value = codTicketChamadoC;

        var list = new List<TicketInsight>();

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var tags = (rd["Tags"] as string ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            list.Add(new TicketInsight
            {
                topicoIndex = rd["TopicoIndex"] is int ti ? ti : 1,
                topicoTitulo = rd["TopicoTitulo"] as string,

                perguntaCliente = rd["PerguntaCliente"] as string,
                resumo = rd["Resumo"] as string,
                categoria = rd["Categoria"] as string,
                intencao = rd["Intencao"] as string,
                gravidade = rd["Gravidade"] as string,
                tags = tags.Count == 0 ? null : tags,

                causaProvavel = rd["CausaProvavel"] as string,
                solucao = rd["Solucao"] as string,
                solucaoConfirmada =
                    rd["SolucaoConfirmada"] != DBNull.Value &&
                    Convert.ToBoolean(rd["SolucaoConfirmada"]),
                confianca =
                    rd["Confianca"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(rd["Confianca"])
            });
        }

        return list;
    }
}