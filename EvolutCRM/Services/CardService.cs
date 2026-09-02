using System.Data;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class CardService
    {
        private readonly string _connection;
        private readonly UserState _userState;
        private readonly HealthMonitorService _monitor;

        public CardService(IConfiguration config, UserState userState, HealthMonitorService monitor)
        {
            _connection = config.GetConnectionString("Connection");
            _userState = userState;
            _monitor = monitor;
        }

        public async Task<CardModels?> GetCardAsync(int codigo, string usuario, int empresa)
        {
            usuario = usuario?.Trim().ToUpper() ?? "";
            empresa = empresa == 0 ? _userState.CurrentCompanyId : empresa;

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // 🔥 Adicionada coluna FaixaLead
            var sql = @"
SELECT Codigo, Descricao, CodCliente, NomeCliente, EmailCliente,
       Telefone, Celular, Whats, Ligacao, Telegram,
       DataCriacao, DataHoraUltimaGravacao, DataPrevisaoFechamento,
       UsuarioCard, Status, Funil, FaixaLead, TelefoneWhatsApp,
       ObservacaoCliente
FROM CRMC
WHERE Codigo = @Codigo 
  AND CodEmp = @Empresa";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Empresa", empresa);


            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new CardModels
            {
                Codigo = reader.GetInt32(0),
                Descricao = reader.IsDBNull(1) ? "" : reader.GetString(1),
                CodCliente = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                NomeCliente = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Telefone = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Celular = reader.IsDBNull(6) ? "" : reader.GetString(6),
                WhatsApp = reader["Whats"].ToString() == "S",
                Ligacao = reader["Ligacao"].ToString() == "S",
                Telegram = reader["Telegram"].ToString() == "S",
                DataCriacao = GetDateSafe(reader, 10),
                DataAlteracao = reader.IsDBNull(11)
    ? (DateTime?)null
    : DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Local),
                PrevisaoFechamento = GetDateSafe(reader, 12),
                Usuario = reader.IsDBNull(13) ? "" : reader.GetString(13),
                Status = reader.IsDBNull(14) ? "" : reader.GetString(14),
                Funil = reader.IsDBNull(15) ? "" : reader.GetString(15),
                TelefoneWhatsApp = reader.IsDBNull(17) ? "" : reader.GetString(17),
                ObservacaoCliente = reader.IsDBNull(18) ? "" : reader.GetString(18),
                FaixaLead = reader.IsDBNull(16) ? "" : reader.GetString(16).Trim().ToUpper()
            };
        }

        private DateTime? GetDateSafe(SqlDataReader reader, int index)
        {
            if (reader.IsDBNull(index)) return null;
            if (reader.GetValue(index) is DateTime dt) return dt;
            return DateTime.TryParse(reader.GetValue(index).ToString(), out var parsed) ? parsed : null;
        }

        public async Task<int> SaveCardAsync(CardModels card)
        {
            var usuarioParaGravar = string.IsNullOrWhiteSpace(card.Usuario)
                ? _userState.CurrentUser.Trim().ToUpper()
                : card.Usuario.Trim().ToUpper();

            try
            {
                using var conn = new SqlConnection(_connection);
                await conn.OpenAsync();

                object previsao = card.PrevisaoFechamento == null
                    ? DBNull.Value
                    : card.PrevisaoFechamento;

                if (card.Codigo == 0)
                {
                    var sql = @"
INSERT INTO CRMC 
(
    Descricao,
    CodCliente,
    NomeCliente,
    EmailCliente,
    Telefone,
    Celular,
    Whats,
    Ligacao,
    Telegram,
    DataCriacao,
    DataHoraUltimaGravacao,
    DataPrevisaoFechamento,
    DataFinalizacao,
    UsuarioCard,
    Status,
    Funil,
    TelefoneWhatsApp,
    ObservacaoCliente,
    CodEmp
)
OUTPUT INSERTED.Codigo
VALUES 
(
    @Descricao,
    @CodCliente,
    @NomeCliente,
    @EmailCliente,
    @Telefone,
    @Celular,
    @Whats,
    @Ligacao,
    @Telegram,
    GETDATE(),
    GETDATE(),
    @DataPrevisaoFechamento,
    CASE 
        WHEN @Status IN ('CONCLUIDO', 'PERDIDO') 
        THEN GETDATE() 
        ELSE NULL 
    END,
    @Usuario,
    @Status,
    @Funil,
    @TelefoneWhatsApp,
    @ObservacaoCliente,
    @Empresa
);";

                    using var cmd = new SqlCommand(sql, conn);
                    BuildCommonParams(cmd, card, usuarioParaGravar, previsao);
                    LogParams(cmd);

                    var result = await cmd.ExecuteScalarAsync();
                    var novoId = Convert.ToInt32(result);

                    _monitor.Log(LogCategory.CRM, LogSeverity.Success,
                        $"Card #{novoId} criado por {usuarioParaGravar}");

                    return novoId;
                }
                else
                {
                    var sql = @"
UPDATE CRMC SET
    Descricao = @Descricao,
    CodCliente = @CodCliente,
    NomeCliente = @NomeCliente,
    EmailCliente = @EmailCliente,
    Telefone = @Telefone,
    Celular = @Celular,
    Whats = @Whats,
    Ligacao = @Ligacao,
    Telegram = @Telegram,
    DataHoraUltimaGravacao = GETDATE(),
    DataPrevisaoFechamento = @DataPrevisaoFechamento,
    UsuarioCard = @Usuario,
    Status = @Status,
    TelefoneWhatsApp = @TelefoneWhatsApp,
    ObservacaoCliente = @ObservacaoCliente,
    DataFinalizacao =
        CASE
            WHEN @Status IN ('CONCLUIDO', 'PERDIDO')
                 AND DataFinalizacao IS NULL
            THEN GETDATE()
            WHEN @Status = 'ABERTO'
            THEN NULL
            ELSE DataFinalizacao
        END,
    Funil = @Funil,
    CodEmp = @Empresa
WHERE Codigo = @Codigo;";

                    using var cmd = new SqlCommand(sql, conn);
                    BuildCommonParams(cmd, card, usuarioParaGravar, previsao);

                    cmd.Parameters.Add(new SqlParameter("@Codigo", SqlDbType.Int)
                    {
                        Value = card.Codigo
                    });

                    LogParams(cmd);

                    await cmd.ExecuteNonQueryAsync();

                    _monitor.Log(LogCategory.CRM, LogSeverity.Info,
                        $"Card #{card.Codigo} atualizado por {usuarioParaGravar}");

                    return card.Codigo;
                }
            }
            catch (Exception ex)
            {
                _monitor.LogException(LogCategory.CRM, ex,
                    $"SaveCardAsync card #{card.Codigo} usuario {usuarioParaGravar}");
                throw new Exception($"Erro ao salvar card: {ex.Message}", ex);
            }
        }

        private void BuildCommonParams(SqlCommand cmd, CardModels card, string usuario, object previsao)
        {
            cmd.Parameters.Add(new SqlParameter("@Descricao", SqlDbType.NVarChar, 200) { Value = (object?)card.Descricao ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CodCliente", SqlDbType.Int) { Value = (object?)card.CodCliente ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@NomeCliente", SqlDbType.NVarChar, 200) { Value = (object?)card.NomeCliente ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@EmailCliente", SqlDbType.NVarChar, 200) { Value = (object?)card.Email ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Telefone", SqlDbType.NVarChar, 50) { Value = (object?)card.Telefone ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Whats", SqlDbType.Char, 1) { Value = card.WhatsApp ? "S" : "N" });
            cmd.Parameters.Add(new SqlParameter("@Ligacao", SqlDbType.Char, 1) { Value = card.Ligacao ? "S" : "N" });
            cmd.Parameters.Add(new SqlParameter("@Telegram", SqlDbType.Char, 1) { Value = card.Telegram ? "S" : "N" });
            cmd.Parameters.Add(new SqlParameter("@DataPrevisaoFechamento", SqlDbType.Date) { Value = previsao });
            cmd.Parameters.Add(new SqlParameter("@Usuario", SqlDbType.NVarChar, 100) { Value = usuario });
            cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 50) { Value = (object?)card.Status ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Funil", SqlDbType.NVarChar, 50) { Value = (object?)card.Funil ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Empresa", SqlDbType.Int) { Value = card.CodEmp });
            cmd.Parameters.Add(new SqlParameter("@ObservacaoCliente", SqlDbType.NVarChar, -1)
            {
                Value = string.IsNullOrWhiteSpace(card.ObservacaoCliente)
                    ? (object)DBNull.Value
                    : card.ObservacaoCliente.Trim()
            });

            var telefoneNormalizado = !string.IsNullOrWhiteSpace(card.TelefoneWhatsApp)
                ? LimparTelefoneWhatsApp(card.TelefoneWhatsApp)
                : !string.IsNullOrWhiteSpace(card.Celular)
                    ? LimparTelefoneWhatsApp(card.Celular)
                    : null;

            cmd.Parameters.Add(new SqlParameter("@Celular", SqlDbType.NVarChar, 50)
            {
                Value = (object?)telefoneNormalizado ?? DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@TelefoneWhatsApp", SqlDbType.NVarChar, 50)
            {
                Value = (object?)telefoneNormalizado ?? DBNull.Value
            });
        }

        private void LogParams(SqlCommand cmd)
        {
#if DEBUG
            Console.WriteLine("\n=== SQL PARAMS ===");
            foreach (SqlParameter p in cmd.Parameters)
            {
                Console.WriteLine($"{p.ParameterName} = {(p.Value == DBNull.Value ? "NULL" : p.Value)}  | Tipo={p.SqlDbType}");
            }
            Console.WriteLine("===================\n");
#endif
        }
        public async Task SalvarAnotacoesAsync(int codCrm, List<AnotacaoModel> anotacoes, string usuario)
        {
            if (anotacoes == null || anotacoes.Count == 0)
                return;

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            foreach (var anot in anotacoes.Where(a => a.IsNova))
            {
                try
                {
                    var sql = @"
INSERT INTO CRMAnotacao 
(
    CodCRMC, DataHora, Anotacao, Funil, Usuario,
    LidoCliente, LidoSuporte, Alterado, MensagemExcluida,
    EnvioCliente, StatusWhatsApp,
    Imagem, NomeImagem,
    Audio, AudioMimeType
)
VALUES 
(
    @CodCRMC, @DataHora, @Anotacao, @Funil, @Usuario,
    'N', 'S', 'N', 'N',
    @EnvioCliente, @StatusWhatsApp,
    @Imagem, @NomeImagem,
    @Audio, @AudioMimeType
)";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@CodCRMC", codCrm);
                    cmd.Parameters.AddWithValue("@DataHora", anot.DataHora);
                    cmd.Parameters.Add("@Anotacao", SqlDbType.VarChar, -1).Value = anot.Texto ?? "";
                    cmd.Parameters.AddWithValue("@Funil", anot.Funil ?? "");
                    cmd.Parameters.AddWithValue("@Usuario", usuario ?? "");
                    cmd.Parameters.AddWithValue("@EnvioCliente", anot.EnvioCliente ?? "N");
                    cmd.Parameters.Add(new SqlParameter("@StatusWhatsApp", SqlDbType.VarChar)
                    {
                        Value = string.IsNullOrWhiteSpace(anot.StatusWhatsApp)
            ? (object)DBNull.Value
            : anot.StatusWhatsApp
                    });

                    // NomeImagem — apenas uma vez
                    cmd.Parameters.AddWithValue("@NomeImagem",
                        string.IsNullOrWhiteSpace(anot.NomeImagem) ? (object)DBNull.Value : anot.NomeImagem);

                    // Imagem — bytes
                    object imagemBytes = DBNull.Value;
                    if (anot.ImagemBytes != null && anot.ImagemBytes.Length > 0)
                    {
                        imagemBytes = anot.ImagemBytes;
                    }
                    else if (!string.IsNullOrWhiteSpace(anot.CaminhoImagem) && anot.CaminhoImagem.Contains(";base64,"))
                    {
                        var base64 = anot.CaminhoImagem.Split(",")[1];
                        imagemBytes = Convert.FromBase64String(base64);
                    }
                    cmd.Parameters.Add(new SqlParameter("@Imagem", SqlDbType.VarBinary) { Value = imagemBytes });

                    // Áudio — bytes
                    object audioBytes = DBNull.Value;
                    if (!string.IsNullOrWhiteSpace(anot.Audio))
                    {
                        var base64 = anot.Audio.Contains(",")
                            ? anot.Audio.Split(',')[1]
                            : anot.Audio;
                        audioBytes = Convert.FromBase64String(base64);
                    }
                    cmd.Parameters.Add(new SqlParameter("@Audio", SqlDbType.VarBinary) { Value = audioBytes });
                    cmd.Parameters.AddWithValue("@AudioMimeType",
                        string.IsNullOrWhiteSpace(anot.AudioMimeType) ? (object)DBNull.Value : anot.AudioMimeType);

                    await cmd.ExecuteNonQueryAsync();
                    anot.IsNova = false;
                }
                catch (Exception ex)
                {
                    _monitor.LogException(LogCategory.CRM, ex,
                        $"SalvarAnotacoesAsync codCrm={codCrm}");
                    throw;
                }
            }
        }

        private string LimparTelefoneWhatsApp(string telefone)
        {
            if (string.IsNullOrWhiteSpace(telefone))
                return "";

            telefone = new string(
                telefone.Where(char.IsDigit).ToArray()
            );

            while (telefone.StartsWith("0"))
            {
                telefone = telefone.Substring(1);
            }

            if (!telefone.StartsWith("55"))
            {
                telefone = "55" + telefone;
            }

            return telefone;
        }

        public async Task SalvarAgendaAsync(AgendaModel agenda)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // ✅ Verificar se já existe agenda neste dia (EVITAR DUPLICATAS)
            var sqlVerifica = @"
SELECT COUNT(*) FROM Agenda
WHERE CodEmp = @Empresa
  AND CAST(DataAgendamento AS DATE) = CAST(@Data AS DATE)
  AND Descricao = @Descricao
  AND Usuario = @Usuario
  AND HoraAgendamento = @Hora
  AND ISNULL(Status, '') <> 'RESOLVIDO'";

            using var cmdVerifica = new SqlCommand(sqlVerifica, conn);
            cmdVerifica.Parameters.AddWithValue("@Empresa", agenda.CodEmp);
            cmdVerifica.Parameters.AddWithValue("@Data", agenda.DataAgendamento);
            cmdVerifica.Parameters.AddWithValue("@Descricao", agenda.Descricao ?? "");
            cmdVerifica.Parameters.AddWithValue("@Usuario", agenda.Usuario);
            cmdVerifica.Parameters.AddWithValue("@Hora", agenda.HoraAgendamento);

            var count = (int)await cmdVerifica.ExecuteScalarAsync();
            if (count > 0)
            {
                // ✅ Já existe, não salva duplicada
                return;
            }

            var sql = @"
INSERT INTO Agenda
(
    Descricao,
    DataAgendamento,
    HoraAgendamento,
    CodCliente,
    NomeCliente,
    EmailCliente,
    DataHoraUltimaGravacao,
    UsuarioUltimaGravacao,
    Status,
    CodEmp,
    CodCRMC,
    Usuario,
    TipoRepeticao,
    FrequenciaRepeticao,
    DataFimRepeticao,
    DiasRepeticao,
    Origem
)
VALUES
(
    @Descricao,
    @Data,
    @Hora,
    @CodCliente,
    @NomeCliente,
    @EmailCliente,
    GETDATE(),
    @UsuarioUltimaGravacao,
    @Status,
    @Empresa,
    @CodCRMC,
    @Usuario,
    @TipoRepeticao,
    @FrequenciaRepeticao,
    @DataFimRepeticao,
    @DiasRepeticao,
    @Origem
)";

            using var cmd = new SqlCommand(sql, conn);

            // TEXTOS
            cmd.Parameters.Add("@Descricao", SqlDbType.NVarChar, 200)
                .Value = agenda.Descricao ?? "";

            cmd.Parameters.Add("@NomeCliente", SqlDbType.NVarChar, 200)
                .Value = agenda.NomeCliente ?? "";

            cmd.Parameters.Add("@EmailCliente", SqlDbType.NVarChar, 200)
                .Value = agenda.EmailCliente ?? "";

            // DATAS / HORA
            cmd.Parameters.Add("@Data", SqlDbType.Date)
                .Value = agenda.DataAgendamento;

            cmd.Parameters.Add("@Hora", SqlDbType.Time)
                .Value = agenda.HoraAgendamento;

            // USUÁRIOS
            cmd.Parameters.Add("@UsuarioUltimaGravacao", SqlDbType.NVarChar, 100)
                .Value = agenda.Usuario;

            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 100)
                .Value = agenda.Usuario;

            // STATUS
            cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 20)
                .Value = agenda.Status;

            // EMPRESA
            cmd.Parameters.Add("@Empresa", SqlDbType.Int)
                .Value = agenda.CodEmp;

            // 🔹 CLIENTE (NUNCA NULL)
            cmd.Parameters.Add("@CodCliente", SqlDbType.Int)
                .Value = agenda.CodCliente > 0 ? agenda.CodCliente : 0;

            // 🔹 CRM (NUNCA NULL)
            cmd.Parameters.Add("@CodCRMC", SqlDbType.Int)
                .Value = agenda.CodCRMC.HasValue && agenda.CodCRMC > 0
                    ? agenda.CodCRMC.Value
                    : 0;

            // ===== CAMPOS DE REPETIÇÃO =====
            cmd.Parameters.Add("@TipoRepeticao", SqlDbType.NVarChar, 20)
                .Value = (object?)agenda.TipoRepeticao ?? DBNull.Value;

            cmd.Parameters.Add("@FrequenciaRepeticao", SqlDbType.NVarChar, 20)
                .Value = (object?)agenda.FrequenciaRepeticao ?? DBNull.Value;

            cmd.Parameters.Add("@DataFimRepeticao", SqlDbType.Date)
                .Value = (object?)agenda.DataFimRepeticao ?? DBNull.Value;

            cmd.Parameters.Add("@DiasRepeticao", SqlDbType.NVarChar, 50)
                .Value = (object?)agenda.DiasRepeticao ?? DBNull.Value;

            cmd.Parameters.Add("@Origem", SqlDbType.NVarChar, 50)
                .Value = (object?)agenda.Origem ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<AgendaModel>> GetAgendasAsync(int codCrmc)
        {
            var list = new List<AgendaModel>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
        SELECT Codigo, Descricao, DataAgendamento, HoraAgendamento, Status
        FROM Agenda
        WHERE CodCRMC = @CodCRMC
        ORDER BY DataAgendamento, HoraAgendamento";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodCRMC", codCrmc);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AgendaModel
                {
                    Codigo = rd.GetInt32(0),
                    Descricao = rd.GetString(1),
                    DataAgendamento = rd.GetDateTime(2),
                    HoraAgendamento = TimeSpan.Parse(rd["HoraAgendamento"].ToString()!),
                    Status = rd["Status"]?.ToString() ?? "AGENDADO"
                });
            }

            return list;
        }

        public async Task ResolverAgendaAsync(int codigo)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
UPDATE Agenda
SET Status = 'RESOLVIDO',
    DataHoraUltimaGravacao = GETDATE()
WHERE Codigo = @Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            await cmd.ExecuteNonQueryAsync();
            _monitor.Log(LogCategory.CRM, LogSeverity.Info,
                $"Agenda #{codigo} resolvida");
        }

        public async Task AtualizarAgendaAsync(AgendaModel agenda)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
    UPDATE Agenda
    SET Descricao = @Descricao,
    DataAgendamento = @Data,
    HoraAgendamento = @Hora,
    CodCliente = @CodCliente,
    NomeCliente = @NomeCliente,
    EmailCliente = @EmailCliente,
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario,
    Usuario = @Usuario,
    Status = @Status,
    CodEmp = @Empresa,
    CodCRMC = @CodCRMC,
    TipoRepeticao = @TipoRepeticao,
    FrequenciaRepeticao = @FrequenciaRepeticao,
    DataFimRepeticao = @DataFimRepeticao,
    DiasRepeticao = @DiasRepeticao,
    Origem = @Origem
WHERE Codigo = @Codigo;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", agenda.Codigo);
            cmd.Parameters.AddWithValue("@Descricao", agenda.Descricao);
            cmd.Parameters.AddWithValue("@Data", agenda.DataAgendamento);
            cmd.Parameters.AddWithValue("@Hora", agenda.HoraAgendamento);
            cmd.Parameters.AddWithValue("@CodCliente", agenda.CodCliente);
            cmd.Parameters.AddWithValue("@NomeCliente", agenda.NomeCliente ?? "");
            cmd.Parameters.AddWithValue("@EmailCliente", agenda.EmailCliente ?? "");
            cmd.Parameters.AddWithValue("@Usuario", agenda.Usuario);
            cmd.Parameters.AddWithValue("@Status", agenda.Status);
            cmd.Parameters.AddWithValue("@Empresa", agenda.CodEmp);
            cmd.Parameters.AddWithValue("@CodCRMC", agenda.CodCRMC ?? (object)DBNull.Value);

            // Campos de repetição
            cmd.Parameters.AddWithValue("@TipoRepeticao", (object?)agenda.TipoRepeticao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FrequenciaRepeticao", (object?)agenda.FrequenciaRepeticao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DataFimRepeticao", (object?)agenda.DataFimRepeticao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DiasRepeticao", (object?)agenda.DiasRepeticao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Origem", (object?)agenda.Origem ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<AgendaModel>> GetAgendasEmpresaAsync(int empresa)
        {
            var list = new List<AgendaModel>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT 
    a.Codigo,
    a.Descricao,
    a.DataAgendamento,
    a.HoraAgendamento,
    a.CodCliente,
    a.NomeCliente,
ISNULL(c.Apelido, '') AS ApelidoCliente,
a.EmailCliente,
    a.UsuarioUltimaGravacao AS Usuario,
    a.Status,
    a.CodEmp,
    a.CodCRMC,
    a.TipoRepeticao,
    a.FrequenciaRepeticao,
    a.DataFimRepeticao,
    a.DiasRepeticao,
    a.Origem,
    a.AgendaPaiCodigo,
    ISNULL(c.Celular,'') AS NumeroTelefone
FROM Agenda a
LEFT JOIN Cliente c ON c.Codigo = a.CodCliente
WHERE a.CodEmp = @Empresa
ORDER BY 
    CASE WHEN a.Status = 'AGENDADO' THEN 0 ELSE 1 END,
    a.DataAgendamento,
    a.HoraAgendamento";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empresa);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AgendaModel
                {
                    Codigo = rd.GetInt32(0),
                    Descricao = rd.GetString(1),
                    DataAgendamento = rd.GetDateTime(2),
                    HoraAgendamento = TimeSpan.Parse(rd["HoraAgendamento"].ToString()!),
                    CodCliente = rd.IsDBNull(4) ? 0 : rd.GetInt32(4),
                    NomeCliente = rd["NomeCliente"]?.ToString() ?? "",
                    ApelidoCliente = rd["ApelidoCliente"]?.ToString() ?? "",
                    EmailCliente = rd["EmailCliente"]?.ToString() ?? "",
                    Usuario = rd["Usuario"]?.ToString() ?? "",
                    Status = rd["Status"]?.ToString() ?? "AGENDADO",
                    CodEmp = Convert.ToInt32(rd["CodEmp"]),
                    CodCRMC = rd["CodCRMC"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CodCRMC"]),
                    TipoRepeticao = rd["TipoRepeticao"] == DBNull.Value ? null : rd["TipoRepeticao"]?.ToString(),
                    FrequenciaRepeticao = rd["FrequenciaRepeticao"] == DBNull.Value ? null : rd["FrequenciaRepeticao"]?.ToString(),
                    DataFimRepeticao = rd["DataFimRepeticao"] == DBNull.Value ? null : Convert.ToDateTime(rd["DataFimRepeticao"]),
                    DiasRepeticao = rd["DiasRepeticao"] == DBNull.Value ? null : rd["DiasRepeticao"]?.ToString(),
                    Origem = rd["Origem"] == DBNull.Value ? null : rd["Origem"]?.ToString(),
                    AgendaPaiCodigo = rd["AgendaPaiCodigo"] == DBNull.Value ? null : Convert.ToInt32(rd["AgendaPaiCodigo"]),
                    NumeroTelefone = rd["NumeroTelefone"]?.ToString() ?? ""
                });
            }

            return list;
        }

        public async Task<int> MaterializarOcorrenciaAgendaAsync(AgendaModel agendaMestre, DateTime dataOcorrencia)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
INSERT INTO Agenda
(
    Descricao,
    DataAgendamento,
    HoraAgendamento,
    CodCliente,
    NomeCliente,
    EmailCliente,
    DataHoraUltimaGravacao,
    UsuarioUltimaGravacao,
    Status,
    CodEmp,
    CodCRMC,
    Usuario,
    Origem,
    AgendaPaiCodigo
)
OUTPUT INSERTED.Codigo
VALUES
(
    @Descricao,
    @Data,
    @Hora,
    @CodCliente,
    @NomeCliente,
    @EmailCliente,
    GETDATE(),
    @Usuario,
    @Status,
    @Empresa,
    @CodCRMC,
    @Usuario,
    @Origem,
    @AgendaPaiCodigo
)";

            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.Add("@Descricao", SqlDbType.NVarChar, 200).Value = agendaMestre.Descricao ?? "";
            cmd.Parameters.Add("@Data", SqlDbType.Date).Value = dataOcorrencia;
            cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = agendaMestre.HoraAgendamento;
            cmd.Parameters.Add("@CodCliente", SqlDbType.Int).Value = agendaMestre.CodCliente > 0 ? agendaMestre.CodCliente : 0;
            cmd.Parameters.Add("@NomeCliente", SqlDbType.NVarChar, 200).Value = agendaMestre.NomeCliente ?? "";
            cmd.Parameters.Add("@EmailCliente", SqlDbType.NVarChar, 200).Value = agendaMestre.EmailCliente ?? "";
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 100).Value = agendaMestre.Usuario;
            cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = "AGENDADO";
            cmd.Parameters.Add("@Empresa", SqlDbType.Int).Value = agendaMestre.CodEmp;
            cmd.Parameters.Add("@CodCRMC", SqlDbType.Int).Value = agendaMestre.CodCRMC.HasValue && agendaMestre.CodCRMC > 0 ? agendaMestre.CodCRMC.Value : 0;
            cmd.Parameters.Add("@Origem", SqlDbType.NVarChar, 50).Value = (object?)agendaMestre.Origem ?? DBNull.Value;
            cmd.Parameters.Add("@AgendaPaiCodigo", SqlDbType.Int).Value = agendaMestre.Codigo;

            var novoCodigo = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(novoCodigo);
        }

        public async Task<List<AgendaModel>> GetAgendasFiltradasAsync(
    int empresa,
    string status,
    DateTime? inicio,
    DateTime? fim)
        {
            var list = new List<AgendaModel>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 30
    Codigo, Descricao, DataAgendamento, HoraAgendamento,
    UsuarioUltimaGravacao, Status, CodCRMC
FROM Agenda
WHERE CodEmp = @Empresa
  AND (@Status = 'TODOS' OR Status = @Status)
  AND (@Inicio IS NULL OR DataAgendamento >= @Inicio)
  AND (@Fim IS NULL OR DataAgendamento <= @Fim)
ORDER BY DataAgendamento DESC, HoraAgendamento DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empresa);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Inicio", (object?)inicio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Fim", (object?)fim ?? DBNull.Value);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AgendaModel
                {
                    Codigo = rd.GetInt32(0),
                    Descricao = rd.GetString(1),
                    DataAgendamento = rd.GetDateTime(2),
                    HoraAgendamento = TimeSpan.Parse(rd["HoraAgendamento"].ToString()!),
                    Usuario = rd["UsuarioUltimaGravacao"].ToString()!,
                    Status = rd["Status"].ToString()!,
                    CodCRMC = rd.GetInt32(6)
                });
            }

            return list;
        }

        public async Task<List<AnotacaoModel>> GetAnotacoesAsync(int codCrm)
        {
            var list = new List<AnotacaoModel>();
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();



            var sql = @"SELECT Codigo, DataHora, Anotacao, Funil, EnvioCliente, StatusWhatsApp, CaminhoImagem, NomeImagem, Imagem, Audio, AudioMimeType, Alterado, MensagemExcluida, Usuario
FROM CRMAnotacao
WHERE CodCRMC = @CodCRMC
ORDER BY Codigo DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodCRMC", codCrm);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AnotacaoModel
                {
                    Codigo = rd.GetInt32(rd.GetOrdinal("Codigo")),
                    DataHora = rd.GetDateTime(rd.GetOrdinal("DataHora")),
                    Texto = rd["Anotacao"]?.ToString() ?? "",
                    Funil = rd["Funil"]?.ToString() ?? "",
                    EnvioCliente = rd["EnvioCliente"]?.ToString() ?? "",
                    StatusWhatsApp = rd["StatusWhatsApp"]?.ToString() ?? "",
                    NomeImagem = rd["NomeImagem"]?.ToString() ?? "",
                    Audio = rd["Audio"] == DBNull.Value || rd["Audio"] == null
    ? ""
    : $"data:{rd["AudioMimeType"]?.ToString() ?? "audio/ogg"};base64,{Convert.ToBase64String((byte[])rd["Audio"])}",
                    AudioMimeType = rd["AudioMimeType"]?.ToString() ?? "",
                    Alterado = rd["Alterado"]?.ToString() ?? "N",
                    MensagemExcluida = rd["MensagemExcluida"]?.ToString() ?? "N",
                    Usuario = rd["Usuario"]?.ToString() ?? "",
                    ImagemBytes = rd["Imagem"] == DBNull.Value ? null : (byte[])rd["Imagem"],
                    CaminhoImagem = rd["Imagem"] == DBNull.Value
    ? rd["CaminhoImagem"]?.ToString() ?? ""
    : $"data:image/jpeg;base64,{Convert.ToBase64String((byte[])rd["Imagem"])}",
                });
            }

            return list;
        }

        public async Task<List<CardModels>> BuscarCardsAsync(string nome, string telefone, int empresa)
        {
            var lista = new List<CardModels>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // 🔹 Normaliza telefone digitado
            telefone = new string((telefone ?? "").Where(char.IsDigit).ToArray());

            var sql = @"
        SELECT Codigo, Descricao, Celular
FROM CRMC
WHERE CodEmp = @Empresa
AND (
       (@Nome <> '' AND Descricao LIKE '%' + @Nome + '%')
    OR (@Tel  <> '' AND 
        REPLACE(REPLACE(REPLACE(REPLACE(Celular, '-', ''), ' ', ''), '(', ''), ')', '')
        LIKE '%' + @Tel + '%')
)
ORDER BY Descricao

    ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empresa);
            cmd.Parameters.AddWithValue("@Nome", nome ?? "");
            cmd.Parameters.AddWithValue("@Tel", telefone ?? "");

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                lista.Add(new CardModels
                {
                    Codigo = rdr.GetInt32(0),
                    Descricao = rdr.GetString(1),
                    Celular = rdr.IsDBNull(2) ? "" : rdr.GetString(2)
                });
            }

            return lista;
        }
        public async Task<List<ClienteModel>> BuscarClientesAsync(string termo, int empresa)
        {
            var resultado = new List<ClienteModel>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 10 
    Codigo,
    Nome,
    Apelido,
    Telefone
FROM Cliente
WHERE 
    (
        Nome     LIKE @Termo
     OR Apelido  LIKE @Termo
     OR Telefone LIKE @Termo
    )
ORDER BY Nome";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empresa);
            cmd.Parameters.AddWithValue("@Termo", "%" + termo + "%");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultado.Add(new ClienteModel
                {
                    Codigo = reader.GetInt32(0),
                    Nome = reader.GetString(1),
                    Apelido = reader.IsDBNull(2) ? "" : reader.GetString(2)
                });
            }


            return resultado;
        }

        public async Task<List<UserItem>> GetUsuariosAsync(int empresa)
        {
            var list = new List<UserItem>();
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // ✅ Busca todos os usuários cadastrados
            var sql = @"SELECT DISTINCT Usuario FROM Usuario WHERE ISNULL(Inativo,'N') = 'N' AND ISNULL(Help,'N') = 'S' ORDER BY Usuario";

            using var cmd = new SqlCommand(sql, conn);
            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                list.Add(new UserItem
                {
                    Username = rd["Usuario"]?.ToString() ?? ""
                });
            }

            Console.WriteLine($"🔍 Usuários carregados: {list.Count}");
            return list;
        }
        public async Task TransferirCardAsync(int codigo, string novoUsuario, int empresa)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
        UPDATE CRMC
        SET UsuarioCard = @NovoUsuario,
            DataHoraUltimaGravacao = GETDATE()
        WHERE Codigo = @Codigo AND CodEmp = @Empresa";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@NovoUsuario", novoUsuario.ToUpper());
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Empresa", empresa);

            await cmd.ExecuteNonQueryAsync();
            _monitor.Log(LogCategory.CRM, LogSeverity.Info,
                $"Card #{codigo} transferido para {novoUsuario}");
        }

        public async Task RegistrarWhatsAsync(int codCrm, string usuario)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
        INSERT INTO CRMAnotacao
        (CodCRMC, DataHora, Anotacao)
        VALUES
        (@Cod, GETDATE(), @Texto)";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Cod", codCrm);
            cmd.Parameters.AddWithValue("@Texto",
                $"WhatsApp visualizado pelo usuário {usuario} em {DateTime.Now:dd/MM/yyyy HH:mm}");

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetQuantidadeCardsNovosAsync(int codEmp)
        {
            string sql = @"
SELECT COUNT(1)
FROM CRMC WITH (NOLOCK)
WHERE ISNULL(Novo, 'N') = 'S'
  AND ISNULL(Status, 'ABERTO') = 'ABERTO'
  AND CodEmp = @CodEmp";

            using var con = new SqlConnection(_connection);
            await con.OpenAsync();

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public async Task<List<CardModels>> GetCardsAsync(string? usuario = null, int? empresa = null)
        {
            var empId = empresa ?? _userState.CurrentCompanyId;
            var cards = new List<CardModels>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT
    c.Codigo, c.Descricao, c.CodCliente, c.NomeCliente, c.EmailCliente,
    c.Telefone, c.Celular, c.Whats, c.Ligacao, c.Telegram,
    c.DataCriacao,
    ISNULL(ua.DataHoraUltimaAnotacao, c.DataHoraUltimaGravacao) AS DataAlteracao,
    c.DataPrevisaoFechamento,
    c.UsuarioCard, c.Status, c.Funil, c.FaixaLead,
    ISNULL(c.Novo, 'N') AS Novo
FROM CRMC c
OUTER APPLY (
    SELECT MAX(a.DataHora) AS DataHoraUltimaAnotacao
    FROM CRMAnotacao a
    WHERE a.CodCRMC = c.Codigo
      AND ISNULL(a.MensagemExcluida, 'N') = 'N'
      AND ISNULL(a.EnvioCliente, 'N') = 'N'
      AND a.Anotacao NOT LIKE 'WhatsApp visualizado pelo usuário%'
) ua
WHERE c.CodEmp = @Empresa";

            if (!string.IsNullOrWhiteSpace(usuario))
                sql += @" AND (
    c.UsuarioCard = @Usuario
    OR UPPER(ISNULL(c.UsuarioCard, '')) = 'NOVO'
)";

            sql += " ORDER BY ISNULL(ua.DataHoraUltimaAnotacao, c.DataHoraUltimaGravacao) DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empId);

            if (!string.IsNullOrWhiteSpace(usuario))
                cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpper());

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                cards.Add(new CardModels
                {
                    Codigo = reader.GetInt32(0),
                    Descricao = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    CodCliente = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    NomeCliente = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Telefone = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Celular = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    WhatsApp = reader["Whats"].ToString() == "S",
                    Ligacao = reader["Ligacao"].ToString() == "S",
                    Telegram = reader["Telegram"].ToString() == "S",
                    DataCriacao = GetDateSafe(reader, 10),
                    DataAlteracao = reader.IsDBNull(11)
                        ? (DateTime?)null
                        : DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Local),
                    PrevisaoFechamento = GetDateSafe(reader, 12),
                    Usuario = reader.IsDBNull(13) ? "" : reader.GetString(13),
                    Status = reader.IsDBNull(14) ? "" : reader.GetString(14),
                    Funil = reader.IsDBNull(15) ? "" : reader.GetString(15),
                    FaixaLead = reader.IsDBNull(16) ? "" : reader.GetString(16).Trim().ToUpper(),
                    Novo = reader.IsDBNull(17) ? "" : reader.GetString(17),
                });
            }

            return cards;
        }

        public async Task<List<CardModels>> GetCardsComUltimaMensagemAsync(int? empresa = null)
        {
            var empId = empresa ?? _userState.CurrentCompanyId;
            var cards = new List<CardModels>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT 
    c.Codigo,
    c.Descricao,
    c.NomeCliente,
    c.UsuarioCard,
    c.Status,
    c.Funil,
    ISNULL(c.Novo, 'N') AS Novo,
    c.DataHoraUltimaGravacao,
    ua.DataHora AS DataHoraUltimaMensagem,
    ua.Anotacao AS UltimaMensagem
FROM CRMC c
OUTER APPLY
(
    SELECT TOP 1
        a.DataHora,
        a.Anotacao
    FROM CRMAnotacao a
    WHERE a.CodCRMC = c.Codigo
      AND ISNULL(a.EnvioCliente, 'N') = 'S'    -- << add esta linha
      AND ISNULL(a.MensagemExcluida, 'N') = 'N' -- << e esta
    ORDER BY a.DataHora DESC
) ua
WHERE c.CodEmp = @Empresa
ORDER BY ISNULL(ua.DataHora, c.DataHoraUltimaGravacao) DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Empresa", empId);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                cards.Add(new CardModels
                {
                    Codigo = reader.GetInt32(0),
                    Descricao = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    NomeCliente = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Usuario = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Status = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Funil = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Novo = reader.IsDBNull(6) ? "N" : reader.GetString(6),
                    DataAlteracao = reader.IsDBNull(8)
                        ? GetDateSafe(reader, 7)
                        : GetDateSafe(reader, 8),

                    // usa DataAlteracao como data da última mensagem para notificação
                    DescricaoUltimaMensagem = reader.IsDBNull(9) ? "" : reader.GetString(9)
                });
            }

            return cards;
        }

        public async Task<AnotacaoModel?> GetUltimaMensagemClienteAsync(int codCrm)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
        SELECT TOP 1
            DataHora, Anotacao, Funil, EnvioCliente,
            StatusWhatsApp, CaminhoImagem, NomeImagem,
            Audio, AudioMimeType, Alterado, MensagemExcluida, Usuario
        FROM CRMAnotacao
        WHERE CodCRMC = @CodCRMC
          AND ISNULL(MensagemExcluida,'N') = 'N'
          AND ISNULL(EnvioCliente,'N') = 'S'     -- << só mensagens do cliente
        ORDER BY DataHora DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodCRMC", codCrm);

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new AnotacaoModel
            {
                DataHora = rd.GetDateTime(rd.GetOrdinal("DataHora")),
                Texto = rd["Anotacao"]?.ToString() ?? "",
                Funil = rd["Funil"]?.ToString() ?? "",
                EnvioCliente = rd["EnvioCliente"]?.ToString() ?? "",
                StatusWhatsApp = rd["StatusWhatsApp"]?.ToString() ?? "",
                CaminhoImagem = rd["CaminhoImagem"]?.ToString() ?? "",
                NomeImagem = rd["NomeImagem"]?.ToString() ?? "",
                Audio = rd["Audio"]?.ToString() ?? "",
                AudioMimeType = rd["AudioMimeType"]?.ToString() ?? "",
                Alterado = rd["Alterado"]?.ToString() ?? "N",
                MensagemExcluida = rd["MensagemExcluida"]?.ToString() ?? "N",
                Usuario = rd["Usuario"]?.ToString() ?? "",
            };
        }

        public async Task<Dictionary<int, AnotacaoModel>> GetUltimasMensagensClienteAsync(List<int> codigos)
        {
            var resultado = new Dictionary<int, AnotacaoModel>();

            if (codigos == null || codigos.Count == 0)
                return resultado;

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // Monta lista de parâmetros @C0, @C1, ... para o IN
            var nomesParam = codigos.Select((_, i) => "@C" + i).ToList();

            var sql = $@"
SELECT
    d.CodCRMC,
    d.DataHora,
    d.Anotacao,
    d.Funil,
    d.EnvioCliente,
    d.StatusWhatsApp,
    d.CaminhoImagem,
    d.NomeImagem,
    d.Audio,
    d.AudioMimeType,
    d.Alterado,
    d.MensagemExcluida,
    d.Usuario
FROM CRMAnotacao d
INNER JOIN (
    SELECT CodCRMC, MAX(DataHora) AS MaxData
    FROM CRMAnotacao
    WHERE ISNULL(MensagemExcluida,'N') = 'N'
      AND ISNULL(EnvioCliente,'N') = 'S'
      AND CodCRMC IN ({string.Join(",", nomesParam)})
    GROUP BY CodCRMC
) ult ON ult.CodCRMC = d.CodCRMC AND ult.MaxData = d.DataHora
WHERE ISNULL(d.MensagemExcluida,'N') = 'N'
  AND ISNULL(d.EnvioCliente,'N') = 'S'";

            using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < codigos.Count; i++)
                cmd.Parameters.AddWithValue(nomesParam[i], codigos[i]);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var cod = rd.GetInt32(rd.GetOrdinal("CodCRMC"));

                // Se houver empate de DataHora, mantém o primeiro (já é suficiente para notificação)
                if (resultado.ContainsKey(cod))
                    continue;

                resultado[cod] = new AnotacaoModel
                {
                    DataHora = rd.GetDateTime(rd.GetOrdinal("DataHora")),
                    Texto = rd["Anotacao"]?.ToString() ?? "",
                    Funil = rd["Funil"]?.ToString() ?? "",
                    EnvioCliente = rd["EnvioCliente"]?.ToString() ?? "",
                    StatusWhatsApp = rd["StatusWhatsApp"]?.ToString() ?? "",
                    CaminhoImagem = rd["CaminhoImagem"]?.ToString() ?? "",
                    NomeImagem = rd["NomeImagem"]?.ToString() ?? "",
                    Audio = rd["Audio"]?.ToString() ?? "",
                    AudioMimeType = rd["AudioMimeType"]?.ToString() ?? "",
                    Alterado = rd["Alterado"]?.ToString() ?? "N",
                    MensagemExcluida = rd["MensagemExcluida"]?.ToString() ?? "N",
                    Usuario = rd["Usuario"]?.ToString() ?? ""
                };
            }

            return resultado;
        }


        // ─────────────────────────────────────────────────────────────────────────────
        // Adicionar dentro da classe CardService (CardService.cs)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Cria um ticket de suporte a partir de um card do CRM.
        /// Copia cabeçalho + resumo do histórico como primeira anotação.
        /// Não cria nenhum vínculo: os dois registros vivem de forma independente.
        /// </summary>
        public async Task<int> CriarTicketAPartirDoCardAsync(
    int codCard,
    string usuarioResponsavel,
    int empresa)
        {
            var responsavel = string.IsNullOrWhiteSpace(usuarioResponsavel)
                ? "NOVO"
                : usuarioResponsavel.Trim().ToUpper();

            try
            {
                using var conn = new SqlConnection(_connection);
                await conn.OpenAsync();

                // -- 1. Lê cabeçalho do card -------------------------------------------
                string descricao = "", telefone = "", nomeCliente = "";
                int codCliente = 0;

                using (var cmd = new SqlCommand(@"
    SELECT Descricao, ISNULL(TelefoneWhatsApp, ISNULL(Celular,'')),
           ISNULL(NomeCliente,''), ISNULL(CodCliente, 0)
    FROM CRMC
    WHERE Codigo = @Cod AND CodEmp = @Emp", conn))
                {
                    cmd.Parameters.AddWithValue("@Cod", codCard);
                    cmd.Parameters.AddWithValue("@Emp", empresa);
                    using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                        throw new Exception("Card não encontrado.");
                    descricao = rd.GetString(0);
                    telefone = rd.GetString(1);
                    nomeCliente = rd.GetString(2);
                    codCliente = rd.GetInt32(3);
                }

                // -- 2. Lê histórico de anotações (últimas 20, ordem cronológica) ------
                var linhasHistorico = new List<string>();
                using (var cmd = new SqlCommand(@"
    SELECT TOP 20
        ISNULL(Usuario,'?') AS Usuario,
        ISNULL(Anotacao,'') AS Anotacao,
        DataHora,
        ISNULL(EnvioCliente,'N') AS EnvioCliente,
        ISNULL(MensagemExcluida,'N') AS Excluida
    FROM CRMAnotacao
    WHERE CodCRMC = @Cod
      AND ISNULL(MensagemExcluida,'N') = 'N'
      AND ISNULL(Anotacao,'') <> ''
      AND Anotacao NOT LIKE 'WhatsApp visualizado pelo usuário%'
    ORDER BY DataHora ASC", conn))
                {
                    cmd.Parameters.AddWithValue("@Cod", codCard);
                    using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                    {
                        var origem = rd.GetString(3) == "S" ? "Cliente" : rd.GetString(0);
                        var texto = rd.GetString(1);
                        var data = rd.GetDateTime(2).ToString("dd/MM/yyyy HH:mm");
                        if (texto.Length > 300) texto = texto[..300] + "...";
                        linhasHistorico.Add($"[{data}] {origem}: {texto}");
                    }
                }

                var resumo = linhasHistorico.Count > 0
                    ? "📋 Histórico do CRM:\n" + string.Join("\n", linhasHistorico)
                    : "📋 Card sem histórico de mensagens.";

                var assunto = $"[CRM] {descricao}";
                if (assunto.Length > 200) assunto = assunto[..200];

                // -- 3. Insere TicketChamadoC ------------------------------------------
                int codTicket;
                using (var cmd = new SqlCommand(@"
    INSERT INTO TicketChamadoC
    (
        Status, CodSetor, CodCategoria,
        DataAbertura, DataHoraAbertura,
        Usuario, UsuarioAbertura, UsuarioUltimaGravacao, DataHoraUltimaGravacao,
        CodCliente, Assunto, CodSituacao, Novo, CodTipo,
        TelefoneWhatsApp
    )
    OUTPUT INSERTED.Codigo
    VALUES
    (
        1, 1, NULL,
        CAST(GETDATE() AS DATE), GETDATE(),
        @Usuario, @UsuarioAbertura, @Usuario, GETDATE(),
        @CodCliente, @Assunto, 1, 'S', 1,
        @Telefone
    )", conn))
                {
                    cmd.Parameters.AddWithValue("@Usuario", responsavel);
                    cmd.Parameters.AddWithValue("@UsuarioAbertura",
                        string.IsNullOrWhiteSpace(nomeCliente) ? descricao : nomeCliente);
                    cmd.Parameters.Add("@CodCliente", System.Data.SqlDbType.Int).Value =
                        codCliente > 0 ? codCliente : System.DBNull.Value;
                    cmd.Parameters.AddWithValue("@Assunto", assunto);
                    cmd.Parameters.Add("@Telefone", System.Data.SqlDbType.VarChar, 30).Value =
                        string.IsNullOrWhiteSpace(telefone) ? System.DBNull.Value : (object)telefone;

                    codTicket = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                // -- 4. Insere anotação inicial com resumo do CRM ----------------------
                using (var cmd = new SqlCommand(@"
    INSERT INTO TicketChamadoD
    (CodTicketChamadoC, Anotacao, DataHora, Usuario, LidoSuporte, EnvioCliente,
     TelefoneWhatsApp, CodInstancia, StatusWhatsApp, WhatsAppEnviado)
    VALUES (@Cod, @Texto, GETDATE(), @Usuario, 'S', 'N',
     NULL, NULL, 'interno', 'S')", conn))
                {
                    cmd.Parameters.AddWithValue("@Cod", codTicket);
                    cmd.Parameters.AddWithValue("@Texto", resumo);
                    cmd.Parameters.AddWithValue("@Usuario", responsavel);
                    await cmd.ExecuteNonQueryAsync();
                }

                _monitor.Log(LogCategory.CRM, LogSeverity.Success,
                    $"Ticket #{codTicket} criado a partir do card #{codCard}",
                    $"responsavel={responsavel}");

                return codTicket;
            }
            catch (Exception ex)
            {
                _monitor.LogException(LogCategory.CRM, ex,
                    $"CriarTicketAPartirDoCardAsync card #{codCard} responsavel={responsavel}");
                throw; // repropaga para o Card.razor tratar e exibir a mensagem de erro
            }
        }
    }
}