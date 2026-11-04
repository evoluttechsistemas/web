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
                lista.Add(MapearProduto(reader));
            }

            return lista;
        }

        /// <summary>
        /// Devolve os bytes da imagem do produto e o tipo (image/jpeg, image/png...).
        /// Usado pelo endpoint GET /img/{slug}/{codigo}: a imagem vai por HTTP com cache,
        /// e não mais em base64 pelo SignalR.
        /// </summary>
        public async Task<(byte[] Bytes, string ContentType)?> BuscarImagemAsync(int codigo, string cnpj)
        {
            const string query = @"
        SELECT TOP 1 Imagem FROM ProdutoAFV 
        WHERE Codigo = @Codigo AND CNPJ = @CNPJ AND Imagem IS NOT NULL";

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);
            command.Parameters.AddWithValue("@Codigo", codigo);
            command.Parameters.AddWithValue("@CNPJ", cnpj);

            await conn.OpenAsync();
            var result = await command.ExecuteScalarAsync();

            if (result is not byte[] bytes || bytes.Length == 0)
                return null;

            return (bytes, DetectarTipoImagem(bytes));
        }

        private static string DetectarTipoImagem(byte[] bytes)
        {
            if (bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 0x50)
                return "image/png";
            if (bytes.Length > 1 && bytes[0] == 0x47 && bytes[1] == 0x49)
                return "image/gif";
            if (bytes.Length > 11 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[8] == 0x57 && bytes[9] == 0x45)
                return "image/webp";
            return "image/jpeg";
        }

        private static Produto MapearProduto(SqlDataReader reader)
        {
            return new Produto
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
                EstoqueAtual = reader["EstoqueAtual"] != DBNull.Value ? Convert.ToDecimal(reader["EstoqueAtual"]) : null,
                CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty,
                PermiteDecimal = reader["PermiteDecimal"]?.ToString() ?? "N"
            };
        }

        /// <summary>
        /// Busca no banco os dados oficiais (preço, nome, fracionamento) dos produtos do carrinho.
        /// Usado para NUNCA confiar no preço que vem do navegador (localStorage).
        /// </summary>
        public async Task<Dictionary<int, Produto>> BuscarPorCodigosAsync(IEnumerable<int> codigos, string cnpj)
        {
            var resultado = new Dictionary<int, Produto>();

            var lista = (codigos ?? Enumerable.Empty<int>())
                .Where(c => c > 0)
                .Distinct()
                .ToList();

            if (!lista.Any() || string.IsNullOrWhiteSpace(cnpj))
                return resultado;

            if (lista.Count > 500)
                throw new InvalidOperationException("Carrinho com itens demais.");

            var parametros = lista.Select((_, i) => "@c" + i).ToList();

            var sql = $@"
        SELECT
            Codigo,
            Descricao,
            PrecoVenda,
            PrecoPromocao,
            EstoqueAtual,
            ISNULL(PermiteDecimal, 'N') AS PermiteDecimal
        FROM ProdutoAFV
        WHERE CNPJ = @CNPJ
          AND Codigo IN ({string.Join(", ", parametros)})";

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(sql, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);
            for (int i = 0; i < lista.Count; i++)
                command.Parameters.AddWithValue(parametros[i], lista[i]);

            await conn.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var prod = new Produto
                {
                    Codigo = reader["Codigo"] != DBNull.Value ? Convert.ToInt32(reader["Codigo"]) : 0,
                    Nome = reader["Descricao"]?.ToString() ?? string.Empty,
                    PrecoVenda = reader["PrecoVenda"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoVenda"]) : 0,
                    PrecoPromocao = reader["PrecoPromocao"] != DBNull.Value ? Convert.ToDecimal(reader["PrecoPromocao"]) : 0,
                    PermiteDecimal = reader["PermiteDecimal"]?.ToString() ?? "N",
                    EstoqueAtual = reader["EstoqueAtual"] != DBNull.Value ? Convert.ToDecimal(reader["EstoqueAtual"]) : null,
                    CNPJ = cnpj
                };

                // ProdutoAFV pode ter linhas repetidas para o mesmo código: mantém a primeira
                resultado.TryAdd(prod.Codigo, prod);
            }

            return resultado;
        }

        /// <summary>
        /// Estoque atual de UM produto (consulta na hora, usado quando o cliente abre o produto).
        /// Devolve null quando o produto não tem estoque informado (vende sem controle).
        /// </summary>
        public async Task<decimal?> BuscarEstoqueAsync(int codigo, string cnpj)
        {
            const string query = @"
        SELECT TOP 1 EstoqueAtual
        FROM ProdutoAFV
        WHERE Codigo = @Codigo AND CNPJ = @CNPJ";

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);
            command.Parameters.AddWithValue("@Codigo", codigo);
            command.Parameters.AddWithValue("@CNPJ", cnpj);

            await conn.OpenAsync();
            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value ? null : Convert.ToDecimal(result);
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