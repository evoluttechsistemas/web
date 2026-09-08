using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    // =========================================================
    // CLIENTE SERVICE
    // Cliente vem do ERP (ClienteAFV)
    // Endereços extras ficam em ClienteEnderecoDelivery
    // =========================================================
    public class CheckoutService
    {
        private readonly string _connectionString;

        public CheckoutService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException(nameof(config), "ConnectionString 'ConnectionComercial' não configurada.");
        }

        public async Task<int> CriarClienteDeliveryAsync(
    string nome,
    string telefone,
    string email,
    string cpfCnpj,
    Endereco endereco)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tran = conn.BeginTransaction();

            try
            {
                var sqlCodigo = @"
SELECT ISNULL(MAX(Codigo), 0) + 1
FROM ClienteAFV";

                int novoCodigo;

                using (var cmdCodigo = new SqlCommand(sqlCodigo, conn, tran))
                {
                    novoCodigo = Convert.ToInt32(await cmdCodigo.ExecuteScalarAsync());
                }

                var sqlInsert = @"
INSERT INTO ClienteAFV
(
    Codigo,
    Nome,
    Apelido,
    Telefone,
    Email,
    CPF,
    CNPJ,
    Endereco,
    Bairro,
    NomeCidade
)
VALUES
(
    @Codigo,
    @Nome,
    @Apelido,
    @Telefone,
    @Email,
    @CPF,
    @CNPJ,
    @Endereco,
    @Bairro,
    @NomeCidade
)";

                using var cmd = new SqlCommand(sqlInsert, conn, tran);

                cmd.Parameters.AddWithValue("@Codigo", novoCodigo);
                cmd.Parameters.AddWithValue("@Nome", nome ?? "");
                cmd.Parameters.AddWithValue("@Apelido", nome ?? "");
                cmd.Parameters.AddWithValue("@Telefone", telefone ?? "");
                cmd.Parameters.AddWithValue("@Email", email ?? "");
                cmd.Parameters.AddWithValue("@CPF", cpfCnpj ?? "");
                cmd.Parameters.AddWithValue("@CNPJ", cpfCnpj ?? "");
                cmd.Parameters.AddWithValue("@Endereco", endereco?.EnderecoCompleto ?? "");
                cmd.Parameters.AddWithValue("@Bairro", endereco?.Bairro ?? "");
                cmd.Parameters.AddWithValue("@NomeCidade", endereco?.Cidade ?? "");

                await cmd.ExecuteNonQueryAsync();

                tran.Commit();
                return novoCodigo;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task<Cliente?> BuscarPorTelefoneAsync(string telefone, int codEmp)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
        SELECT TOP 1 *
        FROM ClienteAFV
        WHERE REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Telefone, ''), '(', ''), ')', ''), '-', ''), ' ', '') =
              REPLACE(REPLACE(REPLACE(REPLACE(@Telefone, '(', ''), ')', ''), '-', ''), ' ', '')";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Telefone", telefone ?? "");

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapearCliente(reader);
            }

            return null;
        }

        public async Task<Cliente?> BuscarPorCodigoAsync(int codigo, int codEmp)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
        SELECT TOP 1 *
        FROM ClienteAFV
        WHERE Codigo = @Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapearCliente(reader);
            }

            return null;
        }

        public async Task<List<Cliente>> BuscarClientesAsync(string busca, int codEmp)
        {
            var clientes = new List<Cliente>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
        SELECT TOP 20 *
        FROM ClienteAFV
        WHERE (
                ISNULL(Nome, '') LIKE @Busca
                OR ISNULL(Apelido, '') LIKE @Busca
                OR ISNULL(Telefone, '') LIKE @Busca
                OR ISNULL(CPF, '') LIKE @Busca
              )
        ORDER BY Nome";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Busca", $"%{busca ?? ""}%");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                clientes.Add(MapearCliente(reader));
            }

            return clientes;
        }

        public async Task<List<Endereco>> BuscarEnderecosAsync(int codCliente, int codEmp)
        {
            var enderecos = new List<Endereco>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Endereço principal do ERP
            var sqlCliente = @"
    SELECT TOP 1 *
    FROM ClienteAFV
    WHERE Codigo = @CodCliente";

            using (var cmd = new SqlCommand(sqlCliente, conn))
            {
                cmd.Parameters.AddWithValue("@CodCliente", codCliente);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var enderecoPrincipal = new Endereco
                    {
                        Codigo = 0,
                        CodCliente = codCliente,
                        Descricao = "Principal",
                        Rua = GetString(reader, "Endereco"),
                        Numero = "",
                        Complemento = "",
                        Bairro = GetString(reader, "Bairro"),
                        Cidade = GetString(reader, "NomeCidade"),
                        Estado = "",
                        CEP = "",
                        PontoReferencia = null,
                        EnderecoPrincipal = true
                    };

                    // Só adiciona se tiver ao menos rua ou bairro/cidade
                    if (!string.IsNullOrWhiteSpace(enderecoPrincipal.Rua) ||
                        !string.IsNullOrWhiteSpace(enderecoPrincipal.Bairro) ||
                        !string.IsNullOrWhiteSpace(enderecoPrincipal.Cidade))
                    {
                        enderecos.Add(enderecoPrincipal);
                    }
                }
            }

            // Endereços extras do delivery
            var sqlExtras = @"
                SELECT *
                FROM ClienteEnderecoDelivery
                WHERE CodCliente = @CodCliente
                  AND CodEmp = @CodEmp
                  AND Ativo = 1
                ORDER BY EnderecoPrincipal DESC, Codigo";

            using (var cmd = new SqlCommand(sqlExtras, conn))
            {
                cmd.Parameters.AddWithValue("@CodCliente", codCliente);
                cmd.Parameters.AddWithValue("@CodEmp", codEmp);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enderecos.Add(new Endereco
                    {
                        Codigo = GetInt(reader, "Codigo"),
                        CodCliente = GetInt(reader, "CodCliente"),
                        Descricao = GetString(reader, "Descricao"),
                        Rua = GetString(reader, "Rua"),
                        Numero = GetString(reader, "Numero"),
                        Complemento = GetString(reader, "Complemento"),
                        Bairro = GetString(reader, "Bairro"),
                        Cidade = GetString(reader, "Cidade"),
                        Estado = GetString(reader, "Estado"),
                        CEP = GetString(reader, "CEP"),
                        PontoReferencia = GetNullableString(reader, "PontoReferencia"),
                        EnderecoPrincipal = GetBool(reader, "EnderecoPrincipal")
                    });
                }
            }


            return enderecos;
        }

        public async Task<Endereco?> BuscarEnderecoPrincipalAsync(int codCliente, int codEmp)
        {
            var enderecos = await BuscarEnderecosAsync(codCliente, codEmp);
            return enderecos.FirstOrDefault(x => x.EnderecoPrincipal) ?? enderecos.FirstOrDefault();
        }

        public async Task<int> SalvarEnderecoAsync(int codCliente, int codEmp, Endereco endereco)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            if (endereco.EnderecoPrincipal)
            {
                var sqlDesmarcar = @"
                    UPDATE ClienteEnderecoDelivery
                    SET EnderecoPrincipal = 0
                    WHERE CodCliente = @CodCliente
                      AND CodEmp = @CodEmp";

                using var cmdDesmarcar = new SqlCommand(sqlDesmarcar, conn);
                cmdDesmarcar.Parameters.AddWithValue("@CodCliente", codCliente);
                cmdDesmarcar.Parameters.AddWithValue("@CodEmp", codEmp);
                await cmdDesmarcar.ExecuteNonQueryAsync();
            }

            var sql = @"
                INSERT INTO ClienteEnderecoDelivery
                (
                    CodCliente,
                    CodEmp,
                    Descricao,
                    Rua,
                    Numero,
                    Complemento,
                    Bairro,
                    Cidade,
                    Estado,
                    CEP,
                    PontoReferencia,
                    EnderecoPrincipal,
                    Ativo,
                    Usuario,
                    DataCadastro
                )
                VALUES
                (
                    @CodCliente,
                    @CodEmp,
                    @Descricao,
                    @Rua,
                    @Numero,
                    @Complemento,
                    @Bairro,
                    @Cidade,
                    @Estado,
                    @CEP,
                    @PontoReferencia,
                    @EnderecoPrincipal,
                    1,
                    'SITE',
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Descricao", string.IsNullOrWhiteSpace(endereco.Descricao) ? "Endereço" : endereco.Descricao);
            cmd.Parameters.AddWithValue("@Rua", endereco.Rua ?? "");
            cmd.Parameters.AddWithValue("@Numero", endereco.Numero ?? "");
            cmd.Parameters.AddWithValue("@Complemento", string.IsNullOrWhiteSpace(endereco.Complemento) ? DBNull.Value : endereco.Complemento);
            cmd.Parameters.AddWithValue("@Bairro", endereco.Bairro ?? "");
            cmd.Parameters.AddWithValue("@Cidade", endereco.Cidade ?? "");
            cmd.Parameters.AddWithValue("@Estado", string.IsNullOrWhiteSpace(endereco.Estado) ? DBNull.Value : endereco.Estado);
            cmd.Parameters.AddWithValue("@CEP", string.IsNullOrWhiteSpace(endereco.CEP) ? DBNull.Value : endereco.CEP);
            cmd.Parameters.AddWithValue("@PontoReferencia", string.IsNullOrWhiteSpace(endereco.PontoReferencia) ? DBNull.Value : endereco.PontoReferencia);
            cmd.Parameters.AddWithValue("@EnderecoPrincipal", endereco.EnderecoPrincipal);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private Cliente MapearCliente(SqlDataReader reader)
        {
            var cliente = new Cliente
            {
                Codigo = GetInt(reader, "Codigo"),
                CodEmp = 0,
                Nome = GetString(reader, "Nome"),
                Apelido = GetString(reader, "Apelido"),
                Telefone = GetString(reader, "Telefone"),
                Email = GetString(reader, "Email"),
                CPF = GetString(reader, "CPF"),

                Rua = GetString(reader, "Endereco"),
                Numero = "",
                Complemento = "",
                Bairro = GetString(reader, "Bairro"),
                Cidade = GetString(reader, "NomeCidade"),
                Estado = "",
                CEP = "",
                PontoReferencia = null
            };

            if (!string.IsNullOrWhiteSpace(cliente.Rua) ||
                !string.IsNullOrWhiteSpace(cliente.Bairro) ||
                !string.IsNullOrWhiteSpace(cliente.Cidade))
            {
                cliente.Enderecos.Add(new Endereco
                {
                    Codigo = 0,
                    CodCliente = cliente.Codigo,
                    Descricao = "Principal",
                    Rua = cliente.Rua,
                    Numero = cliente.Numero,
                    Complemento = cliente.Complemento,
                    Bairro = cliente.Bairro,
                    Cidade = cliente.Cidade,
                    Estado = cliente.Estado,
                    CEP = cliente.CEP,
                    EnderecoPrincipal = true
                });
            }

            return cliente;
        }

        private static string GetString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static string? GetNullableString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static int GetInt(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static bool GetBool(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal));
        }
    }

    public class FormaPagamentoService
    {
        private readonly string _connectionString;

        public FormaPagamentoService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException(nameof(config), "ConnectionString 'ConnectionComercial' não configurada.");
        }

        public async Task<List<string>> ListarDescricoesAtivasAsync(int codEmp)
        {
            var formas = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT DISTINCT Descricao
FROM FormaPagamentoAFV
WHERE ISNULL(Descricao, '') <> ''
ORDER BY Descricao";

            using var cmd = new SqlCommand(sql, conn);

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