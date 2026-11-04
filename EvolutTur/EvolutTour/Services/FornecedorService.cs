using EvolutTur.Data;
using EvolutTur.Models;
using Microsoft.Data.SqlClient;

namespace EvolutTur.Services
{
    public class FornecedorService
    {
        private readonly SqlConnection _connection;

        private readonly Context _context;

        private readonly string _connectionString;

        public FornecedorService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<List<Fornecedor>> BuscaFornecedorAsync(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Razao like '%{filtroBusca}%'";

            var query = $"SELECT top 50 Codigo, Razao, Fantasia, TipoPessoa, CPF, RG, CEP, UF, Endereco, Numero, Bairro, Cidade, Complemento, Telefone, Email, isnull(Comissao,0) Comissao, Observacao FROM Fornecedor where CodEmp = @CodEmp {criterioBusca}";
            List<Fornecedor> set = new List<Fornecedor>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Fornecedor cli = new Fornecedor();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Razao = reader["Razao"].ToString();
                        cli.Fantasia = reader["Fantasia"].ToString();
                        cli.TipoPessoa = reader["TipoPessoa"].ToString();
                        cli.CPF = reader["CPF"].ToString();
                        cli.RG = reader["RG"].ToString();
                        cli.CEP = reader["CEP"].ToString();
                        cli.UF = reader["UF"].ToString();
                        cli.Endereco = reader["Endereco"].ToString();
                        cli.Numero = reader["Numero"].ToString();
                        cli.Bairro = reader["Bairro"].ToString();
                        cli.Cidade = reader["Cidade"].ToString();
                        cli.Complemento = reader["Complemento"].ToString();
                        cli.Telefone = reader["Telefone"].ToString();
                        cli.Email = reader["Email"].ToString();
                        cli.Comissao = decimal.Parse(reader["Comissao"].ToString());
                        cli.Observacao = reader["Observacao"].ToString();

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task AdicionarFornecedor(Models.Fornecedor fornecedor, int CodEmp)
        {
            var query = "INSERT INTO Fornecedor (Razao, Fantasia, TipoPessoa, CPF, RG, CEP, UF, Endereco, Numero, Bairro, Cidade, Complemento, Telefone, Email, Comissao, Observacao, CodEmp) VALUES (@Razao, @Fantasia, @TipoPessoa, @CPF, @RG, @CEP, @UF, @Endereco, @Numero, @Bairro, @Cidade, @Complemento, @Telefone, @Email, @Comissao, @Observacao, @CodEmp)";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Razao", fornecedor.Razao ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Fantasia", fornecedor.Fantasia ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@TipoPessoa", fornecedor.TipoPessoa ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CPF", fornecedor.CPF ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@RG", fornecedor.RG ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CEP", fornecedor.CEP ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@UF", fornecedor.UF ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Endereco", fornecedor.Endereco ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Numero", fornecedor.Numero ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Bairro", fornecedor.Bairro ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Cidade", fornecedor.Cidade ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Complemento", fornecedor.Complemento ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Telefone", fornecedor.Telefone ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Email", fornecedor.Email ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Comissao", fornecedor.Comissao ?? 0);
                command.Parameters.AddWithValue("@Observacao", fornecedor.Observacao ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task AtualizarFornecedor(Fornecedor fornecedor, int CodEmp)
        {
            var query = @"UPDATE Fornecedor 
                  SET Razao = @Razao, 
                      Fantasia = @Fantasia, 
                      TipoPessoa = @TipoPessoa, 
                      CPF = @CPF, 
                      RG = @RG, 
                      CEP = @CEP, 
                      UF = @UF, 
                      Endereco = @Endereco, 
                      Numero = @Numero, 
                      Bairro = @Bairro, 
                      Cidade = @Cidade, 
                      Complemento = @Complemento, 
                      Telefone = @Telefone, 
                      Email = @Email, 
                      Comissao = @Comissao, 
                      Observacao = @Observacao 
                  WHERE Codigo = @Codigo AND CodEmp = @CodEmp";

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", fornecedor.Codigo);
                command.Parameters.AddWithValue("@Razao", fornecedor.Razao ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Fantasia", fornecedor.Fantasia ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@TipoPessoa", fornecedor.TipoPessoa ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CPF", fornecedor.CPF ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@RG", fornecedor.RG ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CEP", fornecedor.CEP ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@UF", fornecedor.UF ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Endereco", fornecedor.Endereco ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Numero", fornecedor.Numero ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Bairro", fornecedor.Bairro ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Cidade", fornecedor.Cidade ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Complemento", fornecedor.Complemento ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Telefone", fornecedor.Telefone ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Email", fornecedor.Email ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Comissao", fornecedor.Comissao ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Observacao", fornecedor.Observacao ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task ExcluirFornecedor(int CodFornecedor, int CodEmp)
        {
            var query = "Delete from Fornecedor where Codigo = @Codigo and CodEmp = @CodEmp";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", CodFornecedor);
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }
    }
}
