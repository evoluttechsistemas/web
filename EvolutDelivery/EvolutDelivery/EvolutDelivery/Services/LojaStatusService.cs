using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    /// <summary>
    /// Status da loja no Delivery: aberta ou fechada pelo próprio cliente (painel admin).
    /// Fica na tabela EmpresaDeliveryStatus (script Database/StatusLoja.sql).
    /// Se a tabela ainda não existir, a loja é tratada como ABERTA (nada quebra).
    /// </summary>
    public class LojaStatusService
    {
        public const int MaxMensagem = 200;

        public record StatusLoja(bool Fechada, string Mensagem, bool TabelaExiste, DateTime? AlteradoEm, string AlteradoPor)
        {
            public static StatusLoja Aberta(bool tabelaExiste = true) => new(false, "", tabelaExiste, null, "");
        }

        private readonly string _connectionString;
        private readonly ILogger<LojaStatusService> _logger;

        public LojaStatusService(IConfiguration config, ILogger<LojaStatusService> logger)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new InvalidOperationException("Connection string 'ConnectionComercial' não encontrada.");
            _logger = logger;
        }

        public async Task<StatusLoja> ObterAsync(int codEmp)
        {
            if (codEmp <= 0)
                return StatusLoja.Aberta();

            const string sql = @"
SELECT TOP 1 LojaFechada, MensagemFechada, AlteradoEm, AlteradoPor
FROM EmpresaDeliveryStatus
WHERE CodEmp = @CodEmp";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@CodEmp", codEmp);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return StatusLoja.Aberta(); // nunca foi alterada = aberta

                return new StatusLoja(
                    reader["LojaFechada"] != DBNull.Value && Convert.ToBoolean(reader["LojaFechada"]),
                    reader["MensagemFechada"]?.ToString() ?? "",
                    true,
                    reader["AlteradoEm"] == DBNull.Value ? null : Convert.ToDateTime(reader["AlteradoEm"]),
                    reader["AlteradoPor"]?.ToString() ?? "");
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // 208 = tabela não existe (script ainda não rodado): loja aberta
                _logger.LogWarning("Tabela EmpresaDeliveryStatus não existe. Rode Database/StatusLoja.sql.");
                return StatusLoja.Aberta(tabelaExiste: false);
            }
        }

        public async Task DefinirAsync(int codEmp, bool fechada, string? mensagem, string? usuario)
        {
            if (codEmp <= 0)
                throw new ArgumentException("Empresa inválida.", nameof(codEmp));

            mensagem = (mensagem ?? "").Trim();
            if (mensagem.Length > MaxMensagem)
                mensagem = mensagem[..MaxMensagem];

            usuario = (usuario ?? "").Trim();
            if (usuario.Length > 100)
                usuario = usuario[..100];

            const string sql = @"
UPDATE EmpresaDeliveryStatus
SET LojaFechada = @Fechada,
    MensagemFechada = @Mensagem,
    AlteradoEm = GETDATE(),
    AlteradoPor = @Usuario
WHERE CodEmp = @CodEmp;

IF @@ROWCOUNT = 0
    INSERT INTO EmpresaDeliveryStatus (CodEmp, LojaFechada, MensagemFechada, AlteradoEm, AlteradoPor)
    VALUES (@CodEmp, @Fechada, @Mensagem, GETDATE(), @Usuario);";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Fechada", fechada);
            cmd.Parameters.AddWithValue("@Mensagem", string.IsNullOrEmpty(mensagem) ? DBNull.Value : mensagem);
            cmd.Parameters.AddWithValue("@Usuario", string.IsNullOrEmpty(usuario) ? DBNull.Value : usuario);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Loja {CodEmp} {Acao} por {Usuario}", codEmp, fechada ? "FECHADA" : "ABERTA", usuario);
        }
    }
}
