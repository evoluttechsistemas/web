using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

public sealed class EmbeddingIndexerService
{
    private readonly string _cs;
    private readonly OpenAiClient _openAi;

    public EmbeddingIndexerService(IConfiguration config, OpenAiClient openAi)
    {
        _cs = config.GetConnectionString("Connection")!;
        _openAi = openAi;
    }

    // 1) Quebra cada doc em pedaços e gera embedding de cada pedaço
    public async Task IndexarDocsAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        // lê docs publicados + blocos
        var docs = new List<(int Cod, string Slug, string Titulo, string Texto)>();
        await using (var cmd = new SqlCommand(@"
            SELECT T.Codigo, T.Slug, T.Titulo,
                   ISNULL(STUFF((SELECT ' ' + ISNULL(B.Conteudo,'')
                                 FROM DocumentacaoTutorialBloco B
                                 WHERE B.CodDocumentacaoTutorial = T.Codigo
                                 ORDER BY B.Ordem FOR XML PATH(''), TYPE)
                                .value('.', 'nvarchar(max)'),1,1,''),'') AS Texto
            FROM DocumentacaoTutorial T
            WHERE ISNULL(T.Publicado,'N') = 'S';", conn))
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
                docs.Add((rd.GetInt32(0), rd["Slug"]?.ToString() ?? "",
                          rd["Titulo"]?.ToString() ?? "", rd["Texto"]?.ToString() ?? ""));

        // limpa e recria os chunks
        await using (var del = new SqlCommand("DELETE FROM dbo.DocChunk;", conn))
            await del.ExecuteNonQueryAsync();

        foreach (var d in docs)
        {
            var limpo = StripHtml(d.Texto);
            foreach (var pedaco in Chunk(limpo, 900)) // ~900 chars por pedaço
            {
                var entradaParaVetor = $"{d.Titulo}\n{pedaco}";
                var vec = await _openAi.GetEmbeddingAsync(entradaParaVetor);

                await using var ins = new SqlCommand(@"
                    INSERT INTO dbo.DocChunk (CodDocumentacaoTutorial, Slug, Titulo, Texto, Embedding)
                    VALUES (@c,@s,@t,@x,@e);", conn);
                ins.Parameters.AddWithValue("@c", d.Cod);
                ins.Parameters.AddWithValue("@s", d.Slug);
                ins.Parameters.AddWithValue("@t", d.Titulo);
                ins.Parameters.AddWithValue("@x", pedaco);
                ins.Parameters.AddWithValue("@e", JsonSerializer.Serialize(vec));
                await ins.ExecuteNonQueryAsync();
            }
        }
    }

    // 2) Gera embedding de cada insight que tenha solução reutilizável
    public async Task IndexarInsightsAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var rows = new List<(int Cod, string Texto)>();
        await using (var cmd = new SqlCommand(@"
            SELECT Codigo, PerguntaCliente, Resumo, Solucao, Tags
            FROM dbo.AiTicketInsights
            WHERE Embedding IS NULL
  AND Resumo IS NOT NULL AND LTRIM(RTRIM(Resumo)) <> '';", conn))
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
            {
                var texto = $"{rd["PerguntaCliente"]} {rd["Resumo"]} {rd["Tags"]} {rd["Solucao"]}";
                rows.Add((rd.GetInt32(0), texto));
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
    }

    private static IEnumerable<string> Chunk(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;
        for (int i = 0; i < s.Length; i += max)
            yield return s.Substring(i, Math.Min(max, s.Length - i));
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        t = System.Net.WebUtility.HtmlDecode(t);
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }
}