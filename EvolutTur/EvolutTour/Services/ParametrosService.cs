using EvolutTur.Data;
using EvolutTur.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutTur.Services
{
    public class ParametrosService
    {
        private readonly SqlConnection _connection;

        private readonly Context _context;

        private readonly string _connectionString;

        public ParametrosService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<Models.Parametros> BuscaParametros(int CodEmp)
        {          

            var query = $"SELECT CodEmp, NomeAgencia, CNPJ, Endereco, Telefone, Email, Contrato FROM Parametros where CodEmp = @CodEmp";
            Models.Parametros set = new Models.Parametros();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);              

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {                       
                        set.CodEmp = (int)reader["CodEmp"];
                        set.NomeAgencia = reader["NomeAgencia"].ToString();
                        set.CNPJ = reader["CNPJ"].ToString();
                        set.Endereco = reader["Endereco"].ToString();
                        set.Telefone = reader["Telefone"].ToString();
                        set.Email = reader["Email"].ToString();
                        set.Contrato = reader["Contrato"].ToString();
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task InsereParametros(string nomeFantasia, int CodEmp, string CNPJ)
        {
            try
            {
                var query = "INSERT INTO Parametros (CodEmp, NomeAgencia, CNPJ) VALUES (@CodEmp, @NomeAgencia, @CNPJ)";
                using (SqlCommand command = new SqlCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@CodEmp", CodEmp);
                    command.Parameters.AddWithValue("@NomeAgencia", nomeFantasia);
                    command.Parameters.AddWithValue("@CNPJ", CNPJ);

                    _connection.Open();
                    await command.ExecuteNonQueryAsync();
                    _connection.Close();
                }
            }
            catch (Exception ee)
            {
                var a = ee.Message;
            }
        }

        public async Task AtualizaContrato(string contrato, int CodEmp)
        {
            var query = "UPDATE Parametros SET Contrato = @Contrato WHERE CodEmp = @CodEmp";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.Add("@Contrato", SqlDbType.VarChar, -1).Value = contrato;

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }


    }
}