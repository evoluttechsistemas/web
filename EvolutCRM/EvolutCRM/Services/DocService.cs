using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

public class DocService
{
    private readonly IConfiguration _config;
    public DocService(IConfiguration config) => _config = config;

    public async Task<List<(int Id, int CodEmp, string Slug, string Title, string Content)>> SearchAsync(
        string question, int top = 3, CancellationToken ct = default)
    {
        var cs = _config.GetConnectionString("Connection");

        LogHelper.Log("[DOCSERVICE] INICIANDO BUSCA");
        LogHelper.Log("[DOCSERVICE] PERGUNTA RECEBIDA: " + question);
        LogHelper.Log("[DOCSERVICE] TOP: " + top);

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);

        LogHelper.Log("[DOCSERVICE] SQL SERVER CONECTADO COM SUCESSO");

        var q = ExpandQuery(question);
        LogHelper.Log("[DOCSERVICE] QUERY EXPANDIDA: " + q);

        var resultado = await SearchLikeAsync(conn, q, top, ct);
        LogHelper.Log("[DOCSERVICE] ENCONTRADOS: " + resultado.Count);

        foreach (var d in resultado.Take(5))
            LogHelper.Log("[DOCSERVICE][RESULT] " + d.Title);

        return resultado;
    }

    public async Task<string> TestConnectionAsync()
    {
        try
        {
            var cs = _config.GetConnectionString("Connection");
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();

            LogHelper.Log("[DOCSERVICE][TEST] SQL SERVER CONECTADO COM SUCESSO");

            using var cmd = new SqlCommand("SELECT COUNT(*) FROM DocumentacaoTutorial", conn);
            var total = await cmd.ExecuteScalarAsync();

            LogHelper.Log("[DOCSERVICE][TEST] TOTAL DE DOCS: " + total);
            return $"SQL Server conectado com sucesso. Total de docs: {total}";
        }
        catch (Exception ex)
        {
            LogHelper.Log("[DOCSERVICE][TEST] ERRO SQL SERVER: " + ex);
            return "ERRO SQL SERVER: " + ex.Message;
        }
    }

    private static string ExpandQuery(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return "";

        var x = q.Trim();

        if (x.Contains("pdv", StringComparison.OrdinalIgnoreCase))
            x += " frente de caixa ponto de venda caixa";

        if (x.Contains("nfe", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("nf-e", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("nota fiscal", StringComparison.OrdinalIgnoreCase))
            x += " emissao danfe sefaz";

        if (x.Contains("financeiro", StringComparison.OrdinalIgnoreCase))
            x += " contas pagar receber boleto caixa baixa titulo vencimento";

        if (x.Contains("estoque", StringComparison.OrdinalIgnoreCase))
            x += " produto inventario saldo movimentacao entrada saida";

        if (x.Contains("balanca", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("balança", StringComparison.OrdinalIgnoreCase))
            x += " pesagem etiqueta serial porta comunicacao";

        if (x.Contains("whatsapp", StringComparison.OrdinalIgnoreCase))
            x += " atendimento conversa mensagem telefone cliente";

        if (x.Contains("sped", StringComparison.OrdinalIgnoreCase))
            x += " fiscal icms pis cofins bloco sped fiscal";

        if (x.Contains("xml", StringComparison.OrdinalIgnoreCase))
            x += " importacao nota fiscal nfe entrada";

        return x;
    }

    private static async Task<List<(int Id, int CodEmp, string Slug, string Title, string Content)>> SearchLikeAsync(
        SqlConnection conn, string q, int top, CancellationToken ct)
    {
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(w => new string(w.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray()))
                     .Where(w => !string.IsNullOrWhiteSpace(w))
                     .Where(w => w.Length >= 3)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(8)
                     .ToList();

        if (words.Count == 0)
            return new List<(int Id, int CodEmp, string Slug, string Title, string Content)>();

        var whereParts = new List<string>();
        var scoreParts = new List<string>();

        for (int i = 0; i < words.Count; i++)
        {
            whereParts.Add($@"(
                D.Titulo LIKE '%' + @w{i} + '%'
                OR D.Descricao LIKE '%' + @w{i} + '%'
                OR D.Categoria LIKE '%' + @w{i} + '%'
                OR D.Slug LIKE '%' + @w{i} + '%'
                OR D.ConteudoBlocos LIKE '%' + @w{i} + '%'
            )");

            scoreParts.Add($@"(
                CASE WHEN D.Titulo LIKE '%' + @w{i} + '%' THEN 10 ELSE 0 END +
                CASE WHEN D.Categoria LIKE '%' + @w{i} + '%' THEN 6 ELSE 0 END +
                CASE WHEN D.Slug LIKE '%' + @w{i} + '%' THEN 5 ELSE 0 END +
                CASE WHEN D.Descricao LIKE '%' + @w{i} + '%' THEN 4 ELSE 0 END +
                CASE WHEN D.ConteudoBlocos LIKE '%' + @w{i} + '%' THEN 2 ELSE 0 END
            )");
        }

        var scoreExpr = string.Join(" + ", scoreParts);

        var sql = $@"
            ;WITH Docs AS (
                SELECT
                    T.Codigo,
                    T.CodEmp, 
                    T.Slug,
                    T.Titulo,
                    ISNULL(T.Descricao, '') AS Descricao,
                    ISNULL(T.Categoria, '') AS Categoria,
                    ISNULL(
                        STUFF((
                            SELECT ' ' + ISNULL(B.Conteudo, '')
                            FROM DocumentacaoTutorialBloco B
                            WHERE B.CodDocumentacaoTutorial = T.Codigo
                            ORDER BY B.Ordem
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'), 1, 1, ''),
                    '') AS ConteudoBlocos
                FROM DocumentacaoTutorial T
                WHERE ISNULL(T.Publicado, 'N') = 'S'
            )
            SELECT TOP (@top)
                D.Codigo,
                D.CodEmp,
                D.Slug,
                D.Titulo,
                D.Descricao,
                D.ConteudoBlocos,
                ({scoreExpr}) AS Score
            FROM Docs D
            WHERE ({string.Join(" OR ", whereParts)})
            ORDER BY ({scoreExpr}) DESC, D.Titulo;";

        using var cmd = new SqlCommand(sql, conn);

        for (int i = 0; i < words.Count; i++)
            cmd.Parameters.AddWithValue($"@w{i}", words[i]);

        cmd.Parameters.AddWithValue("@top", top);

        return await ReadAsync(cmd, ct);
    }

    private static async Task<List<(int Id, int CodEmp, string Slug, string Title, string Content)>> ReadAsync(
    SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<(int, int, string, string, string)>();

        try
        {
            using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                var id = Convert.ToInt32(rd["Codigo"]);
                var codEmp = Convert.ToInt32(rd["CodEmp"]);
                var slug = rd["Slug"]?.ToString() ?? "";
                var title = rd["Titulo"]?.ToString() ?? "";
                var descricao = rd["Descricao"]?.ToString() ?? "";
                var blocos = rd["ConteudoBlocos"]?.ToString() ?? "";

                var content = string.IsNullOrWhiteSpace(blocos)
                    ? descricao
                    : descricao + " " + blocos;

                list.Add((id, codEmp, slug, title, content));
            }
        }
        catch (Exception ex)
        {
            LogHelper.Log("[DOCSERVICE][ERRO SQL] " + ex.Message);
        }

        return list;
    }
}