using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics.Eventing.Reader;

namespace EvolutDelivery.Services
{
    public class VendaService
    {
        private readonly string _connectionString;

        public VendaService(IConfiguration config)
        {
            // usa a connection string do appsettings.json
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public bool InserirVenda(string telefone, string nomeCliente, string endereco, string cnpj, List<ItemCarrinho> produtos, string usuario, string formaPagamento, string entregaRetira)
        {
            if (produtos == null || produtos.Count == 0)
                return false;

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var transaction = conn.BeginTransaction();
            using var command = conn.CreateCommand();
            command.Transaction = transaction;

            try
            {
                decimal totalVenda = produtos.Sum(p => p.Preco * p.Quantidade);

                // Inserir VendaC
                command.CommandText = @"
                    INSERT INTO VendaCAFV (Status, CodCliente, CodVendedor, CodEmp, TotalVenda, Observacao, 
                                        CNPJ, DataHoraMovimento, DataMovimento, NomeCliente, Usuario, FormaPagamento, EntregaRetira)
                    VALUES ('P', @CodCliente, @CodVendedor, 1, @TotalVenda, @Observacao, 
                            @CNPJ, GETDATE(), GETDATE(), @NomeCliente, @Usuario, @FormaPagamento, @EntregaRetira);
                    SELECT SCOPE_IDENTITY();";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@CodCliente", 0);
                command.Parameters.AddWithValue("@CodVendedor", 0);
                command.Parameters.AddWithValue("@TotalVenda", totalVenda);
                command.Parameters.AddWithValue("@Observacao", "CLIENTE: "+ nomeCliente + " TELEFONE: "+telefone + " TIPO: " + entregaRetira);
                command.Parameters.AddWithValue("@CNPJ", cnpj);
                command.Parameters.AddWithValue("@NomeCliente", nomeCliente);
                command.Parameters.AddWithValue("@Usuario", usuario);
                command.Parameters.AddWithValue("@FormaPagamento", formaPagamento);
                string EntregaRetira = "E";
                if (entregaRetira == "Retira")
                    EntregaRetira = "R";
                command.Parameters.AddWithValue("@EntregaRetira", EntregaRetira);

                int idVenda = Convert.ToInt32(command.ExecuteScalar());

                // Inserir produtos na VendaD
                foreach (var prod in produtos)
                {
                    decimal total = prod.Preco * prod.Quantidade;

                    command.CommandText = @"
                        INSERT INTO VendaDAFV (CodVendaC, CodProduto, NomeProduto, Quantidade, PrecoUnitario, PrecoTotal, PrecoCusto, CodVendedor, CNPJ)
                        VALUES (@CodVendaC, @CodProduto, @NomeProduto, @Quantidade, @PrecoUnitario, @PrecoTotal, @PrecoCusto, @CodVendedor, @CNPJ)";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@CodVendaC", idVenda);
                    command.Parameters.AddWithValue("@CodProduto", prod.Codigo);
                    command.Parameters.AddWithValue("@NomeProduto", prod.Nome);
                    command.Parameters.AddWithValue("@Quantidade", prod.Quantidade);
                    command.Parameters.AddWithValue("@PrecoUnitario", prod.Preco);
                    command.Parameters.AddWithValue("@PrecoTotal", total);
                    command.Parameters.AddWithValue("@PrecoCusto", 0);
                    command.Parameters.AddWithValue("@CodVendedor", 0);
                    command.Parameters.AddWithValue("@CNPJ", cnpj);

                    command.ExecuteNonQuery();
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
