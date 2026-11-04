using EvolutCotacao.Data;
using EvolutCotacao.Models;
using Microsoft.Data.SqlClient;

namespace EvolutCotacao.Service
{
    public class CotacaoService
    {    
        private readonly string _connectionString;

        public CotacaoService(IConfiguration configuration)
        {           

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<List<ProdutoCotado>> BuscarCotacao(string codigoCotacao, string cnpjEmpresa, string codigoForcedor)
        {            
            var produtos = new List<ProdutoCotado>();

            if (codigoCotacao == "N/A")
                return produtos;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("SELECT CodProduto, NomeProduto, Quantidade FROM CotacaoProduto prd join CotacaoC c on c.Codigo = prd.CodCotacaoC join CotacaoFornecedor forn on forn.CodCotacaoC = c.Codigo WHERE prd.CodCotacaoC = @codigo and prd.CNPJ = @CNPJ and forn.Finalizado = 'N' and forn.CNPJ = @CNPJ and c.CNPJ = @CNPJ and forn.CodFornecedor = @CodFornecedor", conn);
            cmd.Parameters.AddWithValue("@codigo", codigoCotacao);
            cmd.Parameters.AddWithValue("@CNPJ", cnpjEmpresa);
            cmd.Parameters.AddWithValue("@CodFornecedor", codigoForcedor);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                produtos.Add(new ProdutoCotado
                {
                    Codigo = reader["CodProduto"].ToString(),
                    Nome = reader["NomeProduto"].ToString(),
                    Quantidade = Convert.ToDecimal(reader["Quantidade"])
                });
            }

            return produtos;
        }

        public async Task GravarRespostaCotacao(string codCotacao, string codFornecedor, List<ProdutoCotado> produtos, string cnpjEmpresa)
        {
            if (string.IsNullOrWhiteSpace(codCotacao) || string.IsNullOrWhiteSpace(codFornecedor))
                return;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var item in produtos)
            {
                var cmd = new SqlCommand(@"update CotacaoLancamento set PrecoUnitario = @PrecoUnitario, PrecoTotal = @PrecoTotal, Observacao = @Observacao where CodCotacaoC = @CodCotacaoC and CodProduto = @CodProduto and CodFornecedor = @CodFornecedor and CNPJ = @CNPJ", conn);

                cmd.Parameters.AddWithValue("@CodCotacaoC", codCotacao);
                cmd.Parameters.AddWithValue("@CodFornecedor", codFornecedor);
                cmd.Parameters.AddWithValue("@CodProduto", item.Codigo);
                cmd.Parameters.AddWithValue("@PrecoUnitario", item.PrecoUnitario);
                cmd.Parameters.AddWithValue("@PrecoTotal", item.Total);
                cmd.Parameters.AddWithValue("@Observacao", item.Observacao);
                cmd.Parameters.AddWithValue("@CNPJ", cnpjEmpresa);

                await cmd.ExecuteNonQueryAsync();
            }

            // Atualiza o status da cotação para 'ENVIADO'
            var updateCmd = new SqlCommand("UPDATE CotacaoFornecedor SET Finalizado = 'S' WHERE CodCotacaoC = @CodCotacaoC and CNPJ = @CNPJ and CodFornecedor = @CodFornecedor", conn);
            updateCmd.Parameters.AddWithValue("@CodCotacaoC", codCotacao);
            updateCmd.Parameters.AddWithValue("@CNPJ", cnpjEmpresa);
            updateCmd.Parameters.AddWithValue("@CodFornecedor", codFornecedor);
            await updateCmd.ExecuteNonQueryAsync();
        }

        //public async Task AddSetor(Models.Setor setor)
        //{
        //    var query = "INSERT INTO Setor (Descricao) VALUES (@Descricao)";
        //    using (SqlCommand command = new SqlCommand(query, _connection))
        //    {
        //        command.Parameters.AddWithValue("@Descricao", setor.Descricao);

        //        _connection.Open();
        //        await command.ExecuteNonQueryAsync();
        //        _connection.Close();
        //    }
        //}

        //public async Task UpdateSetorAsync(Setor setor)
        //{
        //    var query = "UPDATE Setor SET Descricao = @Descricao WHERE Codigo = @Codigo";
        //    using (SqlCommand command = new SqlCommand(query, _connection))
        //    {
        //        command.Parameters.AddWithValue("@Codigo", setor.Codigo);
        //        command.Parameters.AddWithValue("@Descricao", setor.Descricao);

        //        _connection.Open();
        //        await command.ExecuteNonQueryAsync();
        //        _connection.Close();
        //    }
        //}
    }
}
