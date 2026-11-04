using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class FormaPagamentoService
    {
        private readonly string _connectionString;

        public FormaPagamentoService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException(nameof(config), "ConnectionString 'ConnectionComercial' não configurada.");
        }

        public async Task<List<string>> ListarDescricoesAtivasAsync(int codEmp, string cnpj)
        {
            var formas = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT DISTINCT Descricao
FROM FormaPagamentoAFV
WHERE ISNULL(Descricao, '') <> ''
  AND CNPJ = @CNPJ
ORDER BY Descricao";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CNPJ", cnpj);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var descricao = GetString(reader, "Descricao");
                if (!string.IsNullOrWhiteSpace(descricao))
                    formas.Add(descricao);
            }

            return formas;
        }

        public async Task<bool> ExisteFormaPagamentoAsync(int codEmp, string descricao)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
            SELECT COUNT(1)
            FROM FormaPagamentoAFV
            WHERE Descricao = @Descricao";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Descricao", descricao ?? "");

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        public async Task<List<FormaPagamentoItem>> ListarFormasAsync()
        {
            var formas = new List<FormaPagamentoItem>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = @"
            SELECT 
                Codigo,
                CodFormaPagamentoAFV,
                Descricao,
                Parcelas,
                CNPJ,
                CodCliente,
                TipoPagamento,
                Dias
            FROM FormaPagamentoAFV
            WHERE ISNULL(Descricao, '') <> ''
            ORDER BY Descricao";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                formas.Add(new FormaPagamentoItem
                {
                    Codigo = GetInt(reader, "Codigo"),
                    CodFormaPagamentoAFV = GetIntOrNull(reader, "CodFormaPagamentoAFV"),
                    Descricao = GetString(reader, "Descricao"),
                    Parcelas = GetIntOrNull(reader, "Parcelas"),
                    CNPJ = GetNullableString(reader, "CNPJ"),
                    CodCliente = GetIntOrNull(reader, "CodCliente"),
                    TipoPagamento = GetNullableString(reader, "TipoPagamento"),
                    Dias = GetIntOrNull(reader, "Dias")
                });
            }
            return formas;
        }
        private static string GetString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
        }

        private static string? GetNullableString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
        }

        private static int GetInt(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static int? GetIntOrNull(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
        }
    }



    public class FormaPagamentoItem
    {
        public int Codigo { get; set; }
        public int? CodFormaPagamentoAFV { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public int? Parcelas { get; set; }
        public string? CNPJ { get; set; }
        public int? CodCliente { get; set; }
        public string? TipoPagamento { get; set; }
        public int? Dias { get; set; }
    }
}