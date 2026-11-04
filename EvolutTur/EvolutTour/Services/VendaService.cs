using System.Data;
using EvolutTur.Data;
using EvolutTur.Models;
using Microsoft.Data.SqlClient;

namespace EvolutTur.Services
{
    public class VendaService
    {
        private readonly SqlConnection _connection;

        private readonly Context _context;

        private readonly string _connectionString;

        public VendaService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;

            // Retrieve the connection string from appsettings.json
            _connectionString = configuration.GetConnectionString("Connection");
        }

        public async Task<List<Models.Vendedor>> BuscarVendedor(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Nome like '%{filtroBusca}%'";

            var query = $"SELECT Codigo, Nome, isnull(Comissao,0) Comissao FROM Vendedor where CodEmp = @CodEmp {criterioBusca}";
            List<Models.Vendedor> set = new List<Models.Vendedor>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Models.Vendedor cli = new Models.Vendedor();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Nome = reader["Nome"].ToString();
                        cli.Comissao = decimal.Parse(reader["Comissao"].ToString());

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task<List<Models.Cliente>> BuscarCliente(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Nome like '%{filtroBusca}%'";

            var query = $"SELECT Codigo, Nome FROM Cliente where CodEmp = @CodEmp {criterioBusca}";
            List<Models.Cliente> set = new List<Models.Cliente>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Models.Cliente cli = new Models.Cliente();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Nome = reader["Nome"].ToString();

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task<List<Models.Fornecedor>> BuscaFornecedor(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Razao like '%{filtroBusca}%'";

            var query = $"SELECT Codigo, Razao, isnull(Comissao,0) Comissao FROM Fornecedor where CodEmp = @CodEmp {criterioBusca}";
            List<Models.Fornecedor> set = new List<Models.Fornecedor>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Models.Fornecedor cli = new Models.Fornecedor();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Razao = reader["Razao"].ToString();
                        cli.Comissao = decimal.Parse(reader["Comissao"].ToString());

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task<decimal> BuscaVendaMes(int CodEmp)
        {
            string mesAtual = DateTime.Now.ToString("MM");
            string anoAtual = DateTime.Now.ToString("yyyy");

            var query = $"SELECT sum(isnull(TotalVenda,0)) TotalVenda from VendaC where CodEmp = @CodEmp and year(DataVenda) = @AnoDataVenda and month(DataVenda) = @MesDataVenda";
            List<Models.Fornecedor> set = new List<Models.Fornecedor>();

            decimal valor = 0;

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@AnoDataVenda", anoAtual);
                command.Parameters.AddWithValue("@MesDataVenda", mesAtual);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        decimal val = reader["TotalVenda"] != DBNull.Value ? (decimal)reader["TotalVenda"] : 0;
                        valor = val;
                    }
                }
                _connection.Close();
            }

            return valor;
        }

        public async Task<List<Models.ClienteEmbarque>> BuscaEmbarquestUltimos7Dias(int CodEmp)
        {
            string mesAtual = DateTime.Now.ToString("MM");
            string anoAtual = DateTime.Now.ToString("yyyy");

            var query = $"SELECT c.CodCliente, d.DataVoo, cli.Nome, cli.Telefone FROM VendaC c JOIN VendaD d ON d.CodVendaC = c.Codigo JOIN Cliente cli ON cli.Codigo = c.CodCliente WHERE d.DataVoo BETWEEN GETDATE() AND DATEADD(DAY, 7, GETDATE()) and c.CodEmp = @CodEmp";
            List<Models.ClienteEmbarque> set = new List<Models.ClienteEmbarque>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        ClienteEmbarque em = new ClienteEmbarque();

                        em.CodCliente = (int)reader["CodCliente"];
                        em.DataVoo = (DateTime)reader["DataVoo"];
                        em.Nome = reader["Nome"].ToString();
                        em.Telefone = reader["Telefone"].ToString();

                        set.Add(em);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task ExcluirVenda(int CodVendaC)
        {
            try
            {
                _connection.Open();
                using (SqlTransaction transaction = _connection.BeginTransaction())
                {

                    // VendaViajante
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM VendaViajante WHERE CodVendaC = @CodVendaC",
                        _connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@CodVendaC", CodVendaC);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // ContaReceber
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM ContaReceber WHERE CodVendaC = @CodVendaC",
                        _connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@CodVendaC", CodVendaC);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // VendaD
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM VendaD WHERE CodVendaC = @CodVendaC",
                        _connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@CodVendaC", CodVendaC);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // VendaC (por último, cabeçalho)
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM VendaC WHERE Codigo = @CodVendaC",
                        _connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@CodVendaC", CodVendaC);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Confirma a transação
                    transaction.Commit();

                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _connection.Close();
            }
        }


        public async Task<List<Models.RelatorioVendas>> BuscaRelatorioVenda(int CodEmp, DateTime dataVendaInicio, DateTime dataVendaFim)
        {
            string dataInicio = dataVendaInicio.ToString("yyyy-MM-dd");
            string dataFim = dataVendaFim.ToString("yyyy-MM-dd");
            try
            {
                var query = $"select c.Codigo, cli.Nome, isnull(c.TotalVenda,0) TotalVenda, isnull(c.TotalComissaoFornecedor,0) TotalComissaoFornecedor, d.Tipo as item, con.Tipo as pagamento, c.DataVenda from VendaC c join Cliente cli on c.CodCliente = cli.Codigo join VendaD d on d.CodVendaC = c.Codigo join ContaReceber con on con.CodVendaC = c.Codigo WHERE c.DataVenda BETWEEN '" + dataInicio + "' AND '" + dataFim + "' and c.CodEmp = @CodEmp order by c.DataVenda desc";
                List<Models.RelatorioVendas> setVenda = new List<Models.RelatorioVendas>();

                using (SqlCommand command = new SqlCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@CodEmp", CodEmp);

                    _connection.Open();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            RelatorioVendas emVenda = new RelatorioVendas();

                            emVenda.Codigo = (int)reader["Codigo"];
                            emVenda.Cliente = reader["Nome"].ToString();
                            emVenda.Item = reader["item"].ToString();
                            emVenda.Pagamento = reader["pagamento"].ToString();
                            emVenda.Valor = (decimal)reader["TotalVenda"];
                            emVenda.Comissao = (decimal)reader["TotalComissaoFornecedor"];
                            emVenda.Data = (DateTime)reader["DataVenda"];

                            setVenda.Add(emVenda);
                        }
                    }
                    _connection.Close();
                }

                return setVenda;
            }
            catch (Exception a)
            {
                var b = a.Message;
                return null;
            }

        }

        public async Task<decimal> BuscaComissaoFornecedorMes(int CodEmp)
        {
            string mesAtual = DateTime.Now.ToString("MM");
            string anoAtual = DateTime.Now.ToString("yyyy");

            var query = $"SELECT sum(isnull(TotalComissaoFornecedor,0)) TotalComissaoFornecedor from VendaC where CodEmp = @CodEmp and year(DataVenda) = @AnoDataVenda and month(DataVenda) = @MesDataVenda";
            List<Models.Fornecedor> set = new List<Models.Fornecedor>();

            decimal valor = 0;

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@AnoDataVenda", anoAtual);
                command.Parameters.AddWithValue("@MesDataVenda", mesAtual);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        decimal val = reader["TotalComissaoFornecedor"] != DBNull.Value ? (decimal)reader["TotalComissaoFornecedor"] : 0;
                        valor = val;
                    }
                }
                _connection.Close();
            }

            return valor;
        }

        public async Task<int> AdicionarVendaC(int CodEmp, int codFornecedor, int codVendedor, string dataVenda, int codCliente, string orcamentoAtendimento, decimal totalVenda, decimal? percentualComissaoFornecedor, decimal? percentualComissaoVendedor)
        {
            // Query com SCOPE_IDENTITY() para retornar o último ID gerado
            var query = @"
        INSERT INTO VendaC (CodEmp, CodFornecedor, CodVendedor, DataVenda, CodCliente, OrcamentoAtendimento, DataHoraVenda, TotalVenda, PercentualComissaoFornecedor, TotalComissaoFornecedor, PercentualComissaoVendedor, TotalComissaoVendedor)
        VALUES (@CodEmp, @CodFornecedor, @CodVendedor, @DataVenda, @CodCliente, @OrcamentoAtendimento, GETDATE(), @TotalVenda, @PercentualComissaoFornecedor, @TotalComissaoFornecedor, @PercentualComissaoVendedor, @TotalComissaoVendedor);
        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmp", CodEmp);
                command.Parameters.AddWithValue("@CodFornecedor", codFornecedor);
                command.Parameters.AddWithValue("@CodVendedor", codVendedor);
                command.Parameters.AddWithValue("@DataVenda", dataVenda);
                command.Parameters.AddWithValue("@CodCliente", codCliente);
                command.Parameters.AddWithValue("@OrcamentoAtendimento", orcamentoAtendimento);
                command.Parameters.AddWithValue("@TotalVenda", totalVenda);
                command.Parameters.AddWithValue("@PercentualComissaoFornecedor", percentualComissaoFornecedor);
                command.Parameters.AddWithValue("@TotalComissaoFornecedor", totalVenda * percentualComissaoFornecedor / 100);
                command.Parameters.AddWithValue("@PercentualComissaoVendedor", percentualComissaoVendedor);
                command.Parameters.AddWithValue("@TotalComissaoVendedor", totalVenda * percentualComissaoVendedor / 100);


                _connection.Open();
                int novoId = (int)await command.ExecuteScalarAsync(); // retorna o ID gerado
                _connection.Close();

                return novoId;
            }
        }

        public async Task AdicionarViajantes(int codVendaC, List<Models.VendaViajante> listaViajantes)
        {
            if (listaViajantes.Count > 0)
            {
                for (int i = 0; i < listaViajantes.Count; i++)
                {

                    var query = @"
        INSERT INTO VendaViajante (CodVendaC, Nome, Sobrenome, CPF, DataNascimento)
        VALUES (@CodVendaC, @Nome, @Sobrenome, @CPF, @DataNascimento);
        ";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Nome", listaViajantes[i].Nome);
                        command.Parameters.AddWithValue("@Sobrenome", listaViajantes[i].Sobrenome);
                        command.Parameters.AddWithValue("@CPF", listaViajantes[i].CPF);
                        command.Parameters.AddWithValue("@DataNascimento", listaViajantes[i].DataNascimento);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }
            }
        }

        public async Task AdicionarItens(int codVendaC, Models.Vender item)
        {
            try
            {
                var tipo = item.ItemSelecionado;

                if (tipo == "PassagemAerea")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, CompanhiaAerea, Localizador, NumeroVoo, Origem, Destino, DataVoo 
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @CompanhiaAerea, @Localizador, @NumeroVoo, @Origem, @Destino, @DataVoo
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@CompanhiaAerea", item.CompanhiaAerea);
                        command.Parameters.AddWithValue("@Localizador", item.Localizador);
                        command.Parameters.AddWithValue("@NumeroVoo", item.NumeroVoo);
                        command.Parameters.AddWithValue("@Origem", item.OrigemVoo);
                        command.Parameters.AddWithValue("@Destino", item.DestinoVoo);
                        command.Parameters.AddWithValue("@DataVoo", item.DataVoo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);


                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "DiariaHospedagem")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Hotel, Localizador, Checkin, Checkout, NumeroQuartos, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Hotel, @Localizador, @Checkin, @Checkout, @NumeroQuartos, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Hotel", item.NomeHotel);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaPacote);
                        command.Parameters.AddWithValue("@Localizador", item.LocalizacaoHotel);
                        command.Parameters.AddWithValue("@Checkin", item.CheckIn);
                        command.Parameters.AddWithValue("@Checkout", item.CheckOut);
                        command.Parameters.AddWithValue("@NumeroQuartos", item.NumeroQuartos);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "PacoteTuristico")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, NomePacote, Transporte, Emissao, Inicio, Fim, ServicoIncluso  
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @NomePacote, @Transporte, @Emissao, @Inicio, @Fim, @ServicoIncluso
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@NomePacote", item.NomePacote);
                        command.Parameters.AddWithValue("@Transporte", item.TransportePacote);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoPacote);
                        command.Parameters.AddWithValue("@Inicio", item.InicioPacote);
                        command.Parameters.AddWithValue("@Fim", item.FimPacote);
                        command.Parameters.AddWithValue("@ServicoIncluso", item.ServicosInclusosPacote);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "SeguroViagem")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Seguro, Emissao, Inicio, Fim
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Seguro, @Emissao, @Inicio, @Fim
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Seguro", item.Seguro);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoSeguro);
                        command.Parameters.AddWithValue("@Inicio", item.InicioSeguro);
                        command.Parameters.AddWithValue("@Fim", item.FimSeguro);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "AluguelVeiculo")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, CategoriaCarro, Emissao, LocalRetirada, LocalDevolucao, DataHoraRetirada, DataHoraDevolucao, ServicoIncluso, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @CategoriaCarro, @Emissao, @LocalRetirada, @LocalDevolucao, @DataHoraRetirada, @DataHoraDevolucao, @ServicoIncluso, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@CategoriaCarro", item.NomeVeiculo);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoVeiculo);
                        command.Parameters.AddWithValue("@LocalRetirada", item.LocalRetiradaVeiculo);
                        command.Parameters.AddWithValue("@LocalDevolucao", item.LocalDevolucaoVeiculo);
                        command.Parameters.AddWithValue("@DataHoraRetirada", item.DataHoraRetiradaVeiculo);
                        command.Parameters.AddWithValue("@DataHoraDevolucao", item.DataHoraDevolucaoVeiculo);
                        command.Parameters.AddWithValue("@ServicoIncluso", item.ServicosInclusosVeiculo);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaVeiculo);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Rodoviario")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Descricao, Companhia, Classe, Origem, Destino, DataHoraSaida, DataHoraChegada, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Descricao, @Companhia, @Classe, @Origem, @Destino, @DataHoraSaida, @DataHoraChegada, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Descricao", item.DescricaoRodoviario);
                        command.Parameters.AddWithValue("@Companhia", item.CompanhiaRodoviario);
                        command.Parameters.AddWithValue("@Classe", item.ClasseRodoviario);
                        command.Parameters.AddWithValue("@Origem", item.OrigemRodoviario);
                        command.Parameters.AddWithValue("@Destino", item.DestinoRodoviario);
                        command.Parameters.AddWithValue("@DataHoraSaida", item.SaidaRodoviario);
                        command.Parameters.AddWithValue("@DataHoraChegada", item.ChegadaRodoviario);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaRodoviario);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Trem")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Descricao, Companhia, Classe, Origem, Destino, DataHoraSaida, DataHoraChegada, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Descricao, @Companhia, @Classe, @Origem, @Destino, @DataHoraSaida, @DataHoraChegada, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Descricao", item.DescricaoFerroviario);
                        command.Parameters.AddWithValue("@Companhia", item.CompanhiaFerroviario);
                        command.Parameters.AddWithValue("@Classe", item.ClasseFerroviario);
                        command.Parameters.AddWithValue("@Origem", item.OrigemFerroviario);
                        command.Parameters.AddWithValue("@Destino", item.DestinoFerroviario);
                        command.Parameters.AddWithValue("@DataHoraSaida", item.SaidaFerroviario);
                        command.Parameters.AddWithValue("@DataHoraChegada", item.ChegadaFerroviario);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaFerroviario);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Cruzeiro")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Navio, Emissao, Inicio, Fim, NumeroCabine, TipoCabine, Acomodacao, Regime, Destino, ServicoIncluso, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Navio, @Emissao, @Inicio, @Fim, @NumeroCabine, @TipoCabine, @Acomodacao, @Regime, @Destino, @ServicoIncluso, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Navio", item.NavioCruzeiro);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoCruzeiro);
                        command.Parameters.AddWithValue("@Inicio", item.InicioCruzeiro);
                        command.Parameters.AddWithValue("@Fim", item.FimCruzeiro);
                        command.Parameters.AddWithValue("@NumeroCabine", item.NumeroCabineCruzeiro);
                        command.Parameters.AddWithValue("@TipoCabine", item.TipoCabineCruzeiro);
                        command.Parameters.AddWithValue("@Acomodacao", item.AcomodacaoCruzeiro);
                        command.Parameters.AddWithValue("@Regime", item.RegimeCruzeiro);
                        command.Parameters.AddWithValue("@Destino", item.DestinoCruzeiro);
                        command.Parameters.AddWithValue("@ServicoIncluso", item.ServicosInclusosCruzeiro);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaCruzeiro);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Passeio")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, NomePasseio, Emissao, Inicio, Fim, ServicoIncluso, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @NomePasseio, @Emissao, @Inicio, @Fim, @ServicoIncluso, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@NomePasseio", item.NomePasseio);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoPasseio);
                        command.Parameters.AddWithValue("@Inicio", item.InicioPasseio);
                        command.Parameters.AddWithValue("@Fim", item.FimPasseio);
                        command.Parameters.AddWithValue("@ServicoIncluso", item.ServicosInclusosPasseio);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaPasseio);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Ingresso")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, NomeIngresso, Emissao, DataUtilizacao, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @NomeIngresso, @Emissao, @DataUtilizacao, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@NomeIngresso", item.Ingresso);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoIngresso);
                        command.Parameters.AddWithValue("@DataUtilizacao", item.DataUtilizacaoIngresso);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaIngresso);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }

                if (tipo == "Outro")
                {
                    var query = @"
                INSERT INTO VendaD (
                    CodVendaC, Tipo, Valor, Descricao, TipoProduto, ServicoIncluso, Emissao, Inicio, Fim, Reserva
                ) VALUES (
                    @CodVendaC, @Tipo, @Valor, @Descricao, @TipoProduto, @ServicoIncluso, @Emissao, @Inicio, @Fim, @Reserva
                );";

                    using (SqlCommand command = new SqlCommand(query, _connection))
                    {
                        command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                        command.Parameters.AddWithValue("@Tipo", tipo);
                        command.Parameters.AddWithValue("@Valor", item.ValorItem);
                        command.Parameters.AddWithValue("@Descricao", item.DescricaoOutros);
                        command.Parameters.AddWithValue("@TipoProduto", item.TipoProdutoOutros);
                        command.Parameters.AddWithValue("@ServicoIncluso", item.ServicosInclusosOutros);
                        command.Parameters.AddWithValue("@Emissao", item.EmissaoOutros);
                        command.Parameters.AddWithValue("@Inicio", item.InicioOutros);
                        command.Parameters.AddWithValue("@Fim", item.FimOutros);
                        command.Parameters.AddWithValue("@Reserva", item.ReservaOutros);

                        _connection.Open();
                        await command.ExecuteNonQueryAsync();
                        _connection.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                var a = ex.Message;
            }
        }

        public async Task AdicionarContaReceber(int codVendaC, Models.Vender item, decimal totalVenda)
        {
            try
            {
                var tipo = item.FormaPagamento;

                var query = @"
                INSERT INTO ContaReceber (
                    CodVendaC, Tipo, NumeroCartao, NomeCartao, Validade, CVV, Parcelas, DataVencimento, ChavePIX, Valor
                ) VALUES (
                    @CodVendaC, @Tipo, @NumeroCartao, @NomeCartao, @Validade, @CVV, @Parcelas, @DataVencimento, @ChavePIX, @Valor
                );";

                using (SqlCommand command = new SqlCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@CodVendaC", codVendaC);
                    command.Parameters.AddWithValue("@Tipo", tipo);
                    command.Parameters.AddWithValue("@NumeroCartao", item.NumeroCartao);
                    command.Parameters.AddWithValue("@NomeCartao", item.NomeCartao);
                    command.Parameters.AddWithValue("@Validade", item.ValidadeCartao);
                    command.Parameters.AddWithValue("@CVV", item.CvvCartao);
                    command.Parameters.AddWithValue("@Parcelas", item.Parcelas);
                    command.Parameters.AddWithValue("@DataVencimento", item.VencimentoBoleto);
                    command.Parameters.AddWithValue("@ChavePIX", item.ChavePix);
                    command.Parameters.AddWithValue("@Valor", totalVenda);

                    _connection.Open();
                    await command.ExecuteNonQueryAsync();
                    _connection.Close();
                }


            }
            catch (Exception ex)
            {
                var a = ex.Message;
            }
        }

    }
}
