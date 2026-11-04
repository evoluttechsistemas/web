using EvolutVendas.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutVendas.Services
{
    public class BuscaEstatisticasService
    {
        private readonly string _connectionString;

        public BuscaEstatisticasService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<Models.EstatisticasModel> BuscaEstatisticaPagamentos(string cnpj, DateTime dataInicio, DateTime dataFim)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                var datainicial = dataInicio.ToString("yyyy-MM-dd");
                var datafinal = dataFim.ToString("yyyy-MM-dd");

                var estatistica = new Models.EstatisticasModel();

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // ==============================
                // 1) Buscar valores por pagamento (ContaReceber)
                // ==============================
                const string queryPagamentos = @"
            SELECT TipoPagamento, SUM(Valor) Valor 
            FROM ContaReceber 
            WHERE CNPJ = @CNPJ 
              AND DataMovimento BETWEEN @DataInicio AND @DataFim
            GROUP BY TipoPagamento";

                using (var cmd = new SqlCommand(queryPagamentos, conn))
                {
                    cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                    cmd.Parameters.Add("@DataInicio", SqlDbType.VarChar).Value = datainicial;
                    cmd.Parameters.Add("@DataFim", SqlDbType.VarChar).Value = datafinal;

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var tipo = reader.GetString(reader.GetOrdinal("TipoPagamento"));
                        var valor = reader.GetDecimal(reader.GetOrdinal("Valor"));

                        if (tipo == "DIN") estatistica.Dinheiro = valor;
                        if (tipo == "CRC") estatistica.Cartao = valor;
                        if (tipo == "CRD") estatistica.Crediario = valor;
                        if (tipo == "PIX") estatistica.PIX = valor;
                    }
                }

                // ==============================
                // 2) Buscar quantidade e total de vendas (VendaC)
                // ==============================
                const string queryVendas = @"
            SELECT COUNT(*) AS QtdVendas, SUM(TotalVenda) AS TotalVendas
            FROM VendaC
            WHERE CNPJ = @CNPJ
              AND DataMovimento BETWEEN @DataInicio AND @DataFim";

                using (var cmd2 = new SqlCommand(queryVendas, conn))
                {
                    cmd2.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
                    cmd2.Parameters.Add("@DataInicio", SqlDbType.VarChar).Value = datainicial;
                    cmd2.Parameters.Add("@DataFim", SqlDbType.VarChar).Value = datafinal;

                    using var reader2 = await cmd2.ExecuteReaderAsync();
                    if (await reader2.ReadAsync())
                    {
                        estatistica.QuantidadeVendas = reader2["QtdVendas"] != DBNull.Value ? Convert.ToInt32(reader2["QtdVendas"]) : 0;
                        estatistica.Faturamento = reader2["TotalVendas"] != DBNull.Value ? Convert.ToDecimal(reader2["TotalVendas"]) : 0;
                    }
                }

                // ==============================
                // 3) Calcular Ticket Médio
                // ==============================
                if (estatistica.QuantidadeVendas > 0)
                    estatistica.TicketMedio = estatistica.Faturamento / estatistica.QuantidadeVendas;

                return estatistica;
            }
            catch (Exception ex)
            {
                var erro = ex.Message;
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
