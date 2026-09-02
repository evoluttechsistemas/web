using System.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class BxClienteService
    {
        private readonly IConfiguration _config;
        private readonly UserState? _userState;

        public BxClienteService(IConfiguration config)
        {
            _config = config;
        }

        public BxClienteService(IConfiguration config, UserState userState)
        {
            _config = config;
            _userState = userState;
        }

        private string ConnectionString =>
            _config.GetConnectionString("Connection")
            ?? _config["Connection"]
            ?? throw new Exception("String de conexão 'Connection' não encontrada.");

        private int CodEmpAtual
        {
            get
            {
                if (_userState is not null && _userState.CurrentCompanyId > 0)
                    return _userState.CurrentCompanyId;

                return 2;
            }
        }

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        public async Task<List<BaixaReceberClienteDto>> BuscarClientesParaBaixaAsync(string filtro)
        {
            var lista = new List<BaixaReceberClienteDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 30
    C.Codigo,
    ISNULL(C.Nome, '') AS NomeCliente,
    ISNULL(C.Apelido, '') AS Apelido
FROM Cliente C
WHERE C.CodEmp = @CodEmp
    AND C.Codigo IN (
        SELECT DISTINCT CodCliente
        FROM ContaReceber
        WHERE CodEmp = @CodEmp
          AND CodCliente IS NOT NULL
    )
    AND (
        CAST(C.Codigo AS VARCHAR) LIKE '%' + @Filtro + '%'
        OR C.Nome LIKE '%' + @Filtro + '%'
        OR C.Apelido LIKE '%' + @Filtro + '%'
    )
ORDER BY ISNULL(NULLIF(C.Apelido,''), C.Nome);";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Filtro", filtro?.Trim() ?? "");

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new BaixaReceberClienteDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    NomeCliente = reader["NomeCliente"]?.ToString() ?? "",
                    Apelido = reader["Apelido"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        public async Task<List<ContaReceberDto>> ListarContasReceberClienteAsync(
            int codCliente,
            string situacao)
        {
            var lista = new List<ContaReceberDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var filtroSituacao = situacao switch
            {
                "abertos" => "AND CR.DataPagamento IS NULL",
                "pagos" => "AND CR.DataPagamento IS NOT NULL",
                _ => ""
            };

            var sql = $@"
SELECT
    CR.Codigo,
    ISNULL(CR.NomeTipoConta, '') AS NomeTipoConta,
    CR.DataMovimento,
    CR.DataVencimento,
    CR.NomeCliente,
    CR.Valor,
    ISNULL(CR.Parcela, '') AS Parcela,
    ISNULL(CR.TotalParcela, '') AS TotalParcela,
    ISNULL(CR.Observacao, '') AS Observacao,
    CR.DataPagamento,
    CR.ValorPago,
    ISNULL(CR.TipoPagamento, '') AS TipoPagamento,
    CR.CodCliente
FROM ContaReceber CR
WHERE CR.CodEmp = @CodEmp
  AND CR.CodCliente = @CodCliente
{filtroSituacao}
ORDER BY CR.DataVencimento ASC;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new ContaReceberDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    NomeTipoConta = reader["NomeTipoConta"]?.ToString() ?? "",
                    DataMovimento = reader["DataMovimento"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["DataMovimento"]),
                    DataVencimento = reader["DataVencimento"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["DataVencimento"]),
                    NomeCliente = reader["NomeCliente"]?.ToString(),
                    Valor = reader["Valor"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["Valor"]),
                    Parcela = reader["Parcela"]?.ToString(),
                    TotalParcela = reader["TotalParcela"]?.ToString(),
                    Observacao = reader["Observacao"]?.ToString(),
                    DataPagamento = reader["DataPagamento"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["DataPagamento"]),
                    ValorPago = reader["ValorPago"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["ValorPago"]),
                    TipoPagamento = reader["TipoPagamento"]?.ToString(),
                    CodCliente = Convert.ToInt32(reader["CodCliente"])
                });
            }

            return lista;
        }

        public async Task<List<ContaBancariaDto>> ListarContasBancariasAsync()
        {
            var lista = new List<ContaBancariaDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    Codigo,
    ISNULL(Descricao, '') AS Descricao,
    ISNULL(Agencia, '') AS Agencia,
    ISNULL(Conta, '') AS Conta,
    ISNULL(Banco, '') AS Banco
FROM ContaBancaria
WHERE CodEmp = @CodEmp
  AND ISNULL(Inativo, 'N') <> 'S'
ORDER BY Descricao;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new ContaBancariaDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    Descricao = reader["Descricao"]?.ToString() ?? "",
                    Agencia = reader["Agencia"]?.ToString() ?? "",
                    Conta = reader["Conta"]?.ToString() ?? "",
                    Banco = reader["Banco"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        public async Task<string> ExecutarBaixaReceberAsync(
            BaixaReceberInputDto input,
            string usuarioLogado)
        {
            if (!input.CodigosContaReceber.Any())
                return "Nenhum título selecionado.";

            var codigos = string.Join(",", input.CodigosContaReceber);

            var tipoPag = input.FormaPagamento switch
            {
                "Dinheiro" => "CRD",
                "Cheque à Vista" => "CRD",
                "Cheque a Prazo" => "CRD",
                "Cartão" => "CRD",
                "PIX" => "CRD",
                "Ticket" => "CRD",
                _ => "CRD"
            };

            var cheque = input.NumeroCheque ?? "";

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            using var tran = conn.BeginTransaction();

            try
            {
                var sqlBaixa = $@"
UPDATE ContaReceber
SET
    DataPagamento          = @DataPagamento,
    ValorPago              = @ValorPago,
    TipoPagamento          = @TipoPagamento,
    UsuarioUltimaGravacao  = @Usuario,
    DataHoraUltimaGravacao = GETDATE()
WHERE CodEmp = @CodEmp
  AND Codigo IN ({codigos});";

                using (var cmd = new SqlCommand(sqlBaixa, conn, tran))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@DataPagamento", input.DataPagamento);
                    cmd.Parameters.AddWithValue("@ValorPago", input.ValorTotal + input.Acrescimo);
                    cmd.Parameters.AddWithValue("@TipoPagamento", tipoPag);
                    cmd.Parameters.AddWithValue("@Usuario", usuarioLogado);

                    await cmd.ExecuteNonQueryAsync();
                }

                if (input.CodContaBancaria.HasValue)
                {
                    string descricaoOp = "ENTRADA RECEBIMENTO";

                    var sqlMov = @"
INSERT INTO MovimentacaoBancaria
(
    CodEmp,
    CodConta,
    Historico,
    CodContaPagar,
    CodigoOperacao,
    DescricaoOperacao,
    TipoCreditoDebito,
    DataMovimento,
    DataVencimento,
    Compensado,
    Estornado,
    DataCompensacao,
    SaldoAntComp,
    ValorMovimento,
    CodFornecedor,
    CodPlanoContas,
    ProvenienteDeBaixa,
    Cheque,
    CodTipoConta,
    TitulosBaixa,
    HistoricoID
)
VALUES
(
    @CodEmp,
    @CodConta,
    @Historico,
    -1,
    1,
    @DescricaoOp,
    'C',
    @DataMov,
    @DataVenc,
    'N',
    'N',
    @DataComp,
    0,
    @Valor,
    0,
    '',
    'S',
    @Cheque,
    1,
    @Titulos,
    ''
);";

                    using var cmd2 = new SqlCommand(sqlMov, conn, tran);
                    AddCodEmp(cmd2);

                    cmd2.Parameters.AddWithValue(
                        "@CodConta",
                        input.CodContaBancaria.Value);

                    var historico = (input.NomeCliente ?? "").Length > 100
                        ? input.NomeCliente!.Substring(0, 100)
                        : input.NomeCliente ?? "";

                    cmd2.Parameters.AddWithValue("@Historico", historico);
                    cmd2.Parameters.AddWithValue("@DescricaoOp", descricaoOp);
                    cmd2.Parameters.AddWithValue("@DataMov", input.DataPagamento);
                    cmd2.Parameters.AddWithValue("@DataVenc", input.DataPagamento);
                    cmd2.Parameters.AddWithValue("@DataComp", input.DataCompensacao);
                    cmd2.Parameters.AddWithValue("@Valor", input.ValorTotal + input.Acrescimo);
                    cmd2.Parameters.AddWithValue("@Cheque", cheque);
                    cmd2.Parameters.AddWithValue("@Titulos", codigos);

                    await cmd2.ExecuteNonQueryAsync();

                    var sqlCodConta = $@"
UPDATE ContaReceber
SET CodConta = @CodConta
WHERE CodEmp = @CodEmp
  AND Codigo IN ({codigos});";

                    using var cmd3 = new SqlCommand(sqlCodConta, conn, tran);
                    AddCodEmp(cmd3);
                    cmd3.Parameters.AddWithValue(
                        "@CodConta",
                        input.CodContaBancaria.Value);

                    await cmd3.ExecuteNonQueryAsync();
                }

                await tran.CommitAsync();
                return "ok";
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                return ex.Message;
            }
        }

        public async Task<List<CobrancaClienteDto>> ListarClientesCobrancaAsync()
        {
            var lista = new List<CobrancaClienteDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    rec.CodCliente,
    rec.NomeCliente,
    ISNULL(cli.Apelido, '') AS Apelido,
    ISNULL(cli.Celular, '') AS Celular,
    SUM(rec.Valor) AS ValorTotal
FROM ContaReceber rec
INNER JOIN Cliente cli
    ON rec.CodCliente = cli.Codigo
   AND rec.CodEmp = cli.CodEmp
WHERE rec.CodEmp = @CodEmp
  AND rec.TipoPagamento = 'CRD'
  AND rec.DataVencimento <= GETDATE()
  AND rec.DataPagamento IS NULL
  AND rec.ValorPago IS NULL
GROUP BY rec.CodCliente, rec.NomeCliente, cli.Apelido, cli.Celular
ORDER BY ISNULL(NULLIF(cli.Apelido,''), rec.NomeCliente);";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new CobrancaClienteDto
                {
                    CodCliente = Convert.ToInt32(reader["CodCliente"]),
                    NomeCliente = reader["NomeCliente"]?.ToString() ?? "",
                    Apelido = reader["Apelido"]?.ToString() ?? "",
                    Celular = reader["Celular"]?.ToString() ?? "",
                    ValorTotal = Convert.ToDecimal(reader["ValorTotal"])
                });
            }

            return lista;
        }

        public async Task<List<CobrancaTituloDto>> ListarTitulosCobrancaClienteAsync(int codCliente)
        {
            var lista = new List<CobrancaTituloDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    CR.Codigo,
    CR.Valor,
    CR.DataMovimento,
    CR.DataVencimento,
    ISNULL(CLI.EmiteBoleto, 'N') AS EhBoleto
FROM ContaReceber CR
INNER JOIN Cliente CLI
    ON CLI.Codigo = CR.CodCliente
   AND CLI.CodEmp = CR.CodEmp
WHERE CR.CodEmp = @CodEmp
  AND CR.TipoPagamento = 'CRD'
  AND CR.DataVencimento <= GETDATE()
  AND CR.DataPagamento IS NULL
  AND CR.ValorPago IS NULL
  AND CR.CodCliente = @CodCliente
ORDER BY CR.DataVencimento;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new CobrancaTituloDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    Valor = Convert.ToDecimal(reader["Valor"]),
                    DataMovimento = Convert.ToDateTime(reader["DataMovimento"]),
                    DataVencimento = Convert.ToDateTime(reader["DataVencimento"]),
                    EhBoleto = reader["EhBoleto"]?.ToString() == "S"
                });
            }

            return lista;
        }

        public async Task<MercadoPagoConfigDto?> ObterConfigMercadoPagoAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 1
    ISNULL(TipoPIX, '')           AS TipoPIX,
    ISNULL(EmailMercadoPago, '')  AS EmailMercadoPago,
    ISNULL(CPFMercadoPago, '')    AS CPFMercadoPago,
    ISNULL(TokenMercadoPago, '')  AS TokenMercadoPago
FROM Cliente
WHERE CodEmp = @CodEmp
  AND TipoCliente = 'PIX'
  AND TipoPIX = 'MercadoPago';";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new MercadoPagoConfigDto
                {
                    TipoPIX = reader["TipoPIX"]?.ToString() ?? "",
                    EmailMercadoPago = reader["EmailMercadoPago"]?.ToString() ?? "",
                    CPFMercadoPago = reader["CPFMercadoPago"]?.ToString() ?? "",
                    TokenMercadoPago = reader["TokenMercadoPago"]?.ToString().Trim() ?? ""
                };
            }

            return null;
        }

        public async Task<string> GerarLinkPagamentoMercadoPagoAsync(
            decimal valor,
            string descricao,
            string emailPagador,
            string cpfPagador,
            string nomePagador,
            string token)
        {
            try
            {
                MercadoPago.Config.MercadoPagoConfig.AccessToken = token;

                var request = new MercadoPago.Client.Payment.PaymentCreateRequest
                {
                    TransactionAmount = valor,
                    Description = descricao,
                    PaymentMethodId = "pix",
                    DateOfExpiration = DateTime.Now.AddHours(24),
                    Payer = new MercadoPago.Client.Payment.PaymentPayerRequest
                    {
                        Email = emailPagador,
                        FirstName = nomePagador,
                        LastName = "Cliente",
                        Identification = new MercadoPago.Client.Common.IdentificationRequest
                        {
                            Type = "CPF",
                            Number = new string(cpfPagador.Where(char.IsDigit).ToArray())
                        }
                    }
                };

                var client = new MercadoPago.Client.Payment.PaymentClient();
                var payment = await client.CreateAsync(request);

                return payment?.PointOfInteraction?.TransactionData?.TicketUrl ?? "";
            }
            catch (Exception ex)
            {
                return $"ERRO:{ex.Message}";
            }
        }
    }
}
