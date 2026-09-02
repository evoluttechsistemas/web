using System.Data;
using System.Data.SqlClient;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class ComercialFluxoService
    {
        private readonly string _connection;

        public ComercialFluxoService(IConfiguration config)
        {
            _connection = config.GetConnectionString("Connection")
                ?? throw new Exception("ConnectionString 'Connection' não configurada.");
        }

        public async Task<List<ComercialFluxoMensagemModel>> ListarAsync(int codEmp)
        {
            var lista = new List<ComercialFluxoMensagemModel>();

            string sql = @"
SELECT 
    Codigo,
    CodEmp,
    DiasParaDisparo,
    Descricao,
    TextoWhatsApp,
    Link,
    TipoMidia,
    NomeArquivo,
    MimeType,
    Ativo,
    ISNULL(Ordem, 0) AS Ordem,
    HoraInicio,
    ISNULL(IntervaloMinutos, 1) AS IntervaloMinutos,
    ISNULL(Funil, 'TODOS') AS Funil,
    ISNULL(CamposPersonalizados, '') AS CamposPersonalizados,
    ISNULL(HorarioComercial, 'S') AS HorarioComercial,
    ISNULL(EnviarTodosFunis, 'S') AS EnviarTodosFunis
FROM ComercialFluxoMensagem
WHERE CodEmp = @CodEmp
ORDER BY DiasParaDisparo, Ordem, Codigo";

            using var con = new SqlConnection(_connection);
            await con.OpenAsync();

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ComercialFluxoMensagemModel
                {
                    Codigo = Convert.ToInt32(rd["Codigo"]),
                    CodEmp = Convert.ToInt32(rd["CodEmp"]),
                    DiasParaDisparo = Convert.ToInt32(rd["DiasParaDisparo"]),
                    Descricao = rd["Descricao"]?.ToString() ?? "",
                    TextoWhatsApp = rd["TextoWhatsApp"]?.ToString() ?? "",
                    Link = rd["Link"]?.ToString() ?? "",
                    TipoMidia = rd["TipoMidia"]?.ToString() ?? "",
                    NomeArquivo = rd["NomeArquivo"]?.ToString() ?? "",
                    MimeType = rd["MimeType"]?.ToString() ?? "",
                    Ativo = rd["Ativo"]?.ToString() ?? "S",
                    Ordem = Convert.ToInt32(rd["Ordem"]),

                    HoraInicio = rd["HoraInicio"] == DBNull.Value
                        ? new TimeSpan(8, 0, 0)
                        : (TimeSpan)rd["HoraInicio"],

                    IntervaloMinutos = Convert.ToInt32(rd["IntervaloMinutos"]),
                    Funil = rd["Funil"]?.ToString() ?? "TODOS",
                    CamposPersonalizados = rd["CamposPersonalizados"]?.ToString() ?? "",
                    HorarioComercial = rd["HorarioComercial"]?.ToString() ?? "S",
                    EnviarTodosFunis = rd["EnviarTodosFunis"]?.ToString() ?? "S"
                });
            }

            return lista;
        }

        public async Task SalvarAsync(ComercialFluxoMensagemModel model)
        {
            using var con = new SqlConnection(_connection);
            await con.OpenAsync();

            if (model.Codigo == 0)
            {
                string sql = @"
INSERT INTO ComercialFluxoMensagem
(
    CodEmp,
    DiasParaDisparo,
    Descricao,
    TextoWhatsApp,
    Link,
    TipoMidia,
    NomeArquivo,
    MimeType,
    Arquivo,
    Ativo,
    Ordem,
    HoraInicio,
    IntervaloMinutos,
    Funil,
    CamposPersonalizados,
    HorarioComercial,
    EnviarTodosFunis,
    DataCadastro
)
VALUES
(
    @CodEmp,
    @DiasParaDisparo,
    @Descricao,
    @TextoWhatsApp,
    @Link,
    @TipoMidia,
    @NomeArquivo,
    @MimeType,
    @Arquivo,
    @Ativo,
    @Ordem,
    @HoraInicio,
    @IntervaloMinutos,
    @Funil,
    @CamposPersonalizados,
    @HorarioComercial,
    @EnviarTodosFunis,
    GETDATE()
)";

                using var cmd = new SqlCommand(sql, con);
                AddParams(cmd, model);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                string sql = @"
UPDATE ComercialFluxoMensagem
SET 
    DiasParaDisparo = @DiasParaDisparo,
    Descricao = @Descricao,
    TextoWhatsApp = @TextoWhatsApp,
    Link = @Link,
    TipoMidia = @TipoMidia,
    NomeArquivo = CASE WHEN @NomeArquivo = '' THEN NomeArquivo ELSE @NomeArquivo END,
    MimeType = CASE WHEN @MimeType = '' THEN MimeType ELSE @MimeType END,
    Arquivo = CASE WHEN @Arquivo IS NULL THEN Arquivo ELSE @Arquivo END,
    Ativo = @Ativo,
    Ordem = @Ordem,
    HoraInicio = @HoraInicio,
    IntervaloMinutos = @IntervaloMinutos,
    Funil = @Funil,
    CamposPersonalizados = @CamposPersonalizados,
    HorarioComercial = @HorarioComercial,
    EnviarTodosFunis = @EnviarTodosFunis,
    DataAlteracao = GETDATE()
WHERE Codigo = @Codigo
AND CodEmp = @CodEmp";

                using var cmd = new SqlCommand(sql, con);
                AddParams(cmd, model);
                cmd.Parameters.AddWithValue("@Codigo", model.Codigo);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private void AddParams(SqlCommand cmd, ComercialFluxoMensagemModel model)
        {
            cmd.Parameters.AddWithValue("@CodEmp", model.CodEmp);
            cmd.Parameters.AddWithValue("@DiasParaDisparo", model.DiasParaDisparo);
            cmd.Parameters.AddWithValue("@Descricao", model.Descricao ?? "");
            cmd.Parameters.AddWithValue("@TextoWhatsApp", model.TextoWhatsApp ?? "");
            cmd.Parameters.AddWithValue("@Link", model.Link ?? "");
            cmd.Parameters.AddWithValue("@TipoMidia", model.TipoMidia ?? "");
            cmd.Parameters.AddWithValue("@NomeArquivo", model.NomeArquivo ?? "");
            cmd.Parameters.AddWithValue("@MimeType", model.MimeType ?? "");
            cmd.Parameters.AddWithValue("@Ativo", string.IsNullOrWhiteSpace(model.Ativo) ? "S" : model.Ativo);
            cmd.Parameters.AddWithValue("@Ordem", model.Ordem);

            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                model.HoraInicio.HasValue
                    ? model.HoraInicio.Value
                    : new TimeSpan(8, 0, 0);

            cmd.Parameters.AddWithValue("@IntervaloMinutos",
                model.IntervaloMinutos <= 0 ? 1 : model.IntervaloMinutos);

            cmd.Parameters.AddWithValue("@Funil",
                string.IsNullOrWhiteSpace(model.Funil) ? "TODOS" : model.Funil);

            cmd.Parameters.AddWithValue("@CamposPersonalizados",
                model.CamposPersonalizados ?? "");

            cmd.Parameters.AddWithValue("@HorarioComercial",
                string.IsNullOrWhiteSpace(model.HorarioComercial) ? "S" : model.HorarioComercial);

            cmd.Parameters.AddWithValue("@EnviarTodosFunis",
                string.IsNullOrWhiteSpace(model.EnviarTodosFunis) ? "S" : model.EnviarTodosFunis);

            var paramArquivo = cmd.Parameters.Add("@Arquivo", SqlDbType.VarBinary, -1);

            paramArquivo.Value =
                model.Arquivo != null && model.Arquivo.Length > 0
                    ? model.Arquivo
                    : DBNull.Value;
        }

        public async Task ExcluirAsync(int codigo, int codEmp)
        {
            string sql = @"
DELETE FROM ComercialFluxoMensagem
WHERE Codigo = @Codigo
AND CodEmp = @CodEmp";

            using var con = new SqlConnection(_connection);
            await con.OpenAsync();

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}