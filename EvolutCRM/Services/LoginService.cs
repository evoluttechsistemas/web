using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class LoginService
    {
        private readonly string _connection;
        private readonly UserState _userState;
        private readonly UsuarioStatusNotifier _statusNotifier;

        public LoginService(IConfiguration config, UserState userState, UsuarioStatusNotifier statusNotifier)
        {
            _connection = config.GetConnectionString("Connection")!;
            _userState = userState;
            _statusNotifier = statusNotifier;
        }

        private int CodEmpAtual
        {
            get
            {
                if (_userState.CurrentCompanyId > 0)
                    return _userState.CurrentCompanyId;

                return 2;
            }
        }

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        private SqlConnection GetConnection() => new SqlConnection(_connection);

        public async Task<List<EmpresaModels>> GetEmpresasAsync()
        {
            var empresas = new List<EmpresaModels>();

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT DISTINCT e.Codigo, e.Fantasia, e.NomeReduzido, ISNULL(u.Usuario, '') AS Usuario
                FROM Empresa e
                LEFT JOIN Usuario u
                       ON u.CodEmp = e.Codigo
                      AND ISNULL(u.Inativo, 'N') = 'N'
                WHERE e.Codigo = @CodEmp
                  AND (e.Inativo = 'N' OR e.Inativo IS NULL)
                ORDER BY e.NomeReduzido", conn);

            AddCodEmp(cmd);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                empresas.Add(new EmpresaModels
                {
                    Codigo = reader.GetInt32(0),
                    Fantasia = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    NomeReduzido = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Usuario = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }

            return empresas;
        }

        public async Task<UserModels?> LoginAsync(string usuario, string senha)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
        SELECT TOP 1 Codigo, Usuario, Senha, ISNULL(Inativo,'N'), ISNULL(CodEmp,0)
        FROM Usuario
        WHERE UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
          AND Senha = @Senha
          AND ISNULL(Inativo, 'N') = 'N'
          AND ISNULL(Help, 'N') = 'S'
        ORDER BY Codigo", conn);

            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@Senha", senha);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserModels
                {
                    Codigo = reader.GetInt32(0),
                    Usuario = reader.GetString(1),
                    Senha = reader.GetString(2),
                    Inativo = false,
                    CodEmp = reader.GetInt32(4)
                };
            }

            return null;
        }

        public async Task AtualizarStatusOnlineAsync(string usuario, bool online)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
        UPDATE Usuario
        SET 
            Online = @Online,
            UltimaAtividadeOnline = CASE 
                WHEN @Online = 'S' THEN GETDATE()
                ELSE UltimaAtividadeOnline
            END,
            -- Ficou Offline (manual ou logout): limpa o status (Banheiro, Treinamento...)
            CodUsuarioStatus = CASE WHEN @Online = 'S' THEN CodUsuarioStatus ELSE NULL END,
            DataHoraStatus   = CASE WHEN @Online = 'S' THEN DataHoraStatus   ELSE NULL END
        WHERE CodEmp = @CodEmp
          AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            cmd.Parameters.AddWithValue("@Online", online ? "S" : "N");

            await cmd.ExecuteNonQueryAsync();

            _statusNotifier.Notificar(CodEmpAtual);
        }

        /// <param name="manterOnline">
        /// true = a pessoa não escolheu ficar Offline, então volta a marcar Online = 'S'
        /// (ex.: minimizou a janela, o navegador marcou offline, mas o HELP continua aberto).
        /// </param>
        public async Task RegistrarAtividadeOnlineAsync(string usuario, bool manterOnline = false)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
        UPDATE Usuario
        SET 
            UltimaAtividadeOnline = GETDATE(),
            Online = CASE WHEN @ManterOnline = 1 THEN 'S' ELSE Online END
        WHERE CodEmp = @CodEmp
          AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
          AND ISNULL(Inativo, 'N') = 'N'
          AND ISNULL(Help, 'N') = 'S'", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            cmd.Parameters.AddWithValue("@ManterOnline", manterOnline ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<UsuarioOnlineModel>> ObterUsuariosStatusAsync()
        {
            var lista = new List<UsuarioOnlineModel>();

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
SELECT
    LTRIM(RTRIM(Usuario)) AS Usuario,
    CASE
        WHEN ISNULL(Online, 'N') = 'S'
         AND UltimaAtividadeOnline IS NOT NULL
         AND DATEDIFF(SECOND, UltimaAtividadeOnline, GETDATE()) <= 40
        THEN 'S'
        ELSE 'N'
    END AS Online
FROM Usuario
WHERE CodEmp = @CodEmp
  AND ISNULL(Inativo, 'N') = 'N'
  AND ISNULL(Help, 'N') = 'S'
  AND Codigo <> 9
ORDER BY Usuario", conn);

            AddCodEmp(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new UsuarioOnlineModel
                {
                    Usuario = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    Online = !reader.IsDBNull(1) &&
                             reader.GetString(1).Trim().ToUpperInvariant() == "S"
                });
            }

            return lista;
        }
    }
}
