using System.Text;
using System.Text.Json;
using EvolutCRM.Helpers;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Gera os embeddings que a IA usa para buscar documentação e casos anteriores.
/// - Por partes: um ticket (ao gerar insight) ou uma documentação (ao salvar/excluir).
/// - Pendentes: o que ainda não está no índice (rotina da madrugada e /api/reindexar).
/// - Completo: apaga e refaz todas as documentações (/api/reindexar?completo=true).
/// </summary>
public sealed class EmbeddingIndexerService
{
    private readonly string _cs;
    private readonly OpenAiClient _openAi;

    // Mesmo critério do EmbeddingIndex: só gera embedding do que vai ser usado
    private const string FiltroInsights = @"
              ISNULL(LTRIM(RTRIM(Resumo)), '')  <> ''
              AND ISNULL(LTRIM(RTRIM(Solucao)), '') <> ''
              AND (SolucaoConfirmada = 1 OR ISNULL(Confianca, 0) >= 70)";

    // Documentação que serve para responder cliente (os tutoriais internos do HELP ficam de fora)
    private const string FiltroDocs = @"
              ISNULL(T.Publicado,'N') = 'S'
              AND ISNULL(T.Sistema,'') <> 'HELP'";

    public EmbeddingIndexerService(IConfiguration config, OpenAiClient openAi)
    {
        _cs = config.GetConnectionString("Connection")!;
        _openAi = openAi;
    }

    // ─────────────────────────── Documentações ───────────────────────────

    /// <summary>Completo: apaga todos os pedaços e refaz todas as documentações.</summary>
    public async Task IndexarDocsAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var docs = await LerDocsAsync(conn, null);

        await using (var del = new SqlCommand("DELETE FROM dbo.DocChunk;", conn))
            await del.ExecuteNonQueryAsync();

        foreach (var d in docs)
            await GravarPedacosAsync(conn, d);
    }

    /// <summary>
    /// Refaz só uma documentação. Se ela foi excluída, despublicada ou é do HELP, só tira do índice.
    /// Chamado ao salvar e ao excluir no editor de documentação.
    /// </summary>
    public async Task<int> IndexarDocAsync(int codDoc)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using (var del = new SqlCommand("DELETE FROM dbo.DocChunk WHERE CodDocumentacaoTutorial = @c;", conn))
        {
            del.Parameters.AddWithValue("@c", codDoc);
            await del.ExecuteNonQueryAsync();
        }

        var docs = await LerDocsAsync(conn, codDoc);
        var total = 0;
        foreach (var d in docs)
            total += await GravarPedacosAsync(conn, d);
        return total;
    }

    // ─────────────────────────── Insights ───────────────────────────

    /// <summary>Gera embedding de todos os insights úteis que ainda não têm.</summary>
    public Task<int> IndexarInsightsAsync() => IndexarInsightsInternoAsync(null);

    /// <summary>Gera embedding dos insights de um ticket (chamado logo depois de gerar o insight).</summary>
    public Task<int> IndexarInsightsDoTicketAsync(int codTicket) => IndexarInsightsInternoAsync(codTicket);

    private async Task<int> IndexarInsightsInternoAsync(int? codTicket)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var rows = new List<(int Cod, string Texto)>();
        await using (var cmd = new SqlCommand($@"
            SELECT Codigo, PerguntaCliente, Resumo, Solucao, Tags
            FROM dbo.AiTicketInsights
            WHERE Embedding IS NULL
              AND {FiltroInsights}
              AND (@Ticket IS NULL OR CodTicketChamadoC = @Ticket);", conn))
        {
            cmd.Parameters.Add("@Ticket", System.Data.SqlDbType.Int).Value = (object?)codTicket ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var texto = $"{rd["PerguntaCliente"]} {rd["Resumo"]} {rd["Tags"]} {rd["Solucao"]}";
                rows.Add((rd.GetInt32(0), texto));
            }
        }

        foreach (var r in rows)
        {
            var vec = await _openAi.GetEmbeddingAsync(r.Texto);
            await using var up = new SqlCommand(
                "UPDATE dbo.AiTicketInsights SET Embedding=@e WHERE Codigo=@c;", conn);
            up.Parameters.AddWithValue("@e", JsonSerializer.Serialize(vec));
            up.Parameters.AddWithValue("@c", r.Cod);
            await up.ExecuteNonQueryAsync();
        }

        return rows.Count;
    }

    // ─────────────────────────── Pendentes ───────────────────────────

    /// <summary>
    /// Coloca no índice o que está faltando, sem refazer o que já está certo:
    /// tira documentação excluída/despublicada/do HELP, indexa documentação publicada sem pedaços
    /// e gera embedding dos insights úteis que ainda não têm.
    /// </summary>
    public async Task<(int DocsRemovidas, int DocsIndexadas, int Insights)> IndexarPendentesAsync()
    {
        int removidas, indexadas = 0;

        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();

            await using (var del = new SqlCommand($@"
                DELETE C FROM dbo.DocChunk C
                WHERE NOT EXISTS (
                    SELECT 1 FROM DocumentacaoTutorial T
                    WHERE T.Codigo = C.CodDocumentacaoTutorial
                      AND {FiltroDocs});", conn))
                removidas = await del.ExecuteNonQueryAsync();

            var semPedacos = new List<int>();
            await using (var cmd = new SqlCommand($@"
                SELECT T.Codigo FROM DocumentacaoTutorial T
                WHERE {FiltroDocs}
                  AND NOT EXISTS (SELECT 1 FROM dbo.DocChunk C WHERE C.CodDocumentacaoTutorial = T.Codigo);", conn))
            await using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    semPedacos.Add(rd.GetInt32(0));

            foreach (var cod in semPedacos)
                foreach (var d in await LerDocsAsync(conn, cod))
                    if (await GravarPedacosAsync(conn, d) > 0) indexadas++;
        }

        var insights = await IndexarInsightsAsync();
        return (removidas, indexadas, insights);
    }

    // ─────────────────────────── Apoio ───────────────────────────

    private sealed record DocTexto(int Cod, string Slug, string Titulo, string Descricao, int CodEmp, string Sistema,
                                   List<DocChunker.Bloco> Blocos);

    private static async Task<List<DocTexto>> LerDocsAsync(SqlConnection conn, int? codDoc)
    {
        var docs = new Dictionary<int, DocTexto>();
        await using var cmd = new SqlCommand($@"
            SELECT T.Codigo, ISNULL(T.Slug,''), ISNULL(T.Titulo,''), ISNULL(T.Descricao,''), T.CodEmp, ISNULL(T.Sistema,''),
                   ISNULL(B.Titulo,''), ISNULL(B.Conteudo,'')
            FROM DocumentacaoTutorial T
            LEFT JOIN DocumentacaoTutorialBloco B
                   ON B.CodDocumentacaoTutorial = T.Codigo AND B.CodEmp = T.CodEmp
            WHERE {FiltroDocs}
              AND (@Doc IS NULL OR T.Codigo = @Doc)
            ORDER BY T.Codigo, B.Ordem, B.Codigo;", conn);
        cmd.Parameters.Add("@Doc", System.Data.SqlDbType.Int).Value = (object?)codDoc ?? DBNull.Value;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var cod = rd.GetInt32(0);
            if (!docs.TryGetValue(cod, out var d))
            {
                d = new DocTexto(cod, rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetString(5),
                                 new List<DocChunker.Bloco>());
                docs[cod] = d;
            }
            var tituloBloco = rd.GetString(6);
            var conteudo = rd.GetString(7);
            if (tituloBloco.Length > 0 || conteudo.Length > 0)
                d.Blocos.Add(new DocChunker.Bloco(tituloBloco, conteudo));
        }
        return docs.Values.ToList();
    }

    /// <summary>
    /// Corta a documentação por subtítulos e passos (DocChunker) e grava um embedding por pedaço.
    /// DocChunk.Titulo guarda o caminho ("Cadastro de Produtos › 3- Aba Tributação"), que a IA vê no contexto.
    /// </summary>
    private async Task<int> GravarPedacosAsync(SqlConnection conn, DocTexto d)
    {
        var total = 0;
        foreach (var p in DocChunker.Cortar(d.Titulo, d.Descricao, d.Blocos))
        {
            var vec = await _openAi.GetEmbeddingAsync($"{p.Caminho}\n{p.Texto}");

            await using var ins = new SqlCommand(@"
                INSERT INTO dbo.DocChunk (CodDocumentacaoTutorial, Slug, Titulo, Texto, Embedding, CodEmp, Sistema)
                VALUES (@c,@s,@t,@x,@e,@emp,@sis);", conn);
            ins.Parameters.AddWithValue("@c", d.Cod);
            ins.Parameters.AddWithValue("@s", d.Slug);
            ins.Parameters.AddWithValue("@t", p.Caminho.Length > 400 ? p.Caminho[..400] : p.Caminho);
            ins.Parameters.AddWithValue("@x", p.Texto);
            ins.Parameters.AddWithValue("@e", JsonSerializer.Serialize(vec));
            ins.Parameters.AddWithValue("@emp", d.CodEmp);
            ins.Parameters.AddWithValue("@sis", d.Sistema);
            await ins.ExecuteNonQueryAsync();
            total++;
        }
        return total;
    }
}
