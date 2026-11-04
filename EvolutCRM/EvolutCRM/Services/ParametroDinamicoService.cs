using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Data;

namespace EvolutCRM.Services;

/// <summary>
/// Parâmetros do HELP. Ficam na tabela ParametrosHelp (Tipo = 'PARAMETRO'), separados do ERP, que continua
/// com a ParametroDinamico. Colunas usadas:
///   Titulo = nome do parâmetro · TipoAlteracao = grupo · Descricao = descrição · Detalhe = valor
///   Referencia = valor padrão (só quando cabe em 100 caracteres) · Usuario/DataHora = última gravação
///
/// Na primeira vez que o sistema roda, os parâmetros do HELP que estavam na ParametroDinamico
/// (grupos TICKET e WHATSAPP) são copiados para cá com o valor atual e apagados de lá.
/// </summary>
public class ParametroDinamicoService
{
    private const string Tipo = "PARAMETRO";

    /// <summary>Grupos que eram do HELP na ParametroDinamico (o ERP não usa nenhum deles).</summary>
    private static readonly string[] GruposHelp = { "TICKET", "WHATSAPP" };

    private const int TamanhoGrupo = 20;        // ParametrosHelp.TipoAlteracao varchar(20)
    private const int TamanhoPadrao = 100;      // ParametrosHelp.Referencia varchar(100)
    private const int TamanhoUsuario = 100;     // ParametrosHelp.Usuario varchar(100)
    private const int TamanhoParametro = 200;   // ParametrosHelp.Titulo nvarchar(200)

    // Valores lidos ficam guardados por pouco tempo: os parâmetros são lidos o tempo todo (cada ticket salvo,
    // cada mensagem do WhatsApp...). Salvar/excluir pela tela limpa na hora.
    private static readonly ConcurrentDictionary<(int CodEmp, string Parametro), (string Valor, DateTime Em)> _cache = new();
    private static readonly TimeSpan TempoCache = TimeSpan.FromSeconds(60);

    private static volatile bool _migrado;
    private static readonly SemaphoreSlim _travaMigracao = new(1, 1);

    private readonly string _conn;
    private readonly UserState _state;

    public ParametroDinamicoService(IConfiguration cfg, UserState state)
    {
        _conn = cfg.GetConnectionString("Connection") ?? "";
        _state = state;
    }

    private int CodEmpAtual
    {
        get
        {
            if (_state.CurrentCompanyId <= 0)
                throw new InvalidOperationException("Empresa do usuario nao carregada no UserState.");

            return _state.CurrentCompanyId;
        }
    }

    private string UsuarioAtual =>
        string.IsNullOrWhiteSpace(_state.CurrentUser)
            ? "SISTEMA"
            : _state.CurrentUser.Trim().ToUpper();

    // ───────────────────────────── Migração (uma vez) ─────────────────────────────

    /// <summary>
    /// Move os parâmetros do HELP da ParametroDinamico para a ParametrosHelp (com o valor atual)
    /// e apaga de lá. Roda uma vez por inicialização; se já foi feito, não encontra nada para mover.
    /// </summary>
    private async Task GarantirMigracaoAsync(SqlConnection conn)
    {
        if (_migrado) return;

        await _travaMigracao.WaitAsync();
        try
        {
            if (_migrado) return;

            await using var cmd = new SqlCommand(@"
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Índice para ler parâmetro (e os logs) sem percorrer a tabela inteira
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParametrosHelp_Tipo_CodEmp' AND object_id = OBJECT_ID('dbo.ParametrosHelp'))
    CREATE NONCLUSTERED INDEX IX_ParametrosHelp_Tipo_CodEmp ON dbo.ParametrosHelp (Tipo, CodEmp) INCLUDE (Titulo);

IF EXISTS (SELECT 1 FROM ParametroDinamico WHERE Grupo IN (" + ListaGruposSql() + @"))
BEGIN
    BEGIN TRAN;

    INSERT INTO ParametrosHelp
        (CodEmp, Tipo, Sistema, Titulo, Descricao, TipoAlteracao, Usuario, Referencia, Detalhe, DataHora, Ativo, Destaque)
    SELECT P.CodEmp, 'PARAMETRO', 'HELP',
           LEFT(P.Parametro, 200),
           ISNULL(P.Descricao, ''),
           LEFT(ISNULL(P.Grupo, ''), 20),
           LEFT(ISNULL(P.UsuarioUltimaGravacao, ''), 100),
           CASE WHEN LEN(ISNULL(P.ValorPadrao, '')) <= 100 THEN P.ValorPadrao END,
           ISNULL(P.Valor, ''),
           ISNULL(P.DataHoraUltimaGravacao, GETDATE()),
           'S', 'N'
    FROM ParametroDinamico P
    WHERE P.Grupo IN (" + ListaGruposSql() + @")
      AND NOT EXISTS (SELECT 1 FROM ParametrosHelp H
                      WHERE H.Tipo = 'PARAMETRO' AND H.CodEmp = P.CodEmp AND H.Titulo = P.Parametro);

    DELETE FROM ParametroDinamico WHERE Grupo IN (" + ListaGruposSql() + @");

    COMMIT;
END", conn);
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();

            _migrado = true;
            _cache.Clear();
        }
        finally
        {
            _travaMigracao.Release();
        }
    }

    private static string ListaGruposSql() => string.Join(", ", GruposHelp.Select(g => "'" + g + "'"));

    private async Task<SqlConnection> AbrirAsync()
    {
        var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirMigracaoAsync(conn);
        return conn;
    }

    // ───────────────────────────── Tela de Parâmetros ─────────────────────────────

    private const string SqlSelect = @"
SELECT
    Codigo,
    CodEmp,
    ISNULL(Titulo, '') AS Parametro,
    ISNULL(TipoAlteracao, '') AS Grupo,
    ISNULL(Descricao, '') AS Descricao,
    ISNULL(Detalhe, '') AS Valor,
    ISNULL(Referencia, '') AS ValorPadrao,
    DataHora,
    ISNULL(Usuario, '') AS Usuario
FROM ParametrosHelp";

    public async Task<List<ParametroDinamicoModel>> ListarAsync(string termo = "", string grupo = "")
    {
        var lista = new List<ParametroDinamicoModel>();
        var termoLimpo = (termo ?? "").Trim();
        var grupoLimpo = (grupo ?? "").Trim();

        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(SqlSelect + @"
WHERE Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp
  AND (@Grupo = '' OR TipoAlteracao = @Grupo)
  AND (
        @Termo = ''
        OR Titulo LIKE @Busca
        OR TipoAlteracao LIKE @Busca
        OR Descricao LIKE @Busca
        OR Detalhe LIKE @Busca
      )
ORDER BY TipoAlteracao, Titulo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, TamanhoGrupo).Value = Limitar(grupoLimpo, TamanhoGrupo);
        cmd.Parameters.Add("@Termo", SqlDbType.NVarChar, 120).Value = termoLimpo;
        cmd.Parameters.Add("@Busca", SqlDbType.NVarChar, 130).Value = "%" + termoLimpo + "%";

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(Mapear(rd));

        return lista;
    }

    public async Task<List<string>> ListarGruposAsync()
    {
        var lista = new List<string>();

        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(@"
SELECT DISTINCT ISNULL(TipoAlteracao, '') AS Grupo
FROM ParametrosHelp
WHERE Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp
  AND ISNULL(TipoAlteracao, '') <> ''
ORDER BY Grupo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(rd.GetString(0));

        return lista;
    }

    public async Task<ParametroDinamicoModel?> ObterAsync(int codigo)
    {
        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(SqlSelect + @"
WHERE Codigo = @Codigo
  AND Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await using var rd = await cmd.ExecuteReaderAsync();
        return await rd.ReadAsync() ? Mapear(rd) : null;
    }

    public async Task<int> SalvarAsync(ParametroDinamicoModel parametro)
    {
        if (parametro.Codigo > 0)
        {
            await AtualizarAsync(parametro);
            return parametro.Codigo;
        }

        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(@"
INSERT INTO ParametrosHelp
    (CodEmp, Tipo, Sistema, Titulo, Descricao, TipoAlteracao, Usuario, Referencia, Detalhe, DataHora, Ativo, Destaque)
OUTPUT INSERTED.Codigo
VALUES
    (@CodEmp, 'PARAMETRO', 'HELP', @Parametro, @Descricao, @Grupo, @Usuario, @ValorPadrao, @Valor, GETDATE(), 'S', 'N');", conn);

        AddParametros(cmd, parametro);
        var codigo = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        _cache.Clear();
        return codigo;
    }

    public async Task AtualizarAsync(ParametroDinamicoModel parametro)
    {
        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(@"
UPDATE ParametrosHelp
SET
    Descricao = @Descricao,
    Detalhe = @Valor,
    DataHora = GETDATE(),
    Usuario = @Usuario
WHERE Codigo = @Codigo
  AND Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = parametro.Codigo;
        AddParametros(cmd, parametro);

        await cmd.ExecuteNonQueryAsync();
        _cache.Clear();
    }

    public async Task ExcluirAsync(int codigo)
    {
        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(@"
DELETE FROM ParametrosHelp
WHERE Codigo = @Codigo
  AND Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await cmd.ExecuteNonQueryAsync();
        _cache.Clear();
    }

    // ───────────────────────────── Leitura pelo sistema ─────────────────────────────

    public async Task<string> BuscarValorAsync(
        string parametro,
        string grupo,
        string descricao,
        string valorPadrao)
    {
        return await BuscarValorAsync(CodEmpAtual, parametro, grupo, descricao, valorPadrao);
    }

    /// <summary>
    /// Valor do parâmetro da empresa. Se ainda não existe, cria com o valor padrão (aparece na tela de
    /// Parâmetros para quem quiser mudar) e devolve o padrão.
    /// </summary>
    public async Task<string> BuscarValorAsync(
        int codEmp,
        string parametro,
        string grupo,
        string descricao,
        string valorPadrao)
    {
        parametro = (parametro ?? "").Trim();
        grupo = (grupo ?? "").Trim();
        descricao = (descricao ?? "").Trim();
        valorPadrao = valorPadrao ?? "";

        if (codEmp <= 0)
            throw new ArgumentException("CodEmp invalido para buscar parametro dinamico.", nameof(codEmp));

        var chave = (codEmp, parametro.ToUpperInvariant());
        if (_cache.TryGetValue(chave, out var guardado) && DateTime.UtcNow - guardado.Em < TempoCache)
            return guardado.Valor;

        await using var conn = await AbrirAsync();

        await using var cmd = new SqlCommand(@"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ValorAtual NVARCHAR(MAX), @Achou BIT = 0;

BEGIN TRAN;

SELECT TOP 1 @ValorAtual = ISNULL(Detalhe, ''), @Achou = 1
FROM ParametrosHelp WITH (UPDLOCK, HOLDLOCK)
WHERE Tipo = 'PARAMETRO'
  AND CodEmp = @CodEmp
  AND Titulo = @Parametro
ORDER BY Codigo;

IF @Achou = 0
BEGIN
    INSERT INTO ParametrosHelp
        (CodEmp, Tipo, Sistema, Titulo, Descricao, TipoAlteracao, Usuario, Referencia, Detalhe, DataHora, Ativo, Destaque)
    VALUES
        (@CodEmp, 'PARAMETRO', 'HELP', @Parametro, @Descricao, @Grupo, @Usuario, @RefPadrao, @ValorPadrao, GETDATE(), 'S', 'N');
    SET @ValorAtual = @ValorPadrao;
END

COMMIT;

SELECT @ValorAtual;", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
        cmd.Parameters.Add("@Parametro", SqlDbType.NVarChar, TamanhoParametro).Value = Limitar(parametro, TamanhoParametro);
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, TamanhoGrupo).Value = Limitar(grupo, TamanhoGrupo);
        cmd.Parameters.Add("@Descricao", SqlDbType.NVarChar, -1).Value = descricao;
        cmd.Parameters.Add("@ValorPadrao", SqlDbType.NVarChar, -1).Value = valorPadrao;
        cmd.Parameters.Add("@RefPadrao", SqlDbType.VarChar, TamanhoPadrao).Value =
            valorPadrao.Length <= TamanhoPadrao ? valorPadrao : (object)DBNull.Value;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, TamanhoUsuario).Value = Limitar(UsuarioAtual, TamanhoUsuario);

        var result = await cmd.ExecuteScalarAsync();
        var valor = result?.ToString() ?? "";

        _cache[chave] = (valor, DateTime.UtcNow);
        return valor;
    }

    public async Task<bool> BuscarBoolAsync(
        string parametro,
        string grupo,
        string descricao,
        bool valorPadrao = false)
    {
        var valor = await BuscarValorAsync(
            parametro,
            grupo,
            descricao,
            valorPadrao ? "S" : "N");

        return EhSim(valor);
    }

    public async Task<bool> BuscarBoolAsync(
        int codEmp,
        string parametro,
        string grupo,
        string descricao,
        bool valorPadrao = false)
    {
        var valor = await BuscarValorAsync(
            codEmp,
            parametro,
            grupo,
            descricao,
            valorPadrao ? "S" : "N");

        return EhSim(valor);
    }

    // ───────────────────────────── Auxiliares ─────────────────────────────

    private void AddParametros(SqlCommand cmd, ParametroDinamicoModel parametro)
    {
        var padrao = parametro.ValorPadrao ?? "";
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Parametro", SqlDbType.NVarChar, TamanhoParametro).Value = Limitar(parametro.Parametro, TamanhoParametro);
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, TamanhoGrupo).Value = Limitar(parametro.Grupo, TamanhoGrupo);
        cmd.Parameters.Add("@Descricao", SqlDbType.NVarChar, -1).Value = (parametro.Descricao ?? "").Trim();
        cmd.Parameters.Add("@Valor", SqlDbType.NVarChar, -1).Value = parametro.Valor ?? "";
        cmd.Parameters.Add("@ValorPadrao", SqlDbType.VarChar, TamanhoPadrao).Value =
            padrao.Length <= TamanhoPadrao ? padrao : (object)DBNull.Value;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, TamanhoUsuario).Value = Limitar(UsuarioAtual, TamanhoUsuario);
    }

    private static ParametroDinamicoModel Mapear(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.IsDBNull(1) ? 0 : rd.GetInt32(1),
        Parametro = rd.GetString(2),
        Grupo = rd.GetString(3),
        Descricao = rd.GetString(4),
        Valor = rd.GetString(5),
        ValorPadrao = rd.GetString(6),
        DataHoraUltimaGravacao = rd.IsDBNull(7) ? null : rd.GetDateTime(7),
        UsuarioUltimaGravacao = rd.GetString(8)
    };

    private static string Limitar(string? valor, int tamanho)
    {
        var texto = (valor ?? "").Trim();
        return texto.Length > tamanho ? texto[..tamanho] : texto;
    }

    private static bool EhSim(string? valor)
    {
        var texto = (valor ?? "").Trim().ToUpperInvariant();
        return texto is "S" or "SIM" or "TRUE" or "1";
    }
}
