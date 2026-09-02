using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services;

public class ParametroDinamicoService
{
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

    public async Task<List<ParametroDinamicoModel>> ListarAsync(string termo = "", string grupo = "")
    {
        var lista = new List<ParametroDinamicoModel>();
        var termoLimpo = (termo ?? "").Trim();
        var grupoLimpo = (grupo ?? "").Trim();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT TOP 500
    Codigo,
    CodEmp,
    ISNULL(Parametro, '') AS Parametro,
    ISNULL(Grupo, '') AS Grupo,
    ISNULL(Descricao, '') AS Descricao,
    ISNULL(Valor, '') AS Valor,
    ISNULL(ValorPadrao, '') AS ValorPadrao,
    DataHoraUltimaGravacao,
    ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM ParametroDinamico
WHERE CodEmp = @CodEmp
  AND (@Grupo = '' OR Grupo = @Grupo)
  AND (
        @Termo = ''
        OR Parametro LIKE @Busca
        OR Grupo LIKE @Busca
        OR Descricao LIKE @Busca
        OR Valor LIKE @Busca
      )
ORDER BY Grupo, Parametro", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, 50).Value = grupoLimpo;
        cmd.Parameters.Add("@Termo", SqlDbType.VarChar, 120).Value = termoLimpo;
        cmd.Parameters.Add("@Busca", SqlDbType.VarChar, 130).Value = "%" + termoLimpo + "%";

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(Mapear(rd));

        return lista;
    }

    public async Task<List<string>> ListarGruposAsync()
    {
        var lista = new List<string>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT DISTINCT ISNULL(Grupo, '') AS Grupo
FROM ParametroDinamico
WHERE CodEmp = @CodEmp
  AND ISNULL(Grupo, '') <> ''
ORDER BY Grupo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(rd.GetString(0));

        return lista;
    }

    public async Task<ParametroDinamicoModel?> ObterAsync(int codigo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT TOP 1
    Codigo,
    CodEmp,
    ISNULL(Parametro, '') AS Parametro,
    ISNULL(Grupo, '') AS Grupo,
    ISNULL(Descricao, '') AS Descricao,
    ISNULL(Valor, '') AS Valor,
    ISNULL(ValorPadrao, '') AS ValorPadrao,
    DataHoraUltimaGravacao,
    ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM ParametroDinamico
WHERE Codigo = @Codigo
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

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
DECLARE @NovoParametro TABLE (Codigo INT);

INSERT INTO ParametroDinamico
(
    CodEmp,
    Parametro,
    Grupo,
    Descricao,
    Valor,
    ValorPadrao,
    DataHoraUltimaGravacao,
    UsuarioUltimaGravacao
)
OUTPUT INSERTED.Codigo INTO @NovoParametro
VALUES
(
    @CodEmp,
    @Parametro,
    @Grupo,
    @Descricao,
    @Valor,
    @ValorPadrao,
    GETDATE(),
    @Usuario
);

SELECT Codigo FROM @NovoParametro;", conn);

        AddParametros(cmd, parametro);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task AtualizarAsync(ParametroDinamicoModel parametro)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE ParametroDinamico
SET
    Descricao = @Descricao,
    Valor = @Valor,
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = parametro.Codigo;
        AddParametros(cmd, parametro);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExcluirAsync(int codigo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
DELETE FROM ParametroDinamico
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string> BuscarValorAsync(
        string parametro,
        string grupo,
        string descricao,
        string valorPadrao)
    {
        return await BuscarValorAsync(CodEmpAtual, parametro, grupo, descricao, valorPadrao);
    }

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

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SET NOCOUNT ON;

DECLARE @ValorAtual VARCHAR(4000);

SELECT @ValorAtual = ISNULL(Valor, '')
FROM ParametroDinamico
WHERE CodEmp = @CodEmp
  AND Parametro = @Parametro
  AND Grupo = @Grupo;

IF (@ValorAtual IS NOT NULL)
BEGIN
    SELECT @ValorAtual AS Parametro;
    RETURN;
END;

INSERT INTO ParametroDinamico
(
    CodEmp,
    Parametro,
    Grupo,
    Descricao,
    Valor,
    ValorPadrao,
    DataHoraUltimaGravacao,
    UsuarioUltimaGravacao
)
VALUES
(
    @CodEmp,
    @Parametro,
    @Grupo,
    @Descricao,
    @ValorPadrao,
    @ValorPadrao,
    GETDATE(),
    @Usuario
);

SELECT ISNULL(Valor, '') AS Parametro
FROM ParametroDinamico
WHERE CodEmp = @CodEmp
  AND Parametro = @Parametro
  AND Grupo = @Grupo;", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
        cmd.Parameters.Add("@Parametro", SqlDbType.VarChar, 80).Value = parametro;
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, 50).Value = grupo;
        cmd.Parameters.Add("@Descricao", SqlDbType.VarChar, 350).Value = descricao;
        cmd.Parameters.Add("@ValorPadrao", SqlDbType.VarChar, 4000).Value = valorPadrao;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "";
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

    private void AddParametros(SqlCommand cmd, ParametroDinamicoModel parametro)
    {
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Parametro", SqlDbType.VarChar, 80).Value = Limitar(parametro.Parametro, 80);
        cmd.Parameters.Add("@Grupo", SqlDbType.VarChar, 50).Value = Limitar(parametro.Grupo, 50);
        cmd.Parameters.Add("@Descricao", SqlDbType.VarChar, 350).Value = Limitar(parametro.Descricao, 350);
        cmd.Parameters.Add("@Valor", SqlDbType.VarChar, 4000).Value = parametro.Valor ?? "";
        cmd.Parameters.Add("@ValorPadrao", SqlDbType.VarChar, 4000).Value = parametro.ValorPadrao ?? "";
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
    }

    private static ParametroDinamicoModel Mapear(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.GetInt32(1),
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
