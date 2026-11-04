using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

public class DocumentacaoService
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmbeddingIndex _index;
    private readonly ImagemArmazenamentoService _imagens;
    private readonly ITimeLimitedDataProtector _previa;

    public DocumentacaoService(IConfiguration config, IServiceScopeFactory scopeFactory, EmbeddingIndex index,
                               ImagemArmazenamentoService imagens, IDataProtectionProvider protecao)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _index = index;
        _imagens = imagens;
        _previa = protecao.CreateProtector("EvolutCRM.DocumentacaoPrevia").ToTimeLimitedDataProtector();
    }

    // ── Link público e pré-visualização ─────────────────────────────────────

    public static readonly TimeSpan ValidadePrevia = TimeSpan.FromHours(12);

    /// <summary>Link público (só abre documentação publicada).</summary>
    public static string UrlPublica(DocumentacaoTutorialModel doc)
        => $"/curso-tutorial/{doc.CodEmp}/{Uri.EscapeDataString(doc.Slug ?? "")}";

    /// <summary>
    /// Link de pré-visualização: abre a última versão salva, mesmo em rascunho.
    /// O token é assinado pelo servidor (não dá para adivinhar ou alterar) e vale 12 horas.
    /// </summary>
    public string UrlPrevia(DocumentacaoTutorialModel doc)
    {
        var token = _previa.Protect($"{doc.Codigo}:{doc.CodEmp}", ValidadePrevia);
        var slug = string.IsNullOrWhiteSpace(doc.Slug) ? "previa" : doc.Slug;
        return $"/curso-tutorial/{doc.CodEmp}/{Uri.EscapeDataString(slug)}?previa={Uri.EscapeDataString(token)}";
    }

    /// <summary>Lê o token da pré-visualização. Null se for inválido ou estiver vencido.</summary>
    public (int Codigo, int CodEmp)? LerTokenPrevia(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var partes = _previa.Unprotect(token, out _).Split(':');
            if (partes.Length == 2 && int.TryParse(partes[0], out var cod) && int.TryParse(partes[1], out var emp))
                return (cod, emp);
        }
        catch { }
        return null;
    }

    // ── Slug ──────────────────────────────────────────────────────────────

    /// <summary>"Como Abrir Ticket?" → "como-abrir-ticket" (sem acento, espaço ou símbolo).</summary>
    public static string NormalizarSlug(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var semAcento = new StringBuilder();
        foreach (var c in texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);
        var slug = Regex.Replace(semAcento.ToString(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 150 ? slug[..150].TrimEnd('-') : slug;
    }

    /// <summary>
    /// Códigos das documentações publicadas cujo texto contém o termo (ignora maiúsculas e acentos).
    /// Usado pela busca da central de ajuda.
    /// </summary>
    // ── Visibilidade: P = pública (central de ajuda + IA), I = interna (só equipe + IA) ──

    private static bool _esquemaOk;
    private static readonly SemaphoreSlim _travaEsquema = new(1, 1);

    /// <summary>Cria a coluna Visibilidade na primeira vez (as documentações existentes ficam 'P', públicas).</summary>
    private async Task GarantirEsquemaAsync()
    {
        if (_esquemaOk) return;
        await _travaEsquema.WaitAsync();
        try
        {
            if (_esquemaOk) return;
            await using var con = CriarConexao();
            await con.OpenAsync();
            await using var cmd = con.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH('DocumentacaoTutorial', 'Visibilidade') IS NULL
                    ALTER TABLE DocumentacaoTutorial
                      ADD Visibilidade CHAR(1) NOT NULL
                          CONSTRAINT DF_DocumentacaoTutorial_Visibilidade DEFAULT ('P');
                """;
            await cmd.ExecuteNonQueryAsync();
            _esquemaOk = true;
        }
        finally { _travaEsquema.Release(); }
    }

    /// <summary>
    /// Link para a equipe abrir uma documentação (fontes da IA no ticket e no assistente):
    /// pré-visualização assinada, que funciona para pública, interna e rascunho.
    /// </summary>
    public string UrlParaEquipe(int codDoc, int codEmp, string? slug)
        => UrlPrevia(new DocumentacaoTutorialModel { Codigo = codDoc, CodEmp = codEmp, Slug = slug ?? "" });

    public async Task<HashSet<int>> BuscarNoConteudoAsync(int codEmp, string termo)
    {
        var achados = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(termo)) return achados;
        await GarantirEsquemaAsync();

        // escapa os curingas do LIKE para buscar o texto literal
        var literal = termo.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT B.CodDocumentacaoTutorial
              FROM DocumentacaoTutorialBloco B
              JOIN DocumentacaoTutorial T
                ON T.Codigo = B.CodDocumentacaoTutorial AND T.CodEmp = B.CodEmp
             WHERE B.CodEmp = @CodEmp
               AND T.Publicado = 'S'
               AND T.Visibilidade = 'P'
               AND CAST(B.Conteudo AS NVARCHAR(MAX)) COLLATE Latin1_General_CI_AI LIKE @Termo
            """;
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@Termo", "%" + literal + "%");
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            achados.Add(rd.GetInt32(0));
        return achados;
    }

    /// <summary>Outra documentação da empresa já usa este slug? Devolve o título dela, ou null.</summary>
    public async Task<string?> SlugEmUsoAsync(string slug, int codEmp, int codigoAtual)
    {
        await using var con = CriarConexao();
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 Titulo
              FROM DocumentacaoTutorial
             WHERE CodEmp = @CodEmp AND Slug = @Slug AND Codigo <> @Codigo
            """;
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@Slug", slug);
        cmd.Parameters.AddWithValue("@Codigo", codigoAtual);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? null : (string)r;
    }

    /// <summary>
    /// Atualiza a documentação no índice da IA em segundo plano (não deixa o salvar mais lento).
    /// Despublicada, excluída ou do HELP: só sai do índice.
    /// </summary>
    private void AtualizarIndiceIA(int codigo)
    {
        if (codigo <= 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var indexer = scope.ServiceProvider.GetRequiredService<EmbeddingIndexerService>();
                var pedacos = await indexer.IndexarDocAsync(codigo);
                await _index.AtualizarDocAsync(codigo);
                LogHelper.Log($"[DOC IA] Documentação #{codigo}: {pedacos} pedaço(s) indexado(s)");
            }
            catch (Exception ex)
            {
                // a rotina da madrugada tenta de novo
                LogHelper.Log($"[DOC IA] Documentação #{codigo}: erro ao indexar: {ex.Message}");
            }
        });
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

    /// <param name="somentePublicados">Central de ajuda: só publicadas E públicas (internas nunca aparecem para o cliente).</param>
    public async Task<List<DocumentacaoTutorialModel>> ListarAsync(int codEmp, bool somentePublicados = false)
    {
        var lista = new List<DocumentacaoTutorialModel>();
        await GarantirEsquemaAsync();

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                   DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                   CASE WHEN Thumbnail IS NOT NULL THEN 1 ELSE 0 END AS TemThumbnail,
                   ThumbnailMime, Sistema, Visibilidade
              FROM DocumentacaoTutorial
             WHERE CodEmp = @CodEmp
               AND (@SomentePublicados = 0 OR (Publicado = 'S' AND Visibilidade = 'P'))
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
        await GarantirEsquemaAsync();

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                       DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                       Thumbnail, ThumbnailMime, Sistema, Visibilidade
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
        {
            tutorial.Blocos = await ListarBlocosAsync(codigo, codEmp, con);
            await MigrarImagensBase64Async(tutorial.Blocos, codEmp, con);
        }

        return tutorial;
    }

    public async Task<DocumentacaoTutorialModel?> BuscarPublicadoPorSlugAsync(string slug, int codEmp)
    {
        DocumentacaoTutorialModel? tutorial = null;
        await GarantirEsquemaAsync();

        await using var con = CriarConexao();
        await con.OpenAsync();

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Codigo, CodEmp, Titulo, Descricao, Categoria, UrlThumbnail, Slug, Publicado, Ordem,
                       DataHoraCriacao, DataHoraAlteracao, UsuarioCriacao, UsuarioAlteracao,
                       Thumbnail, ThumbnailMime, Sistema, Visibilidade
                  FROM DocumentacaoTutorial
                 WHERE Slug = @Slug
                   AND CodEmp = @CodEmp
                   AND Publicado = 'S'
                   AND Visibilidade = 'P'
                """;
            cmd.Parameters.AddWithValue("@Slug", slug);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
                tutorial = MapTutorial(rd);
        }

        if (tutorial != null)
        {
            tutorial.Blocos = await ListarBlocosAsync(tutorial.Codigo, codEmp, con);
            await MigrarImagensBase64Async(tutorial.Blocos, codEmp, con);
        }

        return tutorial;
    }

    public async Task<int> SalvarAsync(DocumentacaoTutorialModel tutorial)
    {
        await GarantirEsquemaAsync();

        // Imagens coladas/antigas em base64 viram arquivo antes de gravar (HTML fica leve)
        foreach (var bloco in tutorial.Blocos)
            bloco.Conteudo = (await ExtrairImagensBase64Async(bloco.Conteudo, tutorial.CodEmp)).Html;

        // Capa nova (ou antiga ainda em binário) vira arquivo; no banco fica só o caminho
        await CapaParaArquivoAsync(tutorial);

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
                         Thumbnail, ThumbnailMime, UsuarioCriacao, UsuarioAlteracao, Sistema, Visibilidade)
                    OUTPUT INSERTED.Codigo
                    VALUES
                        (@CodEmp, @Titulo, @Descricao, @Categoria, @UrlThumbnail, @Slug, @Publicado, @Ordem,
                         @Thumbnail, @ThumbnailMime, @UsuarioCriacao, @UsuarioAlteracao, @Sistema, @Visibilidade)
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
                           Sistema = @Sistema,
                           Visibilidade = @Visibilidade
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

            // aviso de "Nova documentação" no HELP: só para as públicas
            if ((tutorial.Sistema ?? "HELP") == "HELP" && tutorial.Publicado == "S" && !tutorial.EhInterna)
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
            AtualizarIndiceIA(tutorial.Codigo);
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
            AtualizarIndiceIA(codigo);
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    // ── Imagens das documentações ────────────────────────────────────────────
    // Ficam em {Uploads:PastaFisica}/documentacoes/{CodEmp}/ (servidas em /uploads).
    // Quem grava é o ImagemArmazenamentoService (só em produção; em dev fica base64).

    public const long MaxImagemBytes = ImagemArmazenamentoService.MaxImagemBytes;

    private static string SubpastaEmpresa(int codEmp) => $"documentacoes/{codEmp}";

    /// <summary>Grava a imagem em arquivo e devolve a URL pública (/uploads/documentacoes/...).</summary>
    public Task<string> SalvarImagemAsync(byte[] dados, string mime, int codEmp)
        => _imagens.SalvarAsync(dados, mime, SubpastaEmpresa(codEmp));

    /// <summary>Troca cada src="data:image/...;base64,..." por um arquivo em /uploads.</summary>
    public async Task<(string Html, bool Alterado)> ExtrairImagensBase64Async(string? html, int codEmp)
    {
        var (texto, alterado) = await _imagens.ExtrairBase64Async(html, SubpastaEmpresa(codEmp));
        return (texto, alterado);
    }

    // ── Capa (thumbnail) em arquivo ─────────────────────────────────────────
    // A capa passa a ficar em arquivo e o caminho vai na coluna UrlThumbnail.
    // A coluna Thumbnail (binário) só é lida para capas antigas, até a migração.

    private static bool? _colunaCapaOk;
    private static readonly SemaphoreSlim _travaColunaCapa = new(1, 1);

    /// <summary>Garante que UrlThumbnail comporta o caminho do arquivo (aumenta a coluna se for pequena).</summary>
    public async Task<bool> GarantirColunaCapaAsync()
    {
        if (_colunaCapaOk.HasValue) return _colunaCapaOk.Value;
        await _travaColunaCapa.WaitAsync();
        try
        {
            if (_colunaCapaOk.HasValue) return _colunaCapaOk.Value;
            await using var con = CriarConexao();
            await con.OpenAsync();
            await using var cmd = con.CreateCommand();
            cmd.CommandText = """
                DECLARE @len INT, @tipo SYSNAME, @nulo VARCHAR(3), @sql NVARCHAR(400);
                SELECT @len = CHARACTER_MAXIMUM_LENGTH, @tipo = DATA_TYPE, @nulo = IS_NULLABLE
                  FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_NAME = 'DocumentacaoTutorial' AND COLUMN_NAME = 'UrlThumbnail';
                IF @len IS NOT NULL AND @len <> -1 AND @len < 300
                BEGIN
                    SET @sql = N'ALTER TABLE DocumentacaoTutorial ALTER COLUMN UrlThumbnail ' + @tipo + N'(300) '
                             + CASE WHEN @nulo = 'YES' THEN N'NULL' ELSE N'NOT NULL' END;
                    EXEC sp_executesql @sql;
                END
                SELECT CHARACTER_MAXIMUM_LENGTH
                  FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_NAME = 'DocumentacaoTutorial' AND COLUMN_NAME = 'UrlThumbnail';
                """;
            var len = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            _colunaCapaOk = len == -1 || len >= 100;
            if (_colunaCapaOk == false)
                LogHelper.Log($"[DOC IMG] Coluna UrlThumbnail com {len} caracteres: capas continuam no banco.");
        }
        catch (Exception ex)
        {
            _colunaCapaOk = false;
            LogHelper.Log($"[DOC IMG] Não foi possível preparar a coluna UrlThumbnail: {ex.Message}");
        }
        finally { _travaColunaCapa.Release(); }
        return _colunaCapaOk.Value;
    }

    /// <summary>Se a capa estiver em binário, grava em arquivo e passa o caminho para UrlThumbnail.</summary>
    private async Task CapaParaArquivoAsync(DocumentacaoTutorialModel tutorial)
    {
        if (tutorial.Thumbnail is not { Length: > 0 } || !_imagens.GravaEmArquivo) return;
        if (!await GarantirColunaCapaAsync()) return;
        try
        {
            tutorial.UrlThumbnail = await _imagens.SalvarAsync(
                tutorial.Thumbnail, tutorial.ThumbnailMime ?? "image/png", SubpastaEmpresa(tutorial.CodEmp));
            tutorial.Thumbnail = null;
            tutorial.ThumbnailMime = null;
        }
        catch (Exception ex)
        {
            // mantém a capa no banco; tenta de novo no próximo salvar/migração
            LogHelper.Log($"[DOC IMG] Capa da documentação #{tutorial.Codigo} mantida no banco: {ex.Message}");
        }
    }

    /// <summary>
    /// Migração automática: documentações antigas com imagens em base64 são convertidas
    /// na primeira vez que são abertas (editor ou visualização). Não altera data nem índice da IA.
    /// </summary>
    private async Task MigrarImagensBase64Async(List<DocumentacaoTutorialBlocoModel> blocos, int codEmp, SqlConnection con)
    {
        foreach (var bloco in blocos)
        {
            try
            {
                var (html, alterado) = await ExtrairImagensBase64Async(bloco.Conteudo, codEmp);
                if (!alterado) continue;

                await using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    UPDATE DocumentacaoTutorialBloco
                       SET Conteudo = @Conteudo
                     WHERE Codigo = @Codigo
                       AND CodEmp = @CodEmp
                    """;
                cmd.Parameters.Add("@Conteudo", SqlDbType.NVarChar, -1).Value = html;
                cmd.Parameters.AddWithValue("@Codigo", bloco.Codigo);
                cmd.Parameters.AddWithValue("@CodEmp", codEmp);
                await cmd.ExecuteNonQueryAsync();

                LogHelper.Log($"[DOC IMG] Bloco #{bloco.Codigo}: imagens migradas ({bloco.Conteudo.Length / 1024} KB -> {html.Length / 1024} KB)");
                bloco.Conteudo = html;
            }
            catch (Exception ex)
            {
                // falhou? segue mostrando o conteúdo antigo; tenta de novo na próxima abertura
                LogHelper.Log($"[DOC IMG] Bloco #{bloco.Codigo}: erro na migração: {ex.Message}");
            }
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
        cmd.Parameters.Add("@Conteudo", SqlDbType.NVarChar, -1).Value = bloco.Conteudo ?? "";
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
            Sistema = rd.IsDBNull(rd.GetOrdinal("Sistema")) ? "HELP" : rd.GetString(rd.GetOrdinal("Sistema")),
            Visibilidade = rd.GetString(rd.GetOrdinal("Visibilidade")).Trim()
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
            Sistema = rd.IsDBNull(rd.GetOrdinal("Sistema")) ? "HELP" : rd.GetString(rd.GetOrdinal("Sistema")),
            Visibilidade = rd.GetString(rd.GetOrdinal("Visibilidade")).Trim()
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
        cmd.Parameters.AddWithValue("@Visibilidade", tutorial.EhInterna ? "I" : "P");
    }
}
