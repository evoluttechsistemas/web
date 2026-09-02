using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class LogFinanceiroService
    {
        private readonly string _connection;
        private readonly UserState? _userState;

        public const string BAIXA_TITULO = "BAIXA_TITULO";
        public const string PIX_GERADO = "PIX_GERADO";
        public const string WHATSAPP_LEMBRETE = "WHATSAPP_LEMBRETE";
        public const string WHATSAPP_COBRANCA = "WHATSAPP_COBRANCA";
        public const string WHATSAPP_PIX = "WHATSAPP_PIX";

        public LogFinanceiroService(IConfiguration config)
        {
            _connection = config.GetConnectionString("Connection")!;
        }

        public LogFinanceiroService(IConfiguration config, UserState userState)
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
        // GRAVAR
        // ─────────────────────────────────────────────

        public async Task RegistrarAsync(
            string usuario,
            int codEmp,
            string nomeEmpresa,
            string tipoEvento,
            string? detalhe = null,
            string? ip = null)
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
    (@CodEmp, 'LOG_FINANCEIRO', 'HELP', @CodEmpStr, @TipoEvento, @NomeEmpresa, @Ip, @Detalhe,
     GETDATE(), 'S', 'N', @Usuario)", conn);

                cmd.Parameters.AddWithValue("@CodEmp", codEmp);
                cmd.Parameters.AddWithValue("@CodEmpStr", codEmp.ToString());

                cmd.Parameters.AddWithValue("@Usuario",
                    string.IsNullOrWhiteSpace(usuario)
                        ? ""
                        : usuario.Trim().ToUpperInvariant());

                cmd.Parameters.AddWithValue("@TipoEvento",
                    string.IsNullOrWhiteSpace(tipoEvento)
                        ? ""
                        : tipoEvento.Trim());

                cmd.Parameters.AddWithValue("@NomeEmpresa",
                    (nomeEmpresa ?? "").Length > 100
                        ? (nomeEmpresa ?? "")[..100]
                        : (nomeEmpresa ?? ""));

                cmd.Parameters.AddWithValue("@Ip",
                    (object?)ip ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@Detalhe",
                    (object?)detalhe ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ERRO LogFinanceiroService.RegistrarAsync: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // CONSULTAR
        // ─────────────────────────────────────────────

        public async Task<List<LogAcessoModel>> ObterAsync(
            string? usuario = null,
            int? codEmp = null,
            DateTime? de = null,
            DateTime? ate = null,
            int limite = 500,
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
  AND p.Tipo = 'LOG_FINANCEIRO'
  AND (@TipoEvento IS NULL OR p.Titulo = @TipoEvento)
  AND (@Usuario    IS NULL OR UPPER(LTRIM(RTRIM(p.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario))))
  AND (@De         IS NULL OR p.DataHora >= @De)
  AND (@Ate        IS NULL OR p.DataHora <= @Ate)
ORDER BY p.DataHora DESC";

            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Limite", limite);
            cmd.Parameters.AddWithValue("@CodEmp", empresaFiltro);

            cmd.Parameters.AddWithValue("@Usuario",
                string.IsNullOrWhiteSpace(usuario)
                    ? (object)DBNull.Value
                    : usuario.Trim());

            cmd.Parameters.AddWithValue("@De",
                de.HasValue
                    ? (object)de.Value
                    : DBNull.Value);

            cmd.Parameters.AddWithValue("@Ate",
                ate.HasValue
                    ? (object)ate.Value.Date.AddDays(1).AddSeconds(-1)
                    : DBNull.Value);

            cmd.Parameters.AddWithValue("@TipoEvento",
                string.IsNullOrWhiteSpace(tipoEvento)
                    ? (object)DBNull.Value
                    : tipoEvento.Trim());

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
                    UserAgent = reader.IsDBNull(7) ? null : reader.GetString(7).Trim()
                });
            }

            return lista;
        }
    }
}
