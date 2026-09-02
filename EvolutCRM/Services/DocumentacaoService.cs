using System.Data;
using Microsoft.Data.SqlClient;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

public class DocumentacaoService
{
    private readonly IConfiguration _config;

    public DocumentacaoService(IConfiguration config)
    {
        _config = config;
    }

    private SqlConnection CriarConexao()
    {
        var connectionString =
            _config.GetConnectionString("Connection")
            ?? _config.GetConnectionString("ConexaoPadrao")
            ?? _config.GetConnectionString("EvolutCRM");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string nao configurada.");

        return new SqlConnection(connectionString);
    }

    public async Task<List<DocumentacaoTutorialModel>> ListarAsync(int codEmp, bool somentePublicados = false)
    {
        var lista = new List<DocumentacaoTutorialModel>();

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                   DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                   CASE WHEN Thumbnail IS NOT NULL THEN 1 ELSE 0 END AS TemThumbnail,
                   ThumbnailMime, Sistema
              FROM DocumentacaoTutorial
             WHERE CodEmp = @CodEmp
               AND (@SomentePublicados = 0 OR Publicado = 'S')
             ORDER BY Categoria, Ordem, Titulo
            """;
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@SomentePublicados", somentePublicados ? 1 : 0);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(MapTutorialLista(rd));

        return lista;
    }

    public async Task<DocumentacaoTutorialModel?> BuscarAsync(int codigo, int codEmp)
    {
        DocumentacaoTutorialModel? tutorial = null;

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                       DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                       Thumbnail, ThumbnailMime, Sistema
                  FROM DocumentacaoTutorial
                 WHERE Codigo = @Codigo
                   AND CodEmp = @CodEmp
                """;
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
                tutorial = MapTutorial(rd);
        }

        if (tutorial != null)
            tutorial.Blocos = await ListarBlocosAsync(codigo, codEmp, con);

        return tutorial;
    }

    public async Task<DocumentacaoTutorialModel?> BuscarPublicadoPorSlugAsync(string slug, int codEmp)
    {
        DocumentacaoTutorialModel? tutorial = null;

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                       DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                       Thumbnail, ThumbnailMime, Sistema
                  FROM DocumentacaoTutorial
                 WHERE Slug = @Slug
                   AND CodEmp = @CodEmp
                   AND Publicado = 'S'
                """;
            cmd.Parameters.AddWithValue("@Slug", slug);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
                tutorial = MapTutorial(rd);
        }

        if (tutorial != null)
            tutorial.Blocos = await ListarBlocosAsync(tutorial.Codigo, codEmp, con);

        return tutorial;
    }

    public async Task<int> SalvarAsync(DocumentacaoTutorialModel tutorial)
    {
        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var tran = await con.BeginTransactionAsync();

        try
        {
            if (tutorial.Codigo == 0)
            {
                await using var cmd = con.CreateCommand();
                cmd.Transaction = (SqlTransaction)tran;
                cmd.CommandText = """
                    INSERT INTO DocumentacaoTutorial
                        (CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                         Thumbnail, ThumbnailMime, UsuarioCriacao, UsuarioAlteracao, Sistema)
                    OUTPUT INSERTED.Codigo
                    VALUES
                        (@CodEmp, @Titulo, @Descricao, @Categoria, @UrlThumbnail, @Slug, @Publicado, @Ordem,
                         @Thumbnail, @ThumbnailMime, @UsuarioCriacao, @UsuarioAlteracao, @Sistema)
                    """;
                PreencherParametrosTutorial(cmd, tutorial);
                tutorial.Codigo = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            else
            {
                await using var cmd = con.CreateCommand();
                cmd.Transaction = (SqlTransaction)tran;
                cmd.CommandText = """
                    UPDATE DocumentacaoTutorial
                       SET Titulo = @Titulo,
                           Descricao = @Descricao,
                           Categoria = @Categoria,
                           UrlThumbnail = @UrlThumbnail,
                           Slug = @Slug,
                           Publicado = @Publicado,
                           Ordem = @Ordem,
                           DataHoraAlteracao = GETDATE(),
                           UsuarioAlteracao = @UsuarioAlteracao,
                           Thumbnail = @Thumbnail,
                           ThumbnailMime = @ThumbnailMime,
                           Sistema = @Sistema
                     WHERE Codigo = @Codigo
                       AND CodEmp = @CodEmp
                    """;
                PreencherParametrosTutorial(cmd, tutorial);
                cmd.Parameters.AddWithValue("@Codigo", tutorial.Codigo);
                await cmd.ExecuteNonQueryAsync();

                await using var del = con.CreateCommand();
                del.Transaction = (SqlTransaction)tran;
                del.CommandText = """
                    DELETE FROM DocumentacaoTutorialBloco
                     WHERE CodDocumentacaoTutorial = @Codigo
                       AND CodEmp = @CodEmp
                    """;
                del.Parameters.AddWithValue("@Codigo", tutorial.Codigo);
                del.Parameters.AddWithValue("@CodEmp", tutorial.CodEmp);
                await del.ExecuteNonQueryAsync();
            }

            foreach (var bloco in tutorial.Blocos.OrderBy(x => x.Ordem))
                await InserirBlocoAsync(con, (SqlTransaction)tran, tutorial.Codigo, tutorial.CodEmp, bloco);

            if ((tutorial.Sistema ?? "HELP") == "HELP" && tutorial.Publicado == "S")
            {
                await using var cmdChangelog = con.CreateCommand();
                cmdChangelog.Transaction = (SqlTransaction)tran;
                cmdChangelog.CommandText = """
                    IF NOT EXISTS (
                        SELECT 1
                          FROM ParametrosHelp
                         WHERE CodEmp = @CodEmp
                           AND Tipo = 'CHANGELOG'
                           AND Referencia = @CodDocumentacao
                           AND Sistema = 'HELP'
                    )
                    INSERT INTO ParametrosHelp
                        (CodEmp, Tipo, Sistema, Versao, Titulo, Descricao, Icone, TipoAlteracao,
                         DataHora, Ativo, Destaque, Referencia)
                    VALUES
                        (@CodEmp, 'CHANGELOG', 'HELP', @Versao, @Titulo, @Descricao, 'ti-file-text',
                         'NOVO', GETDATE(), 'S', 'N', @CodDocumentacao)
                    """;
                cmdChangelog.Parameters.AddWithValue("@CodEmp", tutorial.CodEmp);
                cmdChangelog.Parameters.AddWithValue("@CodDocumentacao", tutorial.Slug);
                cmdChangelog.Parameters.AddWithValue("@Versao", DateTime.Now.ToString("yyyy.MM"));
                cmdChangelog.Parameters.AddWithValue("@Titulo", $"Nova documentação: {tutorial.Titulo}");
                cmdChangelog.Parameters.AddWithValue("@Descricao",
                    string.IsNullOrWhiteSpace(tutorial.Descricao)
                        ? $"Uma nova documentação foi adicionada na categoria {tutorial.Categoria}."
                        : tutorial.Descricao);
                await cmdChangelog.ExecuteNonQueryAsync();
            }

            await tran.CommitAsync();
            return tutorial.Codigo;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    public async Task ExcluirAsync(int codigo, int codEmp)
    {
        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var tran = await con.BeginTransactionAsync();

        try
        {
            await using (var delBlocos = con.CreateCommand())
            {
                delBlocos.Transaction = (SqlTransaction)tran;
                delBlocos.CommandText = """
                    DELETE FROM DocumentacaoTutorialBloco
                     WHERE CodDocumentacaoTutorial = @Codigo
                       AND CodEmp = @CodEmp
                    """;
                delBlocos.Parameters.AddWithValue("@Codigo", codigo);
                delBlocos.Parameters.AddWithValue("@CodEmp", codEmp);
                await delBlocos.ExecuteNonQueryAsync();
            }

            await using (var delTutorial = con.CreateCommand())
            {
                delTutorial.Transaction = (SqlTransaction)tran;
                delTutorial.CommandText = """
                    DELETE FROM DocumentacaoTutorial
                     WHERE Codigo = @Codigo
                       AND CodEmp = @CodEmp
                    """;
                delTutorial.Parameters.AddWithValue("@Codigo", codigo);
                delTutorial.Parameters.AddWithValue("@CodEmp", codEmp);
                await delTutorial.ExecuteNonQueryAsync();
            }

            await tran.CommitAsync();
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    private static async Task InserirBlocoAsync(
        SqlConnection con,
        SqlTransaction tran,
        int codTutorial,
        int codEmp,
        DocumentacaoTutorialBlocoModel bloco)
    {
        await using var cmd = con.CreateCommand();
        cmd.Transaction = tran;
        cmd.CommandText = """
            INSERT INTO DocumentacaoTutorialBloco
                (CodEmp, CodDocumentacaoTutorial, Tipo, Titulo, Conteudo, UrlMidia, UrlLink,
                 CodDocumentacaoRelacionada, Ordem)
            VALUES
                (@CodEmp, @CodDocumentacaoTutorial, @Tipo, @Titulo, @Conteudo, @UrlMidia, @UrlLink,
                 @CodDocumentacaoRelacionada, @Ordem)
            """;
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@CodDocumentacaoTutorial", codTutorial);
        cmd.Parameters.AddWithValue("@Tipo", bloco.Tipo);
        cmd.Parameters.AddWithValue("@Titulo", bloco.Titulo ?? "");
        cmd.Parameters.AddWithValue("@Conteudo", bloco.Conteudo ?? "");
        cmd.Parameters.AddWithValue("@UrlMidia", bloco.UrlMidia ?? "");
        cmd.Parameters.AddWithValue("@UrlLink", bloco.UrlLink ?? "");
        cmd.Parameters.AddWithValue("@CodDocumentacaoRelacionada", bloco.CodDocumentacaoRelacionada.HasValue ? bloco.CodDocumentacaoRelacionada.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@Ordem", bloco.Ordem);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<DocumentacaoTutorialBlocoModel>> ListarBlocosAsync(int codTutorial, int codEmp, SqlConnection con)
    {
        var blocos = new List<DocumentacaoTutorialBlocoModel>();

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Codigo, CodEmp, CodDocumentacaoTutorial, Tipo, Titulo, Conteudo, UrlMidia, UrlLink,
                   CodDocumentacaoRelacionada, Ordem
              FROM DocumentacaoTutorialBloco
             WHERE CodDocumentacaoTutorial = @Codigo
               AND CodEmp = @CodEmp
             ORDER BY Ordem, Codigo
            """;
        cmd.Parameters.AddWithValue("@Codigo", codTutorial);
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            blocos.Add(new DocumentacaoTutorialBlocoModel
            {
                Codigo = rd.GetInt32(rd.GetOrdinal("Codigo")),
                CodEmp = rd.GetInt32(rd.GetOrdinal("CodEmp")),
                CodDocumentacaoTutorial = rd.GetInt32(rd.GetOrdinal("CodDocumentacaoTutorial")),
                Tipo = rd.GetString(rd.GetOrdinal("Tipo")),
                Titulo = rd.GetString(rd.GetOrdinal("Titulo")),
                Conteudo = rd.GetString(rd.GetOrdinal("Conteudo")),
                UrlMidia = rd.GetString(rd.GetOrdinal("UrlMidia")),
                UrlLink = rd.GetString(rd.GetOrdinal("UrlLink")),
                CodDocumentacaoRelacionada = rd.IsDBNull(rd.GetOrdinal("CodDocumentacaoRelacionada")) ? null : rd.GetInt32(rd.GetOrdinal("CodDocumentacaoRelacionada")),
                Ordem = rd.GetInt32(rd.GetOrdinal("Ordem"))
            });
        }

        return blocos;
    }

    private static DocumentacaoTutorialModel MapTutorialLista(SqlDataReader rd)
    {
        var temThumbnail = rd.GetInt32(rd.GetOrdinal("TemThumbnail")) == 1;

        return new DocumentacaoTutorialModel
        {
            Codigo = rd.GetInt32(rd.GetOrdinal("Codigo")),
            CodEmp = rd.GetInt32(rd.GetOrdinal("CodEmp")),
            Titulo = rd.GetString(rd.GetOrdinal("Titulo")),
            Descricao = rd.GetString(rd.GetOrdinal("Descricao")),
            Categoria = rd.GetString(rd.GetOrdinal("Categoria")),
            UrlThumbnail = rd.GetString(rd.GetOrdinal("UrlThumbnail")),
            Slug = rd.GetString(rd.GetOrdinal("Slug")),
            Publicado = rd.GetString(rd.GetOrdinal("Publicado")),
            Ordem = rd.GetInt32(rd.GetOrdinal("Ordem")),
            DataHoraCriacao = rd.GetDateTime(rd.GetOrdinal("DataHoraCriacao")),
            DataHoraAlteracao = rd.GetDateTime(rd.GetOrdinal("DataHoraAlteracao")),
            UsuarioCriacao = rd.GetString(rd.GetOrdinal("UsuarioCriacao")),
            UsuarioAlteracao = rd.GetString(rd.GetOrdinal("UsuarioAlteracao")),
            Thumbnail = temThumbnail ? Array.Empty<byte>() : null,
            ThumbnailMime = rd.IsDBNull(rd.GetOrdinal("ThumbnailMime")) ? null : rd.GetString(rd.GetOrdinal("ThumbnailMime")),
            Sistema = rd.IsDBNull(rd.GetOrdinal("Sistema")) ? "HELP" : rd.GetString(rd.GetOrdinal("Sistema"))
        };
    }

    private static DocumentacaoTutorialModel MapTutorial(SqlDataReader rd)
    {
        return new DocumentacaoTutorialModel
        {
            Codigo = rd.GetInt32(rd.GetOrdinal("Codigo")),
            CodEmp = rd.GetInt32(rd.GetOrdinal("CodEmp")),
            Titulo = rd.GetString(rd.GetOrdinal("Titulo")),
            Descricao = rd.GetString(rd.GetOrdinal("Descricao")),
            Categoria = rd.GetString(rd.GetOrdinal("Categoria")),
            UrlThumbnail = rd.GetString(rd.GetOrdinal("UrlThumbnail")),
            Slug = rd.GetString(rd.GetOrdinal("Slug")),
            Publicado = rd.GetString(rd.GetOrdinal("Publicado")),
            Ordem = rd.GetInt32(rd.GetOrdinal("Ordem")),
            DataHoraCriacao = rd.GetDateTime(rd.GetOrdinal("DataHoraCriacao")),
            DataHoraAlteracao = rd.GetDateTime(rd.GetOrdinal("DataHoraAlteracao")),
            UsuarioCriacao = rd.GetString(rd.GetOrdinal("UsuarioCriacao")),
            UsuarioAlteracao = rd.GetString(rd.GetOrdinal("UsuarioAlteracao")),
            Thumbnail = rd.IsDBNull(rd.GetOrdinal("Thumbnail")) ? null : (byte[])rd["Thumbnail"],
            ThumbnailMime = rd.IsDBNull(rd.GetOrdinal("ThumbnailMime")) ? null : rd.GetString(rd.GetOrdinal("ThumbnailMime")),
            Sistema = rd.IsDBNull(rd.GetOrdinal("Sistema")) ? "HELP" : rd.GetString(rd.GetOrdinal("Sistema"))
        };
    }

    private static void PreencherParametrosTutorial(SqlCommand cmd, DocumentacaoTutorialModel tutorial)
    {
        cmd.Parameters.AddWithValue("@CodEmp", tutorial.CodEmp);
        cmd.Parameters.AddWithValue("@Titulo", tutorial.Titulo ?? "");
        cmd.Parameters.AddWithValue("@Descricao", tutorial.Descricao ?? "");
        cmd.Parameters.AddWithValue("@Categoria", tutorial.Categoria ?? "");
        cmd.Parameters.AddWithValue("@UrlThumbnail", tutorial.UrlThumbnail ?? "");
        cmd.Parameters.AddWithValue("@Slug", tutorial.Slug ?? "");
        cmd.Parameters.AddWithValue("@Publicado", tutorial.Publicado ?? "N");
        cmd.Parameters.AddWithValue("@Ordem", tutorial.Ordem);
        cmd.Parameters.AddWithValue("@UsuarioCriacao", tutorial.UsuarioCriacao ?? "");
        cmd.Parameters.AddWithValue("@UsuarioAlteracao", tutorial.UsuarioAlteracao ?? tutorial.UsuarioCriacao ?? "");

        var thumbParam = cmd.Parameters.Add("@Thumbnail", SqlDbType.VarBinary, -1);
        thumbParam.Value = (object?)tutorial.Thumbnail ?? DBNull.Value;

        cmd.Parameters.AddWithValue("@ThumbnailMime", (object?)tutorial.ThumbnailMime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sistema", tutorial.Sistema ?? "HELP");
    }
}
