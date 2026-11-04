using System.Data;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Converte para arquivo as imagens que ainda estão no banco (só no servidor de produção):
///   • imagens em base64 no conteúdo das documentações  → /uploads/documentacoes/{CodEmp}/
///   • capas das documentações gravadas em binário      → /uploads/documentacoes/{CodEmp}/ (caminho em UrlThumbnail)
///   • imagens em base64 no conteúdo e no layout do blog → /uploads/blog/ (URL completa, o site é outro domínio)
/// Roda 1 minuto depois que o HELP sobe e depois a cada 6 horas (pega o que foi editado
/// rodando o HELP no computador de desenvolvimento, que mantém base64).
/// Não altera datas de alteração nem o índice da IA. Se algo falhar, tenta na próxima rodada.
/// </summary>
public class MigracaoImagensService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ImagemArmazenamentoService _imagens;
    private readonly IServiceScopeFactory _scopeFactory;

    public MigracaoImagensService(IConfiguration config, ImagemArmazenamentoService imagens, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _imagens = imagens;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_imagens.GravaEmArquivo) return; // dev: não mexe no banco de produção

        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await MigrarAsync(ct);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[MIGRA IMG] Erro: {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch { return; }
        }
    }

    private SqlConnection CriarConexao() => new(
        _config.GetConnectionString("Connection")
        ?? _config.GetConnectionString("ConexaoPadrao")
        ?? _config.GetConnectionString("EvolutCRM"));

    public async Task MigrarAsync(CancellationToken ct = default)
    {
        int blocos = 0, capas = 0, posts = 0;

        await using var con = CriarConexao();
        await con.OpenAsync(ct);

        // 1) Conteúdo das documentações
        foreach (var (codigo, codEmp) in await ListarAsync(con,
                     "SELECT Codigo, CodEmp FROM DocumentacaoTutorialBloco WHERE Conteudo LIKE '%data:image%'", ct))
        {
            try
            {
                var html = await LerTextoAsync(con, "SELECT Conteudo FROM DocumentacaoTutorialBloco WHERE Codigo = @id", codigo, ct);
                var (novo, alterado) = await _imagens.ExtrairBase64Async(html, $"documentacoes/{codEmp}");
                if (!alterado) continue;

                await using var up = con.CreateCommand();
                up.CommandText = "UPDATE DocumentacaoTutorialBloco SET Conteudo = @c WHERE Codigo = @id";
                up.Parameters.Add("@c", SqlDbType.NVarChar, -1).Value = novo;
                up.Parameters.AddWithValue("@id", codigo);
                await up.ExecuteNonQueryAsync(ct);
                blocos++;
            }
            catch (Exception ex) { LogHelper.Log($"[MIGRA IMG] Bloco #{codigo}: {ex.Message}"); }
        }

        // 2) Capas das documentações (binário → arquivo)
        bool colunaOk;
        using (var scope = _scopeFactory.CreateScope())
            colunaOk = await scope.ServiceProvider.GetRequiredService<DocumentacaoService>().GarantirColunaCapaAsync();

        if (colunaOk)
        {
            foreach (var (codigo, codEmp) in await ListarAsync(con,
                         "SELECT Codigo, CodEmp FROM DocumentacaoTutorial WHERE Thumbnail IS NOT NULL", ct))
            {
                try
                {
                    byte[]? dados = null;
                    string mime = "image/png";
                    await using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Thumbnail, ThumbnailMime FROM DocumentacaoTutorial WHERE Codigo = @id";
                        cmd.Parameters.AddWithValue("@id", codigo);
                        await using var rd = await cmd.ExecuteReaderAsync(ct);
                        if (await rd.ReadAsync(ct) && !rd.IsDBNull(0))
                        {
                            dados = (byte[])rd[0];
                            if (!rd.IsDBNull(1)) mime = rd.GetString(1);
                        }
                    }
                    if (dados is not { Length: > 0 }) continue;

                    var url = await _imagens.SalvarAsync(dados, mime, $"documentacoes/{codEmp}");

                    await using var up = con.CreateCommand();
                    up.CommandText = """
                        UPDATE DocumentacaoTutorial
                           SET UrlThumbnail = @u, Thumbnail = NULL, ThumbnailMime = NULL
                         WHERE Codigo = @id AND Thumbnail IS NOT NULL
                        """;
                    up.Parameters.AddWithValue("@u", url);
                    up.Parameters.AddWithValue("@id", codigo);
                    await up.ExecuteNonQueryAsync(ct);
                    capas++;
                }
                catch (Exception ex) { LogHelper.Log($"[MIGRA IMG] Capa da documentação #{codigo}: {ex.Message}"); }
            }
        }

        // 3) Blog: conteúdo e layout (URL completa: o site do blog é outro domínio)
        foreach (var (idPost, _) in await ListarAsync(con,
                     "SELECT IdPost, 0 FROM Posts WHERE Conteudo LIKE '%data:image%' OR Layout LIKE '%data:image%'", ct))
        {
            try
            {
                string? conteudo = null, layout = null;
                await using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "SELECT Conteudo, Layout FROM Posts WHERE IdPost = @id";
                    cmd.Parameters.AddWithValue("@id", idPost);
                    await using var rd = await cmd.ExecuteReaderAsync(ct);
                    if (await rd.ReadAsync(ct))
                    {
                        conteudo = rd.IsDBNull(0) ? null : rd.GetString(0);
                        layout = rd.IsDBNull(1) ? null : rd.GetString(1);
                    }
                }

                var (c2, altC) = await _imagens.ExtrairBase64Async(conteudo, "blog", _imagens.UrlBase);
                var (l2, altL) = await _imagens.ExtrairBase64Async(layout, "blog", _imagens.UrlBase);
                if (!altC && !altL) continue;

                await using var up = con.CreateCommand();
                up.CommandText = "UPDATE Posts SET Conteudo = @c, Layout = @l WHERE IdPost = @id";
                up.Parameters.Add("@c", SqlDbType.NVarChar, -1).Value = altC ? c2 : (object?)conteudo ?? DBNull.Value;
                up.Parameters.Add("@l", SqlDbType.NVarChar, -1).Value = altL ? l2 : (object?)layout ?? DBNull.Value;
                up.Parameters.AddWithValue("@id", idPost);
                await up.ExecuteNonQueryAsync(ct);
                posts++;
            }
            catch (Exception ex) { LogHelper.Log($"[MIGRA IMG] Post #{idPost}: {ex.Message}"); }
        }

        if (blocos + capas + posts > 0)
            LogHelper.Log($"[MIGRA IMG] Convertidos para arquivo: {blocos} bloco(s) de documentação, {capas} capa(s), {posts} post(s) do blog.");
    }

    private static async Task<List<(int, int)>> ListarAsync(SqlConnection con, string sql, CancellationToken ct)
    {
        var lista = new List<(int, int)>();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add((rd.GetInt32(0), rd.GetInt32(1)));
        return lista;
    }

    private static async Task<string?> LerTextoAsync(SqlConnection con, string sql, int id, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is DBNull or null ? null : (string)r;
    }
}
