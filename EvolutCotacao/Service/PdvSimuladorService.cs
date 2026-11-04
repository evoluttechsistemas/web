using EvolutCotacao.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EvolutCotacao.Service
{
    public class PdvSimuladorService
    {
        private readonly string _connectionString;

        public PdvSimuladorService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConnectionServidor")
                ?? throw new InvalidOperationException("Connection string 'Connection' nao encontrada.");
        }

        public async Task<List<PdvSimuladorProduto>> BuscarProdutosAsync(string termo)
        {
            var produtos = new List<PdvSimuladorProduto>();

            if (string.IsNullOrWhiteSpace(termo))
                return produtos;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(@"
SELECT TOP 20
       ISNULL(CodBarras, '') CodBarras,
       ISNULL(Descricao, '') Descricao
  FROM BaseProdutos
 WHERE CodBarras = @Termo
    OR Descricao LIKE @Descricao
 ORDER BY
       CASE WHEN CodBarras = @Termo THEN 0 ELSE 1 END,
       Descricao", conn);

            cmd.Parameters.AddWithValue("@Termo", termo.Trim());
            cmd.Parameters.AddWithValue("@Descricao", "%" + termo.Trim() + "%");

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                produtos.Add(new PdvSimuladorProduto
                {
                    CodBarras = reader["CodBarras"].ToString() ?? "",
                    Descricao = reader["Descricao"].ToString() ?? "",
                    PrecoVenda = 10m
                });
            }

            return produtos;
        }
    }
}
