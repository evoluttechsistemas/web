using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutVendas.Services
{
    public class BuscaProdutosService
    {
        private readonly string _connectionString;

        public BuscaProdutosService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<List<Models.ProdutoBuscaModel>> BuscarProdutosAsync(string cnpj, string filtro)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                if (string.IsNullOrWhiteSpace(filtro))
                    filtro = "%";
                else
                    filtro = "%" + filtro + "%";

                const string query = @"
        SELECT CodProduto, Descricao, PrecoCusto, PrecoVenda, EstoqueAtual, EstoqueMinimo
        FROM Produto
        WHERE CNPJ = @CNPJ
          AND Descricao LIKE @Filtro
        ORDER BY Descricao";

                var lista = new List<Models.ProdutoBuscaModel>();

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(query, conn);

                cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                cmd.Parameters.Add("@Filtro", SqlDbType.VarChar).Value = filtro;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lista.Add(new Models.ProdutoBuscaModel
                    {
                        Codigo = reader.GetInt32(reader.GetOrdinal("CodProduto")),
                        Descricao = reader.GetString(reader.GetOrdinal("Descricao")),
                        PrecoCusto = reader.GetDecimal(reader.GetOrdinal("PrecoCusto")),
                        PrecoVenda = reader.GetDecimal(reader.GetOrdinal("PrecoVenda")),
                        EstoqueAtual = reader.GetDecimal(reader.GetOrdinal("EstoqueAtual")),
                        EstoqueMinimo = reader.GetDecimal(reader.GetOrdinal("EstoqueMinimo")),
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar produtos: {ex.Message}");
                return new List<Models.ProdutoBuscaModel>();
            }
        }
    }
}
