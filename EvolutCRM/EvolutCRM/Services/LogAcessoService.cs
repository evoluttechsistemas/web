using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class LogAcessoService
    {
        private readonly string _connection;
        private readonly UserState? _userState;

        public LogAcessoService(IConfiguration config)
        {
            _connection = config.GetConnectionString("Connection")!;
        }

        public LogAcessoService(IConfiguration config, UserState userState)
        {
            _connection = config.GetConnectionString("Connection")!;
            _userState = userState;
        }

        private int CodEmpAtual
        {
            get
            {
                if (_userState is not null && _userState.CurrentCompanyId > 0)
                    return _userState.CurrentCompanyId;

                return 2;
            }
        }

        private SqlConnection GetConnection() => new SqlConnection(_connection);

        // ─────────────────────────────────────────────
        //  GRAVAR
        // ─────────────────────────────────────────────

        /// <summary>
        /// Registra um evento de acesso (LOGIN / LOGOUT / SESSAO_EXPIRADA) em ParametrosHelp.
        /// </summary>
        public async Task RegistrarAsync(
    string usuario,
    int codEmp,
    string nomeEmpresa,
    string tipoEvento,
    string? ip = null,
    string? userAgent = null,
    string tipoLog = "LOG_ACESSO")
        {
            try
            {
                using var conn = GetConnection();
                await conn.OpenAsync();

                var cmd = new SqlCommand(@"
            INSERT INTO ParametrosHelp
                (CodEmp, Tipo, Sistema, Versao, Titulo, Referencia, IpOrigem, Detalhe,
                 DataHora, Ativo, Destaque, Usuario)
            VALUES
                (@CodEmp, @TipoLog, 'HELP', @CodEmpStr, @TipoEvento, @NomeEmpresa, @Ip, @UserAgent,
                 GETDATE(), 'S', 'N', @Usuario)", conn);

                cmd.Parameters.AddWithValue("@CodEmp", codEmp);
                cmd.Parameters.AddWithValue("@CodEmpStr", codEmp.ToString());
                cmd.Parameters.AddWithValue("@TipoLog", tipoLog);
                cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpperInvariant());
                cmd.Parameters.AddWithValue("@TipoEvento", tipoEvento ?? "");
                cmd.Parameters.AddWithValue("@NomeEmpresa",
                    (nomeEmpresa ?? "").Length > 100
                        ? (nomeEmpresa ?? "")[..100]
                        : (nomeEmpresa ?? ""));
                cmd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserAgent", (object?)userAgent ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERRO LogAcessoService.RegistrarAsync: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        //  CONSULTAR
        // ─────────────────────────────────────────────

        /// <summary>
        /// Retorna os registros de acesso com filtros opcionais de usuário, empresa e período.
        /// </summary>
        public async Task<List<LogAcessoModel>> ObterAsync(
    string? usuario = null,
    int? codEmp = null,
    DateTime? de = null,
    DateTime? ate = null,
    int limite = 500,
    string tipo = "LOG_ACESSO",
    string? tipoEvento = null)
        {
            var lista = new List<LogAcessoModel>();
            var empresaFiltro = codEmp ?? CodEmpAtual;

            using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"
    SELECT TOP (@Limite)
        p.Codigo,
        p.Usuario,
        ISNULL(p.CodEmp, 0)        AS CodEmp,
        ISNULL(p.Referencia, '')   AS NomeEmpresa,
        p.DataHora,
        ISNULL(p.Titulo, '')       AS TipoEvento,
        p.IpOrigem,
        p.Detalhe
    FROM ParametrosHelp p
    WHERE p.CodEmp = @CodEmp
      AND p.Tipo = @Tipo
      AND (@TipoEvento IS NULL OR p.Titulo = @TipoEvento)
      AND (@Usuario    IS NULL OR UPPER(LTRIM(RTRIM(p.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario))))
      AND (@De         IS NULL OR p.DataHora >= @De)
      AND (@Ate        IS NULL OR p.DataHora <= @Ate)
    ORDER BY p.DataHora DESC";

            var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Limite", limite);
            cmd.Parameters.AddWithValue("@CodEmp", empresaFiltro);
            cmd.Parameters.AddWithValue("@Usuario", string.IsNullOrWhiteSpace(usuario) ? (object)DBNull.Value : usuario.Trim());
            cmd.Parameters.AddWithValue("@De", de.HasValue ? (object)de.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Ate", ate.HasValue ? (object)ate.Value.Date.AddDays(1).AddSeconds(-1) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Tipo", tipo);
            cmd.Parameters.AddWithValue("@TipoEvento", string.IsNullOrWhiteSpace(tipoEvento) ? (object)DBNull.Value : tipoEvento.Trim());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new LogAcessoModel
                {
                    Id = reader.GetInt32(0),
                    Usuario = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    CodEmp = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    NomeEmpresa = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                    DataHora = reader.GetDateTime(4),
                    TipoEvento = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                    Ip = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    UserAgent = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                });
            }

            return lista;
        }

        public async Task<List<string>> ObterUsuariosAsync(int? codEmp = null)
        {
            var lista = new List<string>();
            var empresaFiltro = codEmp ?? CodEmpAtual;

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT DISTINCT UPPER(LTRIM(RTRIM(Usuario)))
                FROM ParametrosHelp
                WHERE CodEmp = @CodEmp
                  AND Tipo = 'LOG_ACESSO'
                  AND Usuario IS NOT NULL
                ORDER BY 1", conn);

            cmd.Parameters.AddWithValue("@CodEmp", empresaFiltro);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (!reader.IsDBNull(0))
                    lista.Add(reader.GetString(0));

            return lista;
        }
    }
}
