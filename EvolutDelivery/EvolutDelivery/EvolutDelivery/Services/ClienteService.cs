using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class ClienteService
    {
        private readonly string _connectionString;

        public ClienteService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException(nameof(config), "ConnectionString não configurada.");
        }

        // =========================================================
        // BUSCAR POR TELEFONE
        // =========================================================
        public async Task<Cliente?> BuscarPorTelefoneAsync(string telefone, int codEmp, string cnpjEmpresa)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 1 *
FROM ClienteAFV
WHERE REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Telefone, ''), '(', ''), ')', ''), '-', ''), ' ', '') =
      REPLACE(REPLACE(REPLACE(REPLACE(@Telefone, '(', ''), ')', ''), '-', ''), ' ', '') and CNPJ = @CNPJ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Telefone", telefone ?? "");
            cmd.Parameters.AddWithValue("@CNPJ", cnpjEmpresa ?? "");

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapearCliente(reader);

            return null;
        }

        // =========================================================
        // BUSCAR POR CÓDIGO
        // =========================================================
        public async Task<Cliente?> BuscarPorCodigoAsync(int codigo, int codEmp, string cnpjEmpresa)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"SELECT TOP 1 * FROM ClienteAFV WHERE Codigo = @Codigo and CNPJ = @CNPJ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CNPJ", cnpjEmpresa);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapearCliente(reader);

            return null;
        }

        // =========================================================
        // CRIAR CLIENTE (PRIMEIRA COMPRA)
        // =========================================================
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
                // GERA NOVO CÓDIGO
                var sqlCodigo = @"SELECT ISNULL(MAX(Codigo), 0) + 1 FROM ClienteAFV";

                int novoCodigo;
                using (var cmdCodigo = new SqlCommand(sqlCodigo, conn, tran))
                {
                    novoCodigo = Convert.ToInt32(await cmdCodigo.ExecuteScalarAsync());
                }

                // INSERT
                var sqlInsert = @"
INSERT INTO ClienteAFV
(
    Codigo,
    Nome,
    Apelido,
    Telefone,
    Email,
    Endereco,
    Bairro,
    NomeCidade,
    CPF,
    CNPJ
)
VALUES
(
    @Codigo,
    @Nome,
    @Apelido,
    @Telefone,
    @Email,
    @Endereco,
    @Bairro,
    @Cidade,
    @CPF,
    @CNPJ
)";

                using var cmd = new SqlCommand(sqlInsert, conn, tran);

                cmd.Parameters.AddWithValue("@Codigo", novoCodigo);
                cmd.Parameters.AddWithValue("@Nome", nome ?? "");
                cmd.Parameters.AddWithValue("@Apelido", nome ?? "");
                cmd.Parameters.AddWithValue("@Telefone", telefone ?? "");
                cmd.Parameters.AddWithValue("@Email", email ?? "");
                cmd.Parameters.AddWithValue("@Endereco", endereco?.EnderecoCompleto ?? "");
                cmd.Parameters.AddWithValue("@Bairro", endereco?.Bairro ?? "");
                cmd.Parameters.AddWithValue("@Cidade", endereco?.Cidade ?? "");
                cmd.Parameters.AddWithValue("@CPF", cpfCnpj ?? "");
                cmd.Parameters.AddWithValue("@CNPJ", cpfCnpj ?? "");

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

        // =========================================================
        // BUSCAR ENDEREÇOS
        // =========================================================
        public async Task<List<Endereco>> BuscarEnderecosAsync(int codCliente, int codEmp)
        {
            var enderecos = new List<Endereco>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // ENDEREÇO PRINCIPAL (ERP)
            var sqlCliente = @"SELECT TOP 1 * FROM ClienteAFV WHERE Codigo = @CodCliente";

            using (var cmd = new SqlCommand(sqlCliente, conn))
            {
                cmd.Parameters.AddWithValue("@CodCliente", codCliente);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    enderecos.Add(new Endereco
                    {
                        Codigo = 0,
                        CodCliente = codCliente,
                        Descricao = "Principal",
                        Rua = GetString(reader, "Endereco"),
                        Bairro = GetString(reader, "Bairro"),
                        Cidade = GetString(reader, "NomeCidade"),
                        EnderecoPrincipal = true
                    });
                }
            }

            // ENDEREÇOS DO DELIVERY
            var sqlExtras = @"
SELECT *
FROM ClienteEnderecoDelivery
WHERE CodCliente = @CodCliente
AND CodEmp = @CodEmp
AND Ativo = 1";

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
                        CodCliente = codCliente,
                        Descricao = GetString(reader, "Descricao"),
                        Rua = GetString(reader, "Rua"),
                        Numero = GetString(reader, "Numero"),
                        Bairro = GetString(reader, "Bairro"),
                        Cidade = GetString(reader, "Cidade"),
                        CEP = GetString(reader, "CEP"),
                        EnderecoPrincipal = GetBool(reader, "EnderecoPrincipal")
                    });
                }
            }

            return enderecos;
        }

        // =========================================================
        // SALVAR ENDEREÇO
        // =========================================================
        public async Task<int> SalvarEnderecoAsync(int codCliente, int codEmp, Endereco endereco)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
INSERT INTO ClienteEnderecoDelivery
(
    CodCliente,
    CodEmp,
    Descricao,
    Rua,
    Numero,
    Bairro,
    Cidade,
    CEP,
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
    @Bairro,
    @Cidade,
    @CEP,
    0,
    1,
    'SITE',
    GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Descricao", endereco.Descricao ?? "Endereço");
            cmd.Parameters.AddWithValue("@Rua", endereco.Rua ?? "");
            cmd.Parameters.AddWithValue("@Numero", endereco.Numero ?? "");
            cmd.Parameters.AddWithValue("@Bairro", endereco.Bairro ?? "");
            cmd.Parameters.AddWithValue("@Cidade", endereco.Cidade ?? "");
            cmd.Parameters.AddWithValue("@CEP", endereco.CEP ?? "");

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // =========================================================
        // MAPEAMENTO
        // =========================================================
        private Cliente MapearCliente(SqlDataReader reader)
        {
            return new Cliente
            {
                Codigo = GetInt(reader, "Codigo"),
                Nome = GetString(reader, "Nome"),
                Apelido = GetString(reader, "Apelido"),
                Telefone = GetString(reader, "Telefone"),
                Email = GetString(reader, "Email"),
                CPF = GetString(reader, "CPF"),
                Rua = GetString(reader, "Endereco"),
                Bairro = GetString(reader, "Bairro"),
                Cidade = GetString(reader, "NomeCidade")
            };
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private static string GetString(SqlDataReader reader, string col)
        {
            var ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? "" : reader.GetString(ord);
        }

        private static int GetInt(SqlDataReader reader, string col)
        {
            var ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? 0 : Convert.ToInt32(reader.GetValue(ord));
        }

        private static bool GetBool(SqlDataReader reader, string col)
        {
            var ord = reader.GetOrdinal(col);
            return !reader.IsDBNull(ord) && Convert.ToBoolean(reader.GetValue(ord));
        }
    }
}