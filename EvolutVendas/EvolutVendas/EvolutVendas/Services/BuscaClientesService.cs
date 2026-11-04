using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutVendas.Services
{
    public class BuscaClientesService
    {
        private readonly string _connectionString;

        public BuscaClientesService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<List<Models.ClienteBuscaModels>> BuscarClientesAsync(string cnpj, string filtro)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                if (string.IsNullOrWhiteSpace(filtro))
                    filtro = "%";
                else
                    filtro = "%" + filtro + "%";

                const string query = @"
        SELECT CodCliente, NomeCliente, Telefone, Celular
        FROM Cliente
        WHERE CNPJ = @CNPJ
          AND NomeCliente LIKE @Filtro
        ORDER BY NomeCliente";

                var lista = new List<Models.ClienteBuscaModels>();

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(query, conn);

                cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                cmd.Parameters.Add("@Filtro", SqlDbType.VarChar).Value = filtro;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lista.Add(new Models.ClienteBuscaModels
                    {
                        Codigo = reader.GetInt32(reader.GetOrdinal("CodCliente")),
                        Nome = reader.GetString(reader.GetOrdinal("NomeCliente")),
                        Telefone = reader.GetString(reader.GetOrdinal("Telefone")),
                        Celular = reader.GetString(reader.GetOrdinal("Celular")),                        
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar produtos: {ex.Message}");
                return new List<Models.ClienteBuscaModels>();
            }
        }
    }
}
