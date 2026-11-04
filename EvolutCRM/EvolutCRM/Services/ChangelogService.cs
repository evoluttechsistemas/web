using Microsoft.Data.SqlClient;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class ChangelogService
    {
        private readonly string _conn;
        private readonly UserState? _userState;

        public ChangelogService(string connectionString)
        {
            _conn = connectionString;
        }

        public ChangelogService(string connectionString, UserState userState)
        {
            _conn = connectionString;
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

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        public async Task<List<ChangelogVersao>> ObterChangelogNaoLidoAsync(string usuario, string sistema)
        {
            var itens = new List<ChangelogItem>();

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                SELECT
                    P.Codigo,
                    P.Titulo,
                    ISNULL(P.Descricao, '')              AS Descricao,
                    ISNULL(P.Icone, 'ti-news')           AS Icone,
                    ISNULL(P.TipoAlteracao,'MELHORIA')  AS TipoAlteracao,
                    ISNULL(P.Versao, '')                 AS Versao,
                    P.Destaque,
                    P.DataHora,
                    ISNULL(P.Referencia, '')             AS Referencia
                FROM ParametrosHelp P
                WHERE P.CodEmp = @CodEmp
                  AND P.Tipo  = 'CHANGELOG'
                  AND P.Ativo = 'S'
                  AND (P.Sistema = @Sistema OR P.Sistema = 'AMBOS')
                  AND NOT EXISTS (
                        SELECT 1
                        FROM ParametrosHelpLido L
                        WHERE L.CodEmp = @CodEmp
                          AND L.CodParametrosHelp = P.Codigo
                          AND UPPER(L.Usuario) = UPPER(@Usuario)
                          AND L.NaoMostrarMais = 'S'
                  )
                ORDER BY P.Versao DESC, P.Destaque DESC, P.Codigo ASC", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@Sistema", sistema);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                itens.Add(new ChangelogItem
                {
                    Codigo = rd.GetInt32(0),
                    Titulo = rd.GetString(1),
                    Descricao = rd.GetString(2),
                    Icone = rd.GetString(3),
                    TipoAlteracao = rd.GetString(4),
                    Versao = rd.GetString(5),
                    Destaque = rd.GetString(6) == "S",
                    DataHora = rd.GetDateTime(7),
                    Referencia = rd.GetString(8)
                });
            }

            var versoes = itens
                .GroupBy(i => i.Versao)
                .Select(g => new ChangelogVersao
                {
                    Versao = g.Key,
                    Sistema = sistema,
                    Itens = g.ToList()
                })
                .ToList();

            return versoes;
        }

        public async Task<List<ChangelogItemAdminDto>> ListarAdminAsync()
        {
            var lista = new List<ChangelogItemAdminDto>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        SELECT Codigo, Titulo, ISNULL(Descricao,'') AS Descricao,
               ISNULL(Versao,'') AS Versao, Sistema,
               ISNULL(TipoAlteracao,'MELHORIA') AS TipoAlteracao,
               ISNULL(Icone,'ti-news') AS Icone,
               ISNULL(Referencia,'') AS Referencia,
               Destaque, DataHora, Ativo
        FROM ParametrosHelp
        WHERE CodEmp = @CodEmp
          AND Tipo = 'CHANGELOG'
        ORDER BY DataHora DESC", conn);

            AddCodEmp(cmd);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new ChangelogItemAdminDto
                {
                    Codigo = rd.GetInt32(0),
                    Titulo = rd.GetString(1),
                    Descricao = rd.GetString(2),
                    Versao = rd.GetString(3),
                    Sistema = rd.GetString(4),
                    TipoAlteracao = rd.GetString(5),
                    Icone = rd.GetString(6),
                    Referencia = rd.GetString(7),
                    Destaque = rd.GetString(8) == "S",
                    DataHora = rd.GetDateTime(9),
                    Ativo = rd.GetString(10) == "S"
                });

            return lista;
        }

        public async Task InserirAsync(ParametrosHelpAdminModel m)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        INSERT INTO ParametrosHelp
            (CodEmp, Tipo, Sistema, Versao, Titulo, Descricao, Icone, TipoAlteracao,
             DataHora, Ativo, Destaque, Referencia)
        VALUES
            (@CodEmp, 'CHANGELOG', @Sistema, @Versao, @Titulo, @Descricao, @Icone,
             @TipoAlteracao, GETDATE(), 'S', @Destaque, @Referencia)", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Sistema", m.Sistema);
            cmd.Parameters.AddWithValue("@Versao", m.Versao);
            cmd.Parameters.AddWithValue("@Titulo", m.Titulo);
            cmd.Parameters.AddWithValue("@Descricao", m.Descricao ?? "");
            cmd.Parameters.AddWithValue("@Icone", m.Icone ?? "ti-news");
            cmd.Parameters.AddWithValue("@TipoAlteracao", m.TipoAlteracao);
            cmd.Parameters.AddWithValue("@Destaque", m.Destaque ? "S" : "N");
            cmd.Parameters.AddWithValue("@Referencia", (object?)(m.Referencia) ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AtualizarAsync(ParametrosHelpAdminModel m)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        UPDATE ParametrosHelp SET
            Sistema       = @Sistema,
            Versao        = @Versao,
            Titulo        = @Titulo,
            Descricao     = @Descricao,
            Icone         = @Icone,
            TipoAlteracao = @TipoAlteracao,
            Destaque      = @Destaque,
            Referencia    = @Referencia
        WHERE Codigo = @Codigo
          AND CodEmp = @CodEmp
          AND Tipo = 'CHANGELOG'", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", m.Codigo);
            cmd.Parameters.AddWithValue("@Sistema", m.Sistema);
            cmd.Parameters.AddWithValue("@Versao", m.Versao);
            cmd.Parameters.AddWithValue("@Titulo", m.Titulo);
            cmd.Parameters.AddWithValue("@Descricao", m.Descricao ?? "");
            cmd.Parameters.AddWithValue("@Icone", m.Icone ?? "ti-news");
            cmd.Parameters.AddWithValue("@TipoAlteracao", m.TipoAlteracao);
            cmd.Parameters.AddWithValue("@Destaque", m.Destaque ? "S" : "N");
            cmd.Parameters.AddWithValue("@Referencia", (object?)(m.Referencia) ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ExcluirItemAsync(int codigo)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmdLido = new SqlCommand(@"
                DELETE FROM ParametrosHelpLido
                WHERE CodParametrosHelp = @Codigo
                  AND CodEmp = @CodEmp", conn);

            AddCodEmp(cmdLido);
            cmdLido.Parameters.AddWithValue("@Codigo", codigo);
            await cmdLido.ExecuteNonQueryAsync();

            using var cmd = new SqlCommand(@"
                DELETE FROM ParametrosHelp
                WHERE Codigo = @Codigo
                  AND CodEmp = @CodEmp
                  AND Tipo = 'CHANGELOG'", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarcarVerDepoisAsync(string usuario, List<int> codigos)
        {
            if (!codigos.Any()) return;

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            foreach (var cod in codigos)
            {
                using var cmd = new SqlCommand(@"
                    IF NOT EXISTS (
                        SELECT 1 FROM ParametrosHelpLido
                        WHERE CodEmp = @CodEmp
                          AND CodParametrosHelp = @Cod
                          AND UPPER(Usuario) = UPPER(@Usuario)
                    )
                    INSERT INTO ParametrosHelpLido
                        (CodEmp, CodParametrosHelp, Usuario, NaoMostrarMais, DataHoraLido)
                    VALUES
                        (@CodEmp, @Cod, @Usuario, 'N', GETDATE())", conn);

                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", cod);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task MarcarNaoMostrarMaisAsync(string usuario, List<int> codigos)
        {
            if (!codigos.Any()) return;

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            foreach (var cod in codigos)
            {
                using var cmd = new SqlCommand(@"
                    IF EXISTS (
                        SELECT 1 FROM ParametrosHelpLido
                        WHERE CodEmp = @CodEmp
                          AND CodParametrosHelp = @Cod
                          AND UPPER(Usuario) = UPPER(@Usuario)
                    )
                        UPDATE ParametrosHelpLido
                        SET NaoMostrarMais = 'S', DataHoraLido = GETDATE()
                        WHERE CodEmp = @CodEmp
                          AND CodParametrosHelp = @Cod
                          AND UPPER(Usuario) = UPPER(@Usuario)
                    ELSE
                        INSERT INTO ParametrosHelpLido
                            (CodEmp, CodParametrosHelp, Usuario, NaoMostrarMais, DataHoraLido)
                        VALUES
                            (@CodEmp, @Cod, @Usuario, 'S', GETDATE())", conn);

                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", cod);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
