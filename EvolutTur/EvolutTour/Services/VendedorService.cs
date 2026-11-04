using EvolutTur.Components.Pages;
using EvolutTur.Data;
using Microsoft.Data.SqlClient;

namespace EvolutTur.Services
{
    public class VendedorService
    {
        private readonly SqlConnection _connection;

        private readonly Context _context;

        private readonly string _connectionString;

        public VendedorService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<List<Models.Vendedor>> BuscarVendedor(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Nome like '%{filtroBusca}%'";

            var query = $"SELECT top 50 Codigo, Nome, isnull(Comissao,0) Comissao FROM Vendedor where CodEmp = @CodEmp {criterioBusca}";
            List<Models.Vendedor> set = new List<Models.Vendedor>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Models.Vendedor cli = new Models.Vendedor();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Nome = reader["Nome"].ToString();
                        cli.Comissao = decimal.Parse(reader["Comissao"].ToString());

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task AdicionarVendedor(Models.Vendedor vendedor, int CodEmp)
        {
            var query = "INSERT INTO Vendedor (Nome, Comissao, CodEmp) VALUES (@Nome, @Comissao, @CodEmp)";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Nome", vendedor.Nome);
                command.Parameters.AddWithValue("@Comissao", vendedor.Comissao);
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task AtualizarVendedor(Models.Vendedor vendedor, int CodEmp)
        {
            var query = "UPDATE Vendedor set Nome = @Nome, Comissao = @Comissao WHERE Codigo = @Codigo and CodEmp = @CodEmp";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", vendedor.Codigo);
                command.Parameters.AddWithValue("@Nome", vendedor.Nome);
                command.Parameters.AddWithValue("@Comissao", vendedor.Comissao);           
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task ExcluirVendedor(int CodCliente, int CodEmp)
        {
            var query = "Delete from Vendedor where Codigo = @Codigo and CodEmp = @CodEmp";
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