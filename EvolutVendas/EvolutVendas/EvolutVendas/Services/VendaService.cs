using EvolutVendas.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using static EvolutVendas.Components.Pages.NovaVenda;

namespace EvolutVendas.Services
{
    public class VendaService
    {
        private readonly string _connectionString;

        public VendaService(IConfiguration config)
        {
            // usa a connection string do appsettings.json
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<List<Models.VendaC>> BuscaVendasDia(string cnpj, int codVendedor)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",","");

                string dataAtual = DateTime.Now.ToString("yyyy-MM-dd");

                const string query = @"SELECT Codigo, Status, CodCliente, CodVendedor, CodEmp, TotalVenda, Observacao, 
                                          CNPJ, DataHoraMovimento, DataMovimento, NomeCliente, Usuario 
                                   FROM VendaCAFV  
                                   WHERE CNPJ = @CNPJ 
                                     AND DataMovimento = @DataMovimento 
                                     AND CodVendedor = @CodVendedor";

                var lista = new List<Models.VendaC>();

                using var conn = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, conn);

                command.Parameters.AddWithValue("@CNPJ", cnpj);
                command.Parameters.AddWithValue("@DataMovimento", dataAtual);
                command.Parameters.AddWithValue("@CodVendedor", codVendedor);

                await conn.OpenAsync();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var cli = new Models.VendaC
                    {
                        Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                        Status = reader["Status"].ToString(),
                        CodCliente = reader["CodCliente"].ToString(),
                        CodVendedor = reader["CodVendedor"].ToString(),
                        CodEmp = reader["CodEmp"].ToString(),
                        TotalVenda = reader.GetDecimal(reader.GetOrdinal("TotalVenda")),
                        Observacao = reader["Observacao"].ToString(),
                        CNPJ = reader["CNPJ"].ToString(),
                        DataHoraMovimento = reader["DataHoraMovimento"].ToString(),
                        DataMovimento = reader["DataMovimento"].ToString(),
                        NomeCliente = reader["NomeCliente"].ToString(),
                        Usuario = reader["Usuario"].ToString()
                    };
                    lista.Add(cli);
                }

                return lista;
            }
            catch (Exception ex)
            {
                return null;
            }

        }

        public async Task<List<FormaPagamentoModel>> BuscarFormasPagamentoAsync(string cnpj)
        {
            cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

            const string query = @"SELECT Codigo, Descricao, Parcelas, CodCliente, TipoPagamento, CodFormaPagamentoAFV, isnull(Dias,30) Dias
                           FROM FormaPagamentoAFV 
                           WHERE CNPJ = @CNPJ";

            var lista = new List<FormaPagamentoModel>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@CNPJ", cnpj);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new FormaPagamentoModel
                {
                    Codigo = reader.GetInt32(0),
                    Descricao = reader["Descricao"].ToString(),
                    Parcelas = reader.GetInt32(reader.GetOrdinal("Parcelas")),
                    Dias = reader.GetInt32(reader.GetOrdinal("Dias")),
                    CodCliente = reader.GetInt32(reader.GetOrdinal("CodCliente")),
                    CodFormaPagamento = reader.GetInt32(reader.GetOrdinal("CodFormaPagamentoAFV")),
                    TipoPagamento = reader["TipoPagamento"].ToString()
                });
            }

            return lista;
        }

        public async Task<List<Models.VendaDBusca>> BuscaProdutosVendaD(int codigoVenda, string cnpj)
        {
            try
            {
                cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

                const string query = @"
                SELECT NomeProduto, Quantidade, PrecoUnitario, PrecoTotal
                FROM VendaDAFV
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
                        PrecoVenda = reader.GetDecimal(reader.GetOrdinal("PrecoUnitario")),
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

        public async Task<List<Models.ProdutoModel>> BuscarProdutosAsync(string busca, string cnpj)
        {
            cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

            const string query = @"SELECT  DISTINCT top 100 Codigo, Descricao, PrecoVenda, Imagem 
                                   FROM ProdutoAFV 
                                   WHERE CNPJ = @CNPJ 
                                     AND Descricao LIKE @Descricao";

            var lista = new List<Models.ProdutoModel>();

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);
            command.Parameters.AddWithValue("@Descricao", "%" + busca + "%");

            await conn.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var prod = new Models.ProdutoModel
                {
                    Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                    Descricao = reader["Descricao"].ToString(),
                    PrecoUnitario = reader.GetDecimal(reader.GetOrdinal("PrecoVenda"))
                };

                if (!(reader["Imagem"] is DBNull))
                    prod.Imagem = (byte[])reader["Imagem"];

                lista.Add(prod);
            }

            return lista;
        }

        public async Task<List<Models.ClienteModel>> BuscarClientesAsync(string busca, string cnpj)
        {
            cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

            const string query = @"SELECT DISTINCT top 100 Codigo, Endereco, Nome 
                                   FROM ClienteAFV 
                                   WHERE CNPJ = @CNPJ 
                                     AND Nome LIKE @Nome";

            var lista = new List<Models.ClienteModel>();

            using var conn = new SqlConnection(_connectionString);
            using var command = new SqlCommand(query, conn);

            command.Parameters.AddWithValue("@CNPJ", cnpj);
            command.Parameters.AddWithValue("@Nome", "%" + busca + "%");

            await conn.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var cli = new Models.ClienteModel
                {
                    Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                    Nome = reader["Nome"].ToString(),
                    Endereco = reader["Endereco"].ToString()
                };
                lista.Add(cli);
            }

            return lista;
        }

        public async Task<bool> InserirVendaAsync(Models.ClienteModel cliente, string cnpj, int codVendedor, List<ProdutoSelecionado> produtos, string usuario, FormaPagamentoModel formaPagamento)
        {
            cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

            if (produtos == null || produtos.Count == 0)
                return false;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var transaction = conn.BeginTransaction();
            using var command = conn.CreateCommand();
            command.Transaction = transaction;

            try
            {
                decimal totalVenda = produtos.Sum(p => p.PrecoUnitario * p.Quantidade);

                // Inserir VendaC
                command.CommandText = @"
                    INSERT INTO VendaCAFV (Status, CodCliente, CodVendedor, CodEmp, TotalVenda, Observacao, 
                                        CNPJ, DataHoraMovimento, DataMovimento, NomeCliente, Usuario)
                    VALUES ('P', @CodCliente, @CodVendedor, 1, @TotalVenda, @Observacao, 
                            @CNPJ, GETDATE(), GETDATE(), @NomeCliente, @Usuario);
                    SELECT SCOPE_IDENTITY();";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@CodCliente", cliente.Codigo);
                command.Parameters.AddWithValue("@CodVendedor", codVendedor);
                command.Parameters.AddWithValue("@TotalVenda", totalVenda);
                command.Parameters.AddWithValue("@Observacao", "VENDA REALIZADA PELA FORÇA DE VENDA");
                command.Parameters.AddWithValue("@CNPJ", cnpj);
                command.Parameters.AddWithValue("@NomeCliente", cliente.Nome);
                command.Parameters.AddWithValue("@Usuario", usuario);

                int idVenda = Convert.ToInt32(await command.ExecuteScalarAsync());

                // Inserir produtos na VendaD
                foreach (var prod in produtos)
                {
                    decimal total = prod.PrecoUnitario * prod.Quantidade;

                    command.CommandText = @"
                        INSERT INTO VendaDAFV (CodVendaC, CodProduto, NomeProduto, Quantidade, PrecoUnitario, PrecoTotal, PrecoCusto, CodVendedor, CNPJ)
                        VALUES (@CodVendaC, @CodProduto, @NomeProduto, @Quantidade, @PrecoUnitario, @PrecoTotal, @PrecoCusto, @CodVendedor, @CNPJ)";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@CodVendaC", idVenda);
                    command.Parameters.AddWithValue("@CodProduto", prod.Codigo);
                    command.Parameters.AddWithValue("@NomeProduto", prod.Descricao);
                    command.Parameters.AddWithValue("@Quantidade", prod.Quantidade);
                    command.Parameters.AddWithValue("@PrecoUnitario", prod.PrecoUnitario);
                    command.Parameters.AddWithValue("@PrecoTotal", total);
                    command.Parameters.AddWithValue("@PrecoCusto", 0);
                    command.Parameters.AddWithValue("@CodVendedor", codVendedor);
                    command.Parameters.AddWithValue("@CNPJ", cnpj);

                    await command.ExecuteNonQueryAsync();
                }

                //insere financeiro
                if (formaPagamento != null)
                {
                    if (formaPagamento.TipoPagamento == "DIN")
                    {                       
                        command.Parameters.Clear();
                        command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, DataPagamento, Valor, ValorPago)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, getdate(), getdate(), @Observacao, @CNPJ, @Usuario, 1, getdate(), @Valor, @ValorPago)";

                        command.Parameters.AddWithValue("@CodVendaC", idVenda);
                        command.Parameters.AddWithValue("@CodCliente", formaPagamento.CodCliente);
                        command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                        command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                        command.Parameters.AddWithValue("@CNPJ", cnpj);
                        command.Parameters.AddWithValue("@Usuario", usuario);
                        command.Parameters.AddWithValue("@Valor", totalVenda);
                        command.Parameters.AddWithValue("@ValorPago", totalVenda);

                        await command.ExecuteNonQueryAsync();
                    }

                    if (formaPagamento.TipoPagamento == "CHV")
                    {
                        command.Parameters.Clear();
                        command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, DataPagamento, Valor, ValorPago)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, getdate(), getdate(), @Observacao, @CNPJ, @Usuario, 1, getdate(), @Valor, @ValorPago)";

                        command.Parameters.AddWithValue("@CodVendaC", idVenda);
                        command.Parameters.AddWithValue("@CodCliente", formaPagamento.CodCliente);
                        command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                        command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                        command.Parameters.AddWithValue("@CNPJ", cnpj);
                        command.Parameters.AddWithValue("@Usuario", usuario);
                        command.Parameters.AddWithValue("@Valor", totalVenda);
                        command.Parameters.AddWithValue("@ValorPago", totalVenda);

                        await command.ExecuteNonQueryAsync();
                    }

                    if (formaPagamento.TipoPagamento == "CHP")
                    {
                        int parcelas = formaPagamento.Parcelas;
                        decimal valorParcela = totalVenda / parcelas;                       

                        for (int i = 0; i < parcelas; i++)
                        {
                            command.Parameters.Clear();
                            command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, Valor)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, getdate(), getdate(), @Observacao, @CNPJ, @Usuario, 1, @Valor)";

                            command.Parameters.AddWithValue("@CodVendaC", idVenda);
                            command.Parameters.AddWithValue("@CodCliente", formaPagamento.CodCliente);
                            command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                            command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                            command.Parameters.AddWithValue("@CNPJ", cnpj);
                            command.Parameters.AddWithValue("@Usuario", usuario);
                            command.Parameters.AddWithValue("@Valor", valorParcela);                           

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    if (formaPagamento.TipoPagamento == "CRC")
                    {
                        int parcelas = formaPagamento.Parcelas;
                        decimal valorParcela = totalVenda / parcelas;                        

                        for (int i = 0; i < parcelas; i++)
                        {
                            command.Parameters.Clear();
                            command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, Valor)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, getdate(), getdate(), @Observacao, @CNPJ, @Usuario, 1, @Valor)";

                            command.Parameters.AddWithValue("@CodVendaC", idVenda);
                            command.Parameters.AddWithValue("@CodCliente", formaPagamento.CodCliente);
                            command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                            command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                            command.Parameters.AddWithValue("@CNPJ", cnpj);
                            command.Parameters.AddWithValue("@Usuario", usuario);
                            command.Parameters.AddWithValue("@Valor", valorParcela);

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    if (formaPagamento.TipoPagamento == "CRD")
                    {
                        int parcelas = formaPagamento.Parcelas;
                        decimal valorParcela = totalVenda / parcelas;
                        int dias = formaPagamento.Dias;

                        for (int i = 0; i < parcelas; i++)
                        {
                            string dataMovimento = DateTime.Now.ToString("yyyy-MM-dd");
                            DateTime dataVencimentoData = DateTime.Now.AddDays(dias);

                            string dataVencimento = dataVencimentoData.ToString("yyyy-MM-dd");

                            command.Parameters.Clear();
                            command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, Valor)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, @DataMovimento, @DataVencimento, @Observacao, @CNPJ, @Usuario, 1, @Valor)";

                            command.Parameters.AddWithValue("@CodVendaC", idVenda);
                            command.Parameters.AddWithValue("@CodCliente", cliente.Codigo);
                            command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                            command.Parameters.AddWithValue("@DataMovimento", dataMovimento);
                            command.Parameters.AddWithValue("@DataVencimento", dataVencimento);
                            command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                            command.Parameters.AddWithValue("@CNPJ", cnpj);
                            command.Parameters.AddWithValue("@Usuario", usuario);
                            command.Parameters.AddWithValue("@Valor", valorParcela);

                            dias = dias + dias;

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    if (formaPagamento.TipoPagamento == "PIX")
                    {
                        command.Parameters.Clear();
                        command.CommandText = @"
                INSERT INTO ContaReceberAFV 
                    (CodVendaC, CodCliente, FormaPagamento, DataMovimento, DataVencimento, Observacao, CNPJ, Usuario, CodEmp, DataPagamento, Valor, ValorPago)
                VALUES 
                    (@CodVendaC, @CodCliente, @FormaPagamento, getdate(), getdate(), @Observacao, @CNPJ, @Usuario, 1, getdate(), @Valor, @ValorPago)";

                        command.Parameters.AddWithValue("@CodVendaC", idVenda);
                        command.Parameters.AddWithValue("@CodCliente", formaPagamento.CodCliente);
                        command.Parameters.AddWithValue("@FormaPagamento", formaPagamento.TipoPagamento);
                        command.Parameters.AddWithValue("@Observacao", "Venda AFV, Cod Forma Pagamento AFV " + formaPagamento.CodFormaPagamento);
                        command.Parameters.AddWithValue("@CNPJ", cnpj);
                        command.Parameters.AddWithValue("@Usuario", usuario);
                        command.Parameters.AddWithValue("@Valor", totalVenda);
                        command.Parameters.AddWithValue("@ValorPago", totalVenda);

                        await command.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {

                transaction.Rollback();
                return false;
            }
        }
    }
}
