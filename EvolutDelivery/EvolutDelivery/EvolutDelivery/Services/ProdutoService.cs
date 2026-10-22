using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class ProdutoService
    {
        private readonly string _connectionString;

        public ProdutoService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public List<Produto> BuscarProdutos(string busca, string cnpj)
        {
            const string query = @"
        SELECT DISTINCT 
    Codigo,
    Descricao,
    Unidade,
    CodSecao,
    NomeSecao,
    PrecoUnitario,
    QuantidadeEmbalagem,
    PrecoCusto,
    PrecoVenda,
    PrecoPromocao,
    Estoque,
    EstoqueAtual,
    CNPJ,
    ISNULL(PermiteDecimal, 'N') AS PermiteDecimal
FROM ProdutoAFV
WHERE CNPJ = @CNPJ
  AND Descricao LIKE @Descricao
ORDER BY Descricao";

            var lista = new List<Produto>();

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);
            command.Parameters.AddWithValue("@Descricao", "%" + busca + "%");

            conn.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var prod = new Produto
                {
                    Codigo = reader["Codigo"] != DBNull.Value ? Convert.ToInt32(reader["Codigo"]) : 0,
                    Nome = reader["Descricao"]?.ToString() ?? string.Empty,
                    Unidade = reader["Unidade"]?.ToString() ?? string.Empty,
                    CodSecao = reader["CodSecao"] != DBNull.Value ? Convert.ToInt32(reader["CodSecao"]) : 0,
                    NomeSecao = reader["NomeSecao"]?.ToString() ?? string.Empty,
                    PrecoUnitario = reader["PrecoUnitario"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoUnitario"]) : 0,
                    QuantidadeEmbalagem = reader["QuantidadeEmbalagem"] != DBNull.Value ? Convert.ToDecimal(reader["QuantidadeEmbalagem"]) : 0,
                    PrecoCusto = reader["PrecoCusto"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoCusto"]) : 0,
                    PrecoVenda = reader["PrecoVenda"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoVenda"]) : 0,
                    PrecoPromocao = reader["PrecoPromocao"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoPromocao"]) : 0,
                    Estoque = reader["Estoque"] != DBNull.Value ? Convert.ToDecimal(reader["Estoque"]) : 0,
                    EstoqueAtual = reader["EstoqueAtual"] != DBNull.Value ? Convert.ToDecimal(reader["EstoqueAtual"]) : 0,
                    CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty,
                    PermiteDecimal = reader["PermiteDecimal"]?.ToString() ?? "N"
                };


                lista.Add(prod);
            }

            return lista;
        }

        public List<Produto> BuscarProdutosPorSecao(int codSecao, string cnpj)
        {
            const string query = @"
        SELECT DISTINCT 
            Codigo,
            Descricao,
            Unidade,
            CodSecao,
            NomeSecao,
            PrecoUnitario,
            QuantidadeEmbalagem,
            PrecoCusto,
            PrecoVenda,
            PrecoPromocao,
            Estoque,
            EstoqueAtual,
            CNPJ,
            ISNULL(PermiteDecimal, 'N') AS PermiteDecimal
        FROM ProdutoAFV
        WHERE CNPJ = @CNPJ
          AND CodSecao = @CodSecao
        ORDER BY Descricao";

            var lista = new List<Produto>();

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);
            command.Parameters.AddWithValue("@CodSecao", codSecao);

            conn.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var prod = new Produto
                {
                    Codigo = reader["Codigo"] != DBNull.Value ? Convert.ToInt32(reader["Codigo"]) : 0,
                    Nome = reader["Descricao"]?.ToString() ?? string.Empty,
                    Unidade = reader["Unidade"]?.ToString() ?? string.Empty,
                    CodSecao = reader["CodSecao"] != DBNull.Value ? Convert.ToInt32(reader["CodSecao"]) : 0,
                    NomeSecao = reader["NomeSecao"]?.ToString() ?? string.Empty,
                    PrecoUnitario = reader["PrecoUnitario"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoUnitario"]) : 0,
                    QuantidadeEmbalagem = reader["QuantidadeEmbalagem"] != DBNull.Value ? Convert.ToDecimal(reader["QuantidadeEmbalagem"]) : 0,
                    PrecoCusto = reader["PrecoCusto"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoCusto"]) : 0,
                    PrecoVenda = reader["PrecoVenda"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoVenda"]) : 0,
                    PrecoPromocao = reader["PrecoPromocao"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoPromocao"]) : 0,
                    Estoque = reader["Estoque"] != DBNull.Value ? Convert.ToDecimal(reader["Estoque"]) : 0,
                    EstoqueAtual = reader["EstoqueAtual"] != DBNull.Value ? Convert.ToDecimal(reader["EstoqueAtual"]) : 0,
                    CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty,
                    PermiteDecimal = reader["PermiteDecimal"]?.ToString() ?? "N"
                };

                lista.Add(prod);
            }

            return lista;
        }

        public string? BuscarImagemBase64(int codigo, string cnpj)
        {
            const string query = @"
        SELECT Imagem FROM ProdutoAFV 
        WHERE Codigo = @Codigo AND CNPJ = @CNPJ";

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);
            command.Parameters.AddWithValue("@Codigo", codigo);
            command.Parameters.AddWithValue("@CNPJ", cnpj);

            conn.Open();
            var result = command.ExecuteScalar();

            if (result == null || result == DBNull.Value) return null;

            var bytes = (byte[])result;

            // Detecta o tipo pela assinatura dos bytes
            string mime = "image/jpeg"; // padrão
            if (bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 0x50)
                mime = "image/png";
            else if (bytes.Length > 1 && bytes[0] == 0x47 && bytes[1] == 0x49)
                mime = "image/gif";
            else if (bytes.Length > 3 && bytes[0] == 0x52 && bytes[1] == 0x49)
                mime = "image/webp";

            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        public List<SecaoModel> BuscarSecoes(string cnpj)
        {
            const string query = @"
                SELECT DISTINCT CodSecao, NomeSecao
                FROM ProdutoAFV
                WHERE CNPJ = @CNPJ
                  AND CodSecao IS NOT NULL
                  AND NomeSecao IS NOT NULL
                ORDER BY NomeSecao";

            var lista = new List<SecaoModel>();

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);

            conn.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var secao = new SecaoModel
                {
                    Codigo = reader["CodSecao"] != DBNull.Value ? Convert.ToInt32(reader["CodSecao"]) : 0,
                    Descricao = reader["NomeSecao"]?.ToString() ?? string.Empty
                };

                lista.Add(secao);
            }

            return lista;
        }
    }
}