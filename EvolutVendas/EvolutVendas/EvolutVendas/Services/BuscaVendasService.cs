using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutVendas.Services
{
    public class BuscaVendasService
    {
        private readonly string _connectionString;

        public BuscaVendasService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<List<Models.VendaBuscaC>> BuscarVendas(string cnpj, DateTime dataInicio, DateTime dataFim)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                var datainicial = dataInicio.ToString("yyyy-MM-dd");
                var datafinal = dataFim.ToString("yyyy-MM-dd");

                const string query = @"
                SELECT CodVenda, TotalVenda, DataMovimento, DataHoraVenda, NomeCliente
                FROM VendaC
                WHERE CNPJ = @CNPJ
                  AND DataMovimento BETWEEN @DataInicio AND @DataFim
                ORDER BY DataMovimento DESC";

                var lista = new List<Models.VendaBuscaC>();

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(query, conn);

                cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                cmd.Parameters.Add("@DataInicio", SqlDbType.VarChar).Value = datainicial;
                cmd.Parameters.Add("@DataFim", SqlDbType.VarChar).Value = datafinal;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lista.Add(new Models.VendaBuscaC
                    {
                        Codigo = reader.GetInt32(reader.GetOrdinal("CodVenda")),
                        TotalVenda = reader.GetDecimal(reader.GetOrdinal("TotalVenda")),
                        DataMovimento = reader.GetDateTime(reader.GetOrdinal("DataMovimento")),
                        DataHoraVenda = reader.GetDateTime(reader.GetOrdinal("DataHoraVenda")),
                        NomeCliente = reader["NomeCliente"].ToString(),
                        CNPJ = cnpj
                    });
                }

                return lista;
            }
            catch (Exception aa)
            {
                var bb = aa.Message;
                return null;
            }
        }

        public async Task<List<Models.VendaDBusca>> BuscarProdutosVenda(int codigoVenda, string cnpj)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                const string query = @"
                SELECT NomeProduto, Quantidade, PrecoVenda, PrecoTotal
                FROM VendaD
                WHERE CNPJ = @CNPJ
                 and CodVendaC = @CodVendaC";

                var lista = new List<Models.VendaDBusca>();

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(query, conn);

                cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                cmd.Parameters.Add("@CodVendaC", SqlDbType.VarChar).Value = codigoVenda;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lista.Add(new Models.VendaDBusca
                    {
                        NomeProduto = reader.GetString(reader.GetOrdinal("NomeProduto")),
                        Quantidade = reader.GetDecimal(reader.GetOrdinal("Quantidade")),
                        PrecoVenda = reader.GetDecimal(reader.GetOrdinal("PrecoVenda")),
                        PrecoTotal = reader.GetDecimal(reader.GetOrdinal("PrecoTotal")),

                    });
                }

                return lista;
            }
            catch (Exception aa)
            {
                var bb = aa.Message;
                return null;
            }
        }
    }
}
