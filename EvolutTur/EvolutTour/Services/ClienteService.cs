using EvolutTur.Data;
using EvolutTur.Models;
using Microsoft.Data.SqlClient;

namespace EvolutTur.Services
{
    public class ClienteService
    {
        private readonly SqlConnection _connection;

        private readonly Context _context;

        private readonly string _connectionString;

        public ClienteService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<List<Cliente>> BuscaClienteAsync(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Nome like '%{filtroBusca}%'";

            var query = $"SELECT top 50 Codigo, Nome, Endereco, Bairro, Cidade, Numero, CPF, Telefone, Email FROM Cliente where CodEmp = @CodEmp {criterioBusca}";
            List<Cliente> set = new List<Cliente>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Cliente cli = new Cliente();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Nome = reader["Nome"].ToString();
                        cli.Endereco = reader["Endereco"].ToString();
                        cli.Cidade = reader["Cidade"].ToString();
                        cli.Bairro = reader["Bairro"].ToString();
                        cli.Numero = reader["Numero"].ToString();
                        cli.CPF = reader["CPF"].ToString();
                        cli.Telefone = reader["Telefone"].ToString();
                        cli.Email = reader["Email"].ToString();
                        
                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task AdicionarCliente(Models.Cliente cliente, int CodEmp)
        {
            try
            {
                var query = "INSERT INTO Cliente (Nome, Endereco, Bairro, Cidade, Numero, CPF, Telefone, Email, CodEmp) VALUES (@Nome, @Endereco, @Bairro, @Cidade, @Numero, @CPF, @Telefone, @Email, @CodEmp)";
                using (SqlCommand command = new SqlCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@Nome", cliente.Nome ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Endereco", cliente.Endereco ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Bairro", cliente.Bairro ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Cidade", cliente.Cidade ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Numero", cliente.Numero ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CPF", cliente.CPF ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Telefone", cliente.Telefone ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Email", cliente.Email ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CodEmp", CodEmp);


                    _connection.Open();
                    await command.ExecuteNonQueryAsync();
                    _connection.Close();
                }
            }
            catch(Exception ee)
            {
                var a = ee.Message;
            }
        }

        public async Task AtualizarCliente(Cliente cliente, int CodEmp)
        {
            var query = "UPDATE Cliente set Nome = @Nome, Endereco = @Endereco, Bairro = @Bairro, Cidade = @Cidade, Numero = @Numero, CPF = @CPF, Telefone = @Telefone, Email = @Email WHERE Codigo = @Codigo and CodEmp = @CodEmp";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", cliente.Codigo);
                command.Parameters.AddWithValue("@Nome", cliente.Nome);
                command.Parameters.AddWithValue("@Endereco", cliente.Endereco);
                command.Parameters.AddWithValue("@Bairro", cliente.Bairro);
                command.Parameters.AddWithValue("@Cidade", cliente.Cidade);
                command.Parameters.AddWithValue("@Numero", cliente.Numero);
                command.Parameters.AddWithValue("@CPF", cliente.CPF);
                command.Parameters.AddWithValue("@Telefone", cliente.Telefone);
                command.Parameters.AddWithValue("@Email", cliente.Email);
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task ExcluirCliente(int CodCliente, int CodEmp)
        {
            var query = "Delete from Cliente where Codigo = @Codigo and CodEmp = @CodEmp";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", CodCliente);               
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }
    }
}
