using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace EvolutCRM.Services;

/// <summary>Cadastro de status do usuário e troca de status (sempre por CodEmp).</summary>
public class UsuarioStatusService
{
    private readonly string _conn;
    private readonly UserState _state;
    private readonly UsuarioStatusNotifier _notifier;

    public UsuarioStatusService(IConfiguration cfg, UserState state, UsuarioStatusNotifier notifier)
    {
        _conn = cfg.GetConnectionString("Connection") ?? "";
        _state = state;
        _notifier = notifier;
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
        string.IsNullOrWhiteSpace(_state.CurrentUser) ? "SISTEMA" : _state.CurrentUser.Trim().ToUpper();

    // ───────────────────────────── CADASTRO ─────────────────────────────

    public async Task<List<UsuarioStatusCadastroModel>> ListarAsync(bool incluirInativos = false)
    {
        var lista = new List<UsuarioStatusCadastroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        await GarantirPadroesAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT Codigo, CodEmp, ISNULL(Descricao, '') AS Descricao, ISNULL(Cor, '#f59e0b') AS Cor,
       ISNULL(Disponivel, 'N') AS Disponivel, ISNULL(RedistribuiTickets, 'N') AS RedistribuiTickets,
       ISNULL(Ordem, 0) AS Ordem, ISNULL(Ativo, 'S') AS Ativo,
       DataHoraUltimaGravacao, ISNULL(UsuarioUltimaGravacao, '') AS UsuarioUltimaGravacao
FROM UsuarioStatus
WHERE CodEmp = @CodEmp
  AND (@IncluirInativos = 1 OR ISNULL(Ativo, 'S') = 'S')
ORDER BY Ordem, Codigo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@IncluirInativos", SqlDbType.Bit).Value = incluirInativos;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new UsuarioStatusCadastroModel
            {
                Codigo = rd.GetInt32(0),
                CodEmp = rd.GetInt32(1),
                Descricao = rd.GetString(2),
                Cor = NormalizarCor(rd.GetString(3)),
                Disponivel = SimNao(rd.GetString(4)),
                RedistribuiTickets = SimNao(rd.GetString(5)),
                Ordem = rd.GetInt32(6),
                Ativo = SimNao(rd.GetString(7)),
                DataHoraUltimaGravacao = rd.IsDBNull(8) ? null : rd.GetDateTime(8),
                UsuarioUltimaGravacao = rd.GetString(9)
            });
        }

        return lista;
    }

    public async Task SalvarAsync(UsuarioStatusCadastroModel model)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        if (model.Codigo <= 0)
        {
            await using var cmdProx = new SqlCommand(
                "SELECT ISNULL(MAX(Codigo), 0) + 1 FROM UsuarioStatus WHERE CodEmp = @CodEmp", conn);
            cmdProx.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
            model.Codigo = Convert.ToInt32(await cmdProx.ExecuteScalarAsync());
        }

        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM UsuarioStatus WHERE CodEmp = @CodEmp AND Codigo = @Codigo)
BEGIN
    UPDATE UsuarioStatus
    SET Descricao = @Descricao,
        Cor = @Cor,
        Disponivel = @Disponivel,
        RedistribuiTickets = @Redistribui,
        Ordem = @Ordem,
        Ativo = @Ativo,
        DataHoraUltimaGravacao = GETDATE(),
        UsuarioUltimaGravacao = @Usuario
    WHERE CodEmp = @CodEmp
      AND Codigo = @Codigo;
END
ELSE
BEGIN
    INSERT INTO UsuarioStatus
        (Codigo, CodEmp, Descricao, Cor, Disponivel, RedistribuiTickets, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
    VALUES
        (@Codigo, @CodEmp, @Descricao, @Cor, @Disponivel, @Redistribui, @Ordem, @Ativo, GETDATE(), @Usuario);
END", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = model.Codigo;
        cmd.Parameters.Add("@Descricao", SqlDbType.VarChar, 60).Value = Limitar(model.Descricao, 60);
        cmd.Parameters.Add("@Cor", SqlDbType.VarChar, 7).Value = NormalizarCor(model.Cor);
        cmd.Parameters.Add("@Disponivel", SqlDbType.Char, 1).Value = model.Disponivel ? "S" : "N";
        cmd.Parameters.Add("@Redistribui", SqlDbType.Char, 1).Value = model.RedistribuiTickets ? "S" : "N";
        cmd.Parameters.Add("@Ordem", SqlDbType.Int).Value = model.Ordem <= 0 ? model.Codigo * 10 : model.Ordem;
        cmd.Parameters.Add("@Ativo", SqlDbType.Char, 1).Value = model.Ativo ? "S" : "N";
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
        await cmd.ExecuteNonQueryAsync();

        _notifier.Notificar(CodEmpAtual);
    }

    public async Task AlterarAtivoAsync(int codigo, bool ativo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE UsuarioStatus
SET Ativo = @Ativo,
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario
WHERE CodEmp = @CodEmp
  AND Codigo = @Codigo", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;
        cmd.Parameters.Add("@Ativo", SqlDbType.Char, 1).Value = ativo ? "S" : "N";
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
        await cmd.ExecuteNonQueryAsync();

        _notifier.Notificar(CodEmpAtual);
    }

    // ─────────────────────────── TROCA DE STATUS ───────────────────────────

    /// <summary>Define o status do usuário. codStatus = null significa "Disponível".</summary>
    public async Task DefinirStatusAsync(string usuario, int? codStatus)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE Usuario
SET CodUsuarioStatus = @CodStatus,
    DataHoraStatus = GETDATE(),
    Online = 'S',
    UltimaAtividadeOnline = GETDATE()
WHERE CodEmp = @CodEmp
  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@CodStatus", SqlDbType.Int).Value = (object?)codStatus ?? DBNull.Value;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = usuario.Trim();
        await cmd.ExecuteNonQueryAsync();

        _notifier.Notificar(CodEmpAtual);
    }

    /// <summary>Situação atual de todos os usuários do HELP da empresa.</summary>
    public async Task<List<EquipeMembroModel>> ObterEquipeAsync()
    {
        var lista = new List<EquipeMembroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Mesma regra de "online" do resto do sistema: Online = 'S' e heartbeat nos últimos 40s
        await using var cmd = new SqlCommand(@"
SELECT
    LTRIM(RTRIM(U.Usuario)) AS Usuario,
    CASE
        WHEN ISNULL(U.Online, 'N') = 'S'
         AND U.UltimaAtividadeOnline IS NOT NULL
         AND DATEDIFF(SECOND, U.UltimaAtividadeOnline, GETDATE()) <= 40
        THEN 1 ELSE 0
    END AS Online,
    S.Codigo,
    S.Descricao,
    S.Cor,
    ISNULL(S.Disponivel, 'S') AS Disponivel,
        CASE WHEN U.DataHoraStatus IS NULL THEN NULL
         ELSE DATEDIFF(MINUTE, U.DataHoraStatus, GETDATE()) END AS Minutos,
    (SELECT COUNT(1)
     FROM TicketChamadoC T
     WHERE T.CodEmp = U.CodEmp
       AND UPPER(LTRIM(RTRIM(ISNULL(T.Usuario, '')))) = UPPER(LTRIM(RTRIM(U.Usuario)))
       AND ISNULL(T.CodSetor, 1) = 1
       AND ISNULL(T.CodSituacao, 1) = 1 
       AND ISNULL(T.ChatInterno, 'N') <> 'S'
    ) AS Atendimentos
FROM Usuario U
LEFT JOIN UsuarioStatus S
       ON S.CodEmp = U.CodEmp
      AND S.Codigo = U.CodUsuarioStatus
WHERE U.CodEmp = @CodEmp
  AND ISNULL(U.Inativo, 'N') = 'N'
  AND ISNULL(U.Help, 'N') = 'S'
  AND U.Codigo NOT IN (9, 17)
ORDER BY U.Usuario", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new EquipeMembroModel
            {
                Usuario = rd.IsDBNull(0) ? "" : rd.GetString(0),
                Online = rd.GetInt32(1) == 1,
                CodStatus = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                StatusDescricao = rd.IsDBNull(3) ? null : rd.GetString(3),
                StatusCor = rd.IsDBNull(4) ? null : NormalizarCor(rd.GetString(4)),
                StatusContaDisponivel = SimNao(rd.GetString(5)),
                MinutosNoStatus = rd.IsDBNull(6) ? null : rd.GetInt32(6),
                Atendimentos = rd.IsDBNull(7) ? 0 : rd.GetInt32(7)
            });
        }

        return lista;
    }

    // ─────────────────────────────── APOIO ───────────────────────────────

    /// <summary>Cria Banheiro / Assunto pessoal / Treinamento para a empresa que ainda não tem nenhum status.</summary>
    private async Task GarantirPadroesAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM UsuarioStatus WHERE CodEmp = @CodEmp)
BEGIN
    INSERT INTO UsuarioStatus
        (Codigo, CodEmp, Descricao, Cor, Disponivel, RedistribuiTickets, Ordem, Ativo, DataHoraUltimaGravacao, UsuarioUltimaGravacao)
    VALUES
        (1, @CodEmp, 'Banheiro',        '#0ea5e9', 'N', 'N', 10, 'S', GETDATE(), @Usuario),
        (2, @CodEmp, 'Assunto pessoal', '#a855f7', 'N', 'N', 20, 'S', GETDATE(), @Usuario),
        (3, @CodEmp, 'Treinamento',     '#f59e0b', 'N', 'S', 30, 'S', GETDATE(), @Usuario);
END", conn);

        cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = CodEmpAtual;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 80).Value = UsuarioAtual;
        await cmd.ExecuteNonQueryAsync();
    }

    private static readonly Regex _corHex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static string NormalizarCor(string? cor)
    {
        var c = (cor ?? "").Trim();
        return _corHex.IsMatch(c) ? c.ToLowerInvariant() : "#f59e0b";
    }

    private static bool SimNao(string? valor) =>
        (valor ?? "").Trim().Equals("S", StringComparison.OrdinalIgnoreCase);

    private static string Limitar(string? valor, int tamanho)
    {
        var texto = (valor ?? "").Trim();
        return texto.Length > tamanho ? texto[..tamanho] : texto;
    }
}
