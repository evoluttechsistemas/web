using System.Data;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class CurriculoOnlineService
    {
        private readonly string _connection;
        private readonly UserState _userState;


        public async Task<int> SalvarAsync(CandidatoTalentoModel candidato)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidato.FotoBase64))
                    throw new Exception("A foto é obrigatória.");

                using var conn = new SqlConnection(_connection);
                await conn.OpenAsync();

                var empresa = candidato.CodEmp > 0
                    ? candidato.CodEmp
                    : (_userState.CurrentCompanyId > 0 ? _userState.CurrentCompanyId : 1);

                var sql = @"
INSERT INTO CurriculoOnline
(
    CodEmp,
    NomeCompleto,
    Telefone,
    Email,
    Cidade,
    Estado,
    AreaInteresse,
    CargoPretendido,
    Escolaridade,
    Experiencias,
    Cursos,
    Habilidades,
    Disponibilidade,
    PretensaoSalarial,
    Observacoes,
    Foto,
    FotoContentType,
    Status,
    DataCadastro
)
OUTPUT INSERTED.Id
VALUES
(
    @CodEmp,
    @NomeCompleto,
    @Telefone,
    @Email,
    @Cidade,
    @Estado,
    @AreaInteresse,
    @CargoPretendido,
    @Escolaridade,
    @Experiencias,
    @Cursos,
    @Habilidades,
    @Disponibilidade,
    @PretensaoSalarial,
    @Observacoes,
    @Foto,
    @FotoContentType,
    @Status,
    GETDATE()
);";

                using var cmd = new SqlCommand(sql, conn);
                BuildCommonParams(cmd, candidato, empresa);

                var result = await cmd.ExecuteScalarAsync();
                var novoId = Convert.ToInt32(result);

                // Fire-and-forget — erro no e-mail não desfaz o cadastro
                _ = _emailService.EnviarNotificacaoNovoCurriculoAsync(
                        candidato.NomeCompleto,
                        candidato.AreaInteresse,
                        candidato.Cidade,
                        candidato.Estado);

                return novoId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao salvar currículo online: {ex.Message}", ex);
            }
        }

        public async Task<List<CandidatoTalentoModel>> ListarAsync(int? empresa = null)
        {
            var lista = new List<CandidatoTalentoModel>();
            var codEmp = empresa ?? _userState.CurrentCompanyId;

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT
    Id,
    CodEmp,
    NomeCompleto,
    Telefone,
    Email,
    Cidade,
    Estado,
    AreaInteresse,
    CargoPretendido,
    Escolaridade,
    Experiencias,
    Cursos,
    Habilidades,
    Disponibilidade,
    PretensaoSalarial,
    Observacoes,
    Foto,
    FotoContentType,
    Status,
    DataCadastro
FROM CurriculoOnline
WHERE (@CodEmp = 0 OR CodEmp = @CodEmp)
ORDER BY DataCadastro DESC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@CodEmp", SqlDbType.Int) { Value = codEmp });

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(MapCurriculo(rd));
            }

            return lista;
        }

        public async Task<CandidatoTalentoModel?> ObterAsync(int id)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT
    Id,
    CodEmp,
    NomeCompleto,
    Telefone,
    Email,
    Cidade,
    Estado,
    AreaInteresse,
    CargoPretendido,
    Escolaridade,
    Experiencias,
    Cursos,
    Habilidades,
    Disponibilidade,
    PretensaoSalarial,
    Observacoes,
    Foto,
    FotoContentType,
    Status,
    DataCadastro
FROM CurriculoOnline
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return MapCurriculo(rd);
        }

        public async Task AtualizarStatusAsync(int id, string status)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
UPDATE CurriculoOnline
SET Status = @Status
WHERE Id = @Id;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = status ?? "Novo" });

            await cmd.ExecuteNonQueryAsync();
        }

        private static void BuildCommonParams(SqlCommand cmd, CandidatoTalentoModel candidato, int empresa)
        {
            cmd.Parameters.Add(new SqlParameter("@CodEmp", SqlDbType.Int) { Value = empresa });
            cmd.Parameters.Add(new SqlParameter("@NomeCompleto", SqlDbType.NVarChar, 200) { Value = candidato.NomeCompleto.Trim() });
            cmd.Parameters.Add(new SqlParameter("@Telefone", SqlDbType.NVarChar, 50) { Value = candidato.Telefone.Trim() });
            cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = candidato.Email.Trim() });
            cmd.Parameters.Add(new SqlParameter("@Cidade", SqlDbType.NVarChar, 100) { Value = candidato.Cidade.Trim() });
            cmd.Parameters.Add(new SqlParameter("@Estado", SqlDbType.NVarChar, 2) { Value = candidato.Estado.Trim().ToUpper() });
            cmd.Parameters.Add(new SqlParameter("@AreaInteresse", SqlDbType.NVarChar, 120) { Value = candidato.AreaInteresse.Trim() });
            cmd.Parameters.Add(new SqlParameter("@CargoPretendido", SqlDbType.NVarChar, 120) { Value = DbText(candidato.CargoPretendido) });
            cmd.Parameters.Add(new SqlParameter("@Escolaridade", SqlDbType.NVarChar, 120) { Value = DbText(candidato.Escolaridade) });
            cmd.Parameters.Add(new SqlParameter("@Experiencias", SqlDbType.NVarChar) { Value = DbText(candidato.Experiencias) });
            cmd.Parameters.Add(new SqlParameter("@Cursos", SqlDbType.NVarChar) { Value = DbText(candidato.Cursos) });
            cmd.Parameters.Add(new SqlParameter("@Habilidades", SqlDbType.NVarChar) { Value = DbText(candidato.Habilidades) });
            cmd.Parameters.Add(new SqlParameter("@Disponibilidade", SqlDbType.NVarChar, 100) { Value = DbText(candidato.Disponibilidade) });
            cmd.Parameters.Add(new SqlParameter("@PretensaoSalarial", SqlDbType.NVarChar, 50) { Value = DbText(candidato.PretensaoSalarial) });
            cmd.Parameters.Add(new SqlParameter("@Observacoes", SqlDbType.NVarChar) { Value = DbText(candidato.Observacoes) });
            cmd.Parameters.Add(new SqlParameter("@Foto", SqlDbType.VarBinary) { Value = Convert.FromBase64String(candidato.FotoBase64) });
            cmd.Parameters.Add(new SqlParameter("@FotoContentType", SqlDbType.NVarChar, 80) { Value = candidato.FotoContentType });
            cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = string.IsNullOrWhiteSpace(candidato.Status) ? "Novo" : candidato.Status });
        }

        private static CandidatoTalentoModel MapCurriculo(SqlDataReader rd)
        {
            var fotoBytes = rd["Foto"] == DBNull.Value
                ? Array.Empty<byte>()
                : (byte[])rd["Foto"];

            return new CandidatoTalentoModel
            {
                Id = Convert.ToInt32(rd["Id"]),
                CodEmp = Convert.ToInt32(rd["CodEmp"]),
                NomeCompleto = rd["NomeCompleto"]?.ToString() ?? "",
                Telefone = rd["Telefone"]?.ToString() ?? "",
                Email = rd["Email"]?.ToString() ?? "",
                Cidade = rd["Cidade"]?.ToString() ?? "",
                Estado = rd["Estado"]?.ToString() ?? "",
                AreaInteresse = rd["AreaInteresse"]?.ToString() ?? "",
                CargoPretendido = rd["CargoPretendido"]?.ToString() ?? "",
                Escolaridade = rd["Escolaridade"]?.ToString() ?? "",
                Experiencias = rd["Experiencias"]?.ToString() ?? "",
                Cursos = rd["Cursos"]?.ToString() ?? "",
                Habilidades = rd["Habilidades"]?.ToString() ?? "",
                Disponibilidade = rd["Disponibilidade"]?.ToString() ?? "",
                PretensaoSalarial = rd["PretensaoSalarial"]?.ToString() ?? "",
                Observacoes = rd["Observacoes"]?.ToString() ?? "",
                FotoBase64 = fotoBytes.Length == 0 ? "" : Convert.ToBase64String(fotoBytes),
                FotoContentType = rd["FotoContentType"]?.ToString() ?? "image/jpeg",
                Status = rd["Status"]?.ToString() ?? "Novo",
                DataCadastro = Convert.ToDateTime(rd["DataCadastro"])
            };
        }

        // Adicione ao construtor
        private readonly EmailNotificacaoService _emailService;

        public CurriculoOnlineService(
            IConfiguration config,
            UserState userState,
            EmailNotificacaoService emailService)   // <-- novo parâmetro
        {
            _connection = config.GetConnectionString("Connection");
            _userState = userState;
            _emailService = emailService;
        }

        private static object DbText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }
    }
}
