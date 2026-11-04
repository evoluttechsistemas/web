using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services;

public class TicketClassificacaoService
{
    private readonly string _conn;
    private readonly UserState _state;

    public TicketClassificacaoService(IConfiguration cfg, UserState state)
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

    public async Task<List<TicketSetorCadastroModel>> ListarSetoresAsync(bool incluirInativos = false)
    {
        var lista = new List<TicketSetorCadastroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirPadroesAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT Codigo, CodEmp, ISNULL(Descricao, '') AS Descricao, Ordem,
       ISNULL(Ativo, 'S') AS Ativo, DataHoraUltimaGravacao,
       ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM TicketSetorCRM
WHERE CodEmp = @CodEmp
  AND (@IncluirInativos = 1 OR ISNULL(Ativo, 'S') = 'S')
ORDER BY Ordem, Codigo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@IncluirInativos", SqlDbType.Bit).Value = incluirInativos;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(MapearSetor(rd));

        return lista;
    }

    public async Task<List<TicketSituacaoCadastroModel>> ListarSituacoesAsync(bool incluirInativas = false)
    {
        var lista = new List<TicketSituacaoCadastroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirPadroesAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT Codigo, CodEmp, ISNULL(Descricao, '') AS Descricao, Ordem,
       ISNULL(Ativo, 'S') AS Ativo, DataHoraUltimaGravacao,
       ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM TicketSituacaoCRM
WHERE CodEmp = @CodEmp
  AND (@IncluirInativas = 1 OR ISNULL(Ativo, 'S') = 'S')
ORDER BY Ordem, Codigo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@IncluirInativas", SqlDbType.Bit).Value = incluirInativas;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(MapearSituacao(rd));

        return lista;
    }

    public async Task<List<TicketTipoCadastroModel>> ListarTiposAsync(bool incluirInativos = false)
    {
        var lista = new List<TicketTipoCadastroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirPadroesAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT Codigo, CodEmp, ISNULL(Descricao, '') AS Descricao, Ordem,
       ISNULL(Ativo, 'S') AS Ativo, DataHoraUltimaGravacao,
       ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM TicketTipoCRM
WHERE CodEmp = @CodEmp
  AND (@IncluirInativos = 1 OR ISNULL(Ativo, 'S') = 'S')
ORDER BY Ordem, Codigo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@IncluirInativos", SqlDbType.Bit).Value = incluirInativos;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(MapearTipo(rd));

        return lista;
    }

    public async Task GarantirPadroesAsync()
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirPadroesAsync(conn);
    }

    public async Task SalvarSituacaoAsync(TicketSituacaoCadastroModel model)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        if (model.Codigo <= 0)
            model.Codigo = await ProximoCodigoAsync(conn, "TicketSituacaoCRM");

        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM TicketSituacaoCRM WHERE CodEmp = @CodEmp AND Codigo = @Codigo)
BEGIN
    UPDATE TicketSituacaoCRM
    SET Descricao = @Descricao,
        Ordem = @Ordem,
        Ativo = @Ativo,
        DataHoraUltimaGravacao = GETDATE(),
        UsuarioUltimaGravacao = @Usuario
    WHERE CodEmp = @CodEmp
      AND Codigo = @Codigo;
END
ELSE
BEGIN
    INSERT INTO TicketSituacaoCRM
        (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
    VALUES
        (@Codigo, @CodEmp, @Descricao, @Ordem, @Ativo, GETDATE(), @Usuario);
END", conn);

        AddComum(cmd, model.Codigo, model.Descricao, model.Ordem, model.Ativo);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SalvarSetorAsync(TicketSetorCadastroModel model)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        if (model.Codigo <= 0)
            model.Codigo = await ProximoCodigoAsync(conn, "TicketSetorCRM");

        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM TicketSetorCRM WHERE CodEmp = @CodEmp AND Codigo = @Codigo)
BEGIN
    UPDATE TicketSetorCRM
    SET Descricao = @Descricao,
        Ordem = @Ordem,
        Ativo = @Ativo,
        DataHoraUltimaGravacao = GETDATE(),
        UsuarioUltimaGravacao = @Usuario
    WHERE CodEmp = @CodEmp
      AND Codigo = @Codigo;
END
ELSE
BEGIN
    INSERT INTO TicketSetorCRM
        (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
    VALUES
        (@Codigo, @CodEmp, @Descricao, @Ordem, @Ativo, GETDATE(), @Usuario);
END", conn);

        AddComum(cmd, model.Codigo, model.Descricao, model.Ordem, model.Ativo);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SalvarTipoAsync(TicketTipoCadastroModel model)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        if (model.Codigo <= 0)
            model.Codigo = await ProximoCodigoAsync(conn, "TicketTipoCRM");

        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM TicketTipoCRM WHERE CodEmp = @CodEmp AND Codigo = @Codigo)
BEGIN
    UPDATE TicketTipoCRM
    SET Descricao = @Descricao,
        Ordem = @Ordem,
        Ativo = @Ativo,
        DataHoraUltimaGravacao = GETDATE(),
        UsuarioUltimaGravacao = @Usuario
    WHERE CodEmp = @CodEmp
      AND Codigo = @Codigo;
END
ELSE
BEGIN
    INSERT INTO TicketTipoCRM
        (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
    VALUES
        (@Codigo, @CodEmp, @Descricao, @Ordem, @Ativo, GETDATE(), @Usuario);
END", conn);

        AddComum(cmd, model.Codigo, model.Descricao, model.Ordem, model.Ativo);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InativarSituacaoAsync(int codigo)
    {
        await AlterarAtivoAsync("TicketSituacaoCRM", codigo, false);
    }

    public async Task InativarSetorAsync(int codigo)
    {
        await AlterarAtivoAsync("TicketSetorCRM", codigo, false);
    }

    public async Task InativarTipoAsync(int codigo)
    {
        await AlterarAtivoAsync("TicketTipoCRM", codigo, false);
    }

    public async Task AlterarAtivoSituacaoAsync(int codigo, bool ativo)
    {
        await AlterarAtivoAsync("TicketSituacaoCRM", codigo, ativo);
    }

    public async Task AlterarAtivoSetorAsync(int codigo, bool ativo)
    {
        await AlterarAtivoAsync("TicketSetorCRM", codigo, ativo);
    }

    public async Task AlterarAtivoTipoAsync(int codigo, bool ativo)
    {
        await AlterarAtivoAsync("TicketTipoCRM", codigo, ativo);
    }

    private async Task AlterarAtivoAsync(string tabela, int codigo, bool ativo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        var sql = $@"
UPDATE {tabela}
SET Ativo = @Ativo,
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario
WHERE CodEmp = @CodEmp
  AND Codigo = @Codigo";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@Ativo", SqlDbType.Char, 1).Value = ativo ? "S" : "N";
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task GarantirPadroesAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(@"
SET NOCOUNT ON;

INSERT INTO TicketSetorCRM
    (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
SELECT V.Codigo, @CodEmp, V.Descricao, V.Ordem, 'S', GETDATE(), @Usuario
FROM
(
    VALUES
    (1, 'Suporte', 10),
    (2, 'Desenvolvimento', 20),
    (3, 'Comercial', 30),
    (4, 'Treinamento', 40)
) V(Codigo, Descricao, Ordem)
WHERE NOT EXISTS
(
    SELECT 1
    FROM TicketSetorCRM X
    WHERE X.CodEmp = @CodEmp
      AND X.Codigo = V.Codigo
);

INSERT INTO TicketSituacaoCRM
    (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
SELECT V.Codigo, @CodEmp, V.Descricao, V.Ordem, 'S', GETDATE(), @Usuario
FROM
(
    VALUES
    (1, 'Suporte', 10),
    (2, 'Desenvolvimento', 20),
    (4, 'Atualizar', 30),
    (5, 'Pendente', 40),
    (6, 'Aprovacao', 50),
    (3, 'Finalizado', 60)
) V(Codigo, Descricao, Ordem)
WHERE NOT EXISTS
(
    SELECT 1
    FROM TicketSituacaoCRM X
    WHERE X.CodEmp = @CodEmp
      AND X.Codigo = V.Codigo
);

INSERT INTO TicketTipoCRM
    (Codigo, CodEmp, Descricao, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
SELECT V.Codigo, @CodEmp, V.Descricao, V.Ordem, 'S', GETDATE(), @Usuario
FROM
(
    VALUES
    (1, 'Melhoria', 10),
    (2, 'Erro', 20),
    (3, 'N. Viavel', 30),
    (4, 'Chamado', 40),
    (5, 'WhatsApp', 50)
) V(Codigo, Descricao, Ordem)
WHERE NOT EXISTS
(
    SELECT 1
    FROM TicketTipoCRM X
    WHERE X.CodEmp = @CodEmp
      AND X.Codigo = V.Codigo
);", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ProximoCodigoAsync(SqlConnection conn, string tabela)
    {
        var sql = $"SELECT ISNULL(MAX(Codigo), 0) + 1 FROM {tabela} WHERE CodEmp = @CodEmp";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private void AddComum(SqlCommand cmd, int codigo, string descricao, int ordem, bool ativo)
    {
        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@Descricao", SqlDbType.VarChar, 80).Value = Limitar(descricao, 80);
        cmd.Parameters.Add("@Ordem", SqlDbType.Int).Value = ordem <= 0 ? codigo : ordem;
        cmd.Parameters.Add("@Ativo", SqlDbType.Char, 1).Value = ativo ? "S" : "N";
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
    }

    private static TicketSetorCadastroModel MapearSetor(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.GetInt32(1),
        Descricao = rd.GetString(2),
        Ordem = rd.GetInt32(3),
        Ativo = rd.GetString(4).Trim().Equals("S", StringComparison.OrdinalIgnoreCase),
        DataHoraUltimaGravacao = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
        UsuarioUltimaGravacao = rd.GetString(6)
    };

    private static TicketSituacaoCadastroModel MapearSituacao(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.GetInt32(1),
        Descricao = rd.GetString(2),
        Ordem = rd.GetInt32(3),
        Ativo = rd.GetString(4).Trim().Equals("S", StringComparison.OrdinalIgnoreCase),
        DataHoraUltimaGravacao = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
        UsuarioUltimaGravacao = rd.GetString(6)
    };

    private static TicketTipoCadastroModel MapearTipo(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.GetInt32(1),
        Descricao = rd.GetString(2),
        Ordem = rd.GetInt32(3),
        Ativo = rd.GetString(4).Trim().Equals("S", StringComparison.OrdinalIgnoreCase),
        DataHoraUltimaGravacao = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
        UsuarioUltimaGravacao = rd.GetString(6)
    };

    private static string Limitar(string? valor, int tamanho)
    {
        var texto = (valor ?? "").Trim();
        return texto.Length > tamanho ? texto[..tamanho] : texto;
    }
}
