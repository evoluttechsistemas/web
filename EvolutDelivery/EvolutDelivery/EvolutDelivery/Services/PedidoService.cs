using EvolutDelivery.Helpers;
using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace EvolutDelivery.Services
{
    public class PedidoService
    {
        private readonly string _connectionString;

        public PedidoService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException("ConnectionString não configurada");
        }

        // =========================
        // CRIAR PEDIDO
        // =========================
        public async Task<int> CriarPedidoAsync(Pedido pedido, List<ItemCarrinho> itens)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var transaction = conn.BeginTransaction();

            try
            {
                int codVenda = await InserirVendaCAFVAsync(conn, transaction, pedido);

                await InserirVendaDAFVAsync(conn, transaction, itens, codVenda, pedido.CNPJ);

                int codPedidoDelivery = await InserirPedidoDeliveryAsync(conn, transaction, pedido, codVenda);

                await transaction.CommitAsync();

                return codPedidoDelivery;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine("Erro ao criar pedido: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        // =========================
        // LISTAR PEDIDOS
        // =========================
        public async Task<List<Pedido>> ListarPedidosAsync(int codEmp, string? status = null, DateTime? dataInicio = null, DateTime? dataFim = null)
        {
            var pedidos = new List<Pedido>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    p.Codigo,
                    p.CodVenda,
                    p.CodCliente,
                    p.NomeCliente,
                    p.TelefoneCliente,
                    p.EmailCliente,
                    p.EnderecoCompleto,
                    p.Bairro,
                    p.Cidade,
                    p.CEP,
                    p.Complemento,
                    p.Numero,
                    p.Referencia,
                    p.DataPedido,
                    p.DataEntrega,
                    p.ValorSubtotal,
                    p.ValorEntrega,
                    p.ValorDesconto,
                    p.ValorTotal,
                    p.Status,
                    p.FormaPagamento,
                    p.TipoEntrega,
                    p.Observacao,
                    p.CodEntregador,
                    p.LatitudeEntrega,
                    p.LongitudeEntrega,
                    p.UltimaAtualizacaoLocalizacao,
                    p.Usuario,
                    p.CodEmp,
                    p.Notificado,
                    ISNULL(e.Nome, '') AS NomeEntregador
                FROM PedidoDelivery p
                LEFT JOIN Entregador e ON e.Codigo = p.CodEntregador
                WHERE p.CodEmp = @CodEmp";

            if (!string.IsNullOrWhiteSpace(status))
                sql += " AND p.Status = @Status";

            if (dataInicio.HasValue)
                sql += " AND p.DataPedido >= @DataInicio";

            if (dataFim.HasValue)
                sql += " AND p.DataPedido <= @DataFim";

            sql += " ORDER BY p.DataPedido DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            if (!string.IsNullOrWhiteSpace(status))
                cmd.Parameters.AddWithValue("@Status", status);

            if (dataInicio.HasValue)
                cmd.Parameters.AddWithValue("@DataInicio", dataInicio.Value);

            if (dataFim.HasValue)
                cmd.Parameters.AddWithValue("@DataFim", dataFim.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pedidos.Add(MapearPedido(reader));
            }

            return pedidos;
        }

        public async Task<List<Pedido>> ListarPedidosPorTelefoneAsync(int codEmp, string cnpj, string telefone)
        {
            var pedidos = new List<Pedido>();
            string telefoneNormalizado = new string((telefone ?? "").Where(char.IsDigit).ToArray());

            if (codEmp <= 0 || string.IsNullOrWhiteSpace(cnpj) || string.IsNullOrWhiteSpace(telefoneNormalizado))
            {
                return pedidos;
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    p.Codigo,
                    p.CodVenda,
                    p.CodCliente,
                    p.NomeCliente,
                    p.TelefoneCliente,
                    p.EmailCliente,
                    p.EnderecoCompleto,
                    p.Bairro,
                    p.Cidade,
                    p.CEP,
                    p.Complemento,
                    p.Numero,
                    p.Referencia,
                    p.DataPedido,
                    p.DataEntrega,
                    p.ValorSubtotal,
                    p.ValorEntrega,
                    p.ValorDesconto,
                    p.ValorTotal,
                    p.Status,
                    p.FormaPagamento,
                    p.TipoEntrega,
                    p.Observacao,
                    p.CodEntregador,
                    p.LatitudeEntrega,
                    p.LongitudeEntrega,
                    p.UltimaAtualizacaoLocalizacao,
                    p.Usuario,
                    p.CodEmp,
                    p.Notificado,
                    ISNULL(e.Nome, '') AS NomeEntregador
                FROM PedidoDelivery p
                LEFT JOIN Entregador e ON e.Codigo = p.CodEntregador
                WHERE p.CodEmp = @CodEmp
                  AND EXISTS
                  (
                      SELECT 1
                      FROM VendaCAFV v
                      WHERE v.Codigo = p.CodVenda
                        AND v.CNPJ = @CNPJ
                  )
                  AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(p.TelefoneCliente, ''), '(', ''), ')', ''), '-', ''), ' ', ''), '.', '') = @Telefone
                ORDER BY p.DataPedido DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@CNPJ", cnpj);
            cmd.Parameters.AddWithValue("@Telefone", telefoneNormalizado);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pedidos.Add(MapearPedido(reader));
            }

            foreach (var pedido in pedidos)
            {
                pedido.Itens = await BuscarItensVendaAsync(pedido.CodVenda);
            }

            return pedidos;
        }

        // =========================
        // BUSCAR PEDIDO POR ID
        // =========================
        public async Task<Pedido?> BuscarPorIdAsync(int codigo)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    p.Codigo,
                    p.CodVenda,
                    p.CodCliente,
                    p.NomeCliente,
                    p.TelefoneCliente,
                    p.EmailCliente,
                    p.EnderecoCompleto,
                    p.Bairro,
                    p.Cidade,
                    p.CEP,
                    p.Complemento,
                    p.Numero,
                    p.Referencia,
                    p.DataPedido,
                    p.DataEntrega,
                    p.ValorSubtotal,
                    p.ValorEntrega,
                    p.ValorDesconto,
                    p.ValorTotal,
                    p.Status,
                    p.FormaPagamento,
                    p.TipoEntrega,
                    p.Observacao,
                    p.CodEntregador,
                    p.LatitudeEntrega,
                    p.LongitudeEntrega,
                    p.UltimaAtualizacaoLocalizacao,
                    p.Usuario,
                    p.CodEmp,
                    p.Notificado,
                    ISNULL(e.Nome, '') AS NomeEntregador
                FROM PedidoDelivery p
                LEFT JOIN Entregador e ON e.Codigo = p.CodEntregador
                WHERE p.Codigo = @Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var pedido = MapearPedido(reader);
            reader.Close();

            pedido.Itens = await BuscarItensVendaAsync(pedido.CodVenda);

            return pedido;
        }

        // =========================
        // BUSCAR ITENS DA VENDA
        // =========================
        private async Task<List<ItemPedido>> BuscarItensVendaAsync(int codVenda)
        {
            var itens = new List<ItemPedido>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
        SELECT 
            vd.Codigo,
            vd.CodVendaC,
            vd.CodProduto,
            ISNULL(prod.Descricao, ISNULL(vd.NomeProduto, '')) AS NomeProduto,
            vd.Quantidade,
            vd.PrecoUnitario,
            vd.PrecoTotal,
            CAST(NULL AS NVARCHAR(300)) AS Observacao,
            prod.Imagem
        FROM VendaDAFV vd
        OUTER APPLY
        (
            SELECT TOP 1
                p.Descricao,
                p.Imagem
            FROM ProdutoAFV p
            WHERE p.Codigo = vd.CodProduto
              AND p.CNPJ = vd.CNPJ
            ORDER BY p.Descricao
        ) prod
        WHERE vd.CodVendaC = @CodVenda
        ORDER BY vd.Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodVenda", codVenda);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                itens.Add(new ItemPedido
                {
                    Codigo = reader.GetInt32("Codigo"),
                    CodVenda = reader.GetInt32("CodVendaC"),
                    CodProduto = reader.GetInt32("CodProduto"),
                    NomeProduto = reader.GetStringOrEmpty("NomeProduto"),
                    Quantidade = Convert.ToDecimal(reader["Quantidade"]),
                    PrecoUnitario = Convert.ToDecimal(reader["PrecoUnitario"]),
                    PrecoTotal = Convert.ToDecimal(reader["PrecoTotal"]),
                    Observacao = reader.GetStringOrEmpty("Observacao"),
                    ImagemProduto = reader["Imagem"] == DBNull.Value ? null : (byte[])reader["Imagem"]
                });
            }

            return itens;
        }

        // =========================
        // ATUALIZAR STATUS
        // =========================
        public async Task<bool> AtualizarStatusAsync(int codigo, string novoStatus, int? codEntregador = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                UPDATE PedidoDelivery
                SET Status = @Status,
                    CodEntregador = CASE 
                        WHEN @CodEntregador IS NOT NULL THEN @CodEntregador 
                        ELSE CodEntregador 
                    END,
                    DataEntrega = CASE 
                        WHEN @Status = 'ENTREGUE' THEN GETDATE() 
                        ELSE DataEntrega 
                    END
                WHERE Codigo = @Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Status", novoStatus);
            cmd.Parameters.AddWithValue("@CodEntregador", (object?)codEntregador ?? DBNull.Value);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =========================
        // CANCELAR PEDIDO
        // =========================
        public async Task<bool> CancelarPedidoAsync(int codigo, string motivo)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                UPDATE PedidoDelivery
                SET Status = 'CANCELADO',
                    Observacao = 
                        CASE 
                            WHEN ISNULL(Observacao, '') = '' THEN 'CANCELADO: ' + @Motivo
                            ELSE Observacao + ' | CANCELADO: ' + @Motivo
                        END
                WHERE Codigo = @Codigo
                  AND Status IN ('PENDENTE', 'CONFIRMADO')";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Motivo", motivo);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // =========================
        // ESTATÍSTICAS
        // =========================
        public async Task<Dictionary<string, int>> ObterEstatisticasAsync(int codEmp, DateTime? data = null)
        {
            var stats = new Dictionary<string, int>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var dataFiltro = (data ?? DateTime.Today).Date;

            var sql = @"
                SELECT
                    COUNT(CASE WHEN Status = 'PENDENTE' THEN 1 END) AS Pendentes,
                    COUNT(CASE WHEN Status = 'CONFIRMADO' THEN 1 END) AS Confirmados,
                    COUNT(CASE WHEN Status = 'PREPARANDO' THEN 1 END) AS Preparando,
                    COUNT(CASE WHEN Status = 'SAIU_ENTREGA' THEN 1 END) AS EmEntrega,
                    COUNT(CASE WHEN Status = 'ENTREGUE' THEN 1 END) AS Entregues,
                    COUNT(CASE WHEN Status = 'CANCELADO' THEN 1 END) AS Cancelados
                FROM PedidoDelivery
                WHERE CodEmp = @CodEmp
                  AND CAST(DataPedido AS DATE) = @Data";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Data", dataFiltro);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                stats["Pendentes"] = Convert.ToInt32(reader["Pendentes"]);
                stats["Confirmados"] = Convert.ToInt32(reader["Confirmados"]);
                stats["Preparando"] = Convert.ToInt32(reader["Preparando"]);
                stats["EmEntrega"] = Convert.ToInt32(reader["EmEntrega"]);
                stats["Entregues"] = Convert.ToInt32(reader["Entregues"]);
                stats["Cancelados"] = Convert.ToInt32(reader["Cancelados"]);
            }

            return stats;
        }

        // =========================
        // MÉTODOS PRIVADOS
        // =========================
        private async Task<int> GerarNovoCodigoVendaAsync(SqlConnection conn, SqlTransaction transaction, int codEmp)
        {
            var sql = @"
                SELECT ISNULL(MAX(Codigo), 0) + 1
                FROM VendaCAFV
                WHERE CodEmp = @CodEmp";

            using var cmd = new SqlCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InserirVendaCAFVAsync(SqlConnection conn, SqlTransaction transaction, Pedido pedido)
        {
            var sql = @"
INSERT INTO VendaCAFV
(
    CodCliente,
    FormaPagamento,
    EntregaRetira,
    TotalVenda,
    CodEmp,
    Observacao,
    DataHoraMovimento,
    Usuario,
    NomeCliente,
    Status,
    CNPJ,
    DataMovimento,
    EnderecoCompleto,
    Bairro,
    Cidade,
    CEP,
    Numero,
    Complemento,
    Referencia
)
VALUES
(
    @CodCliente,
    @FormaPagamento,
    @EntregaRetira,
    @TotalVenda,
    @CodEmp,
    @Observacao,
    GETDATE(),
    @Usuario,
    @NomeCliente,
    'P',
    @CNPJ,
    GETDATE(),
    @EnderecoCompleto,
    @Bairro,
    @Cidade,
    @CEP,
    @Numero,
    @Complemento,
    @Referencia
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, conn, transaction);

            cmd.Parameters.AddWithValue("@CodCliente", pedido.CodCliente);
            cmd.Parameters.AddWithValue("@FormaPagamento", pedido.FormaPagamento ?? "");
            cmd.Parameters.AddWithValue("@EntregaRetira", pedido.TipoEntrega == "ENTREGA" ? "E" : "R");
            cmd.Parameters.AddWithValue("@TotalVenda", pedido.ValorTotal);
            cmd.Parameters.AddWithValue("@CodEmp", pedido.CodEmp);
            cmd.Parameters.AddWithValue("@Observacao", pedido.Observacao ?? "");
            cmd.Parameters.AddWithValue("@Usuario", pedido.Usuario ?? "SITE");
            cmd.Parameters.AddWithValue("@NomeCliente", pedido.NomeCliente ?? "");
            cmd.Parameters.AddWithValue("@CNPJ", pedido.CNPJ ?? "");

            cmd.Parameters.AddWithValue("@EnderecoCompleto", pedido.EnderecoCompleto ?? "");
            cmd.Parameters.AddWithValue("@Bairro", pedido.Bairro ?? "");
            cmd.Parameters.AddWithValue("@Cidade", pedido.Cidade ?? "");
            cmd.Parameters.AddWithValue("@CEP", pedido.CEP ?? "");
            cmd.Parameters.AddWithValue("@Numero", pedido.Numero ?? "");
            cmd.Parameters.AddWithValue("@Complemento", pedido.Complemento ?? "");
            cmd.Parameters.AddWithValue("@Referencia", pedido.Referencia ?? "");

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task InserirVendaDAFVAsync(SqlConnection conn, SqlTransaction transaction, List<ItemCarrinho> itens, int codVenda, string cnpj)
        {
            foreach (var item in itens)
            {
                var sql = @"
        INSERT INTO VendaDAFV
(
    CodVendaC,
    CodProduto,
    NomeProduto,
    Quantidade,
    PrecoUnitario,
    PrecoTotal,
    CNPJ
)
        VALUES
        (
            @CodVendaC,
            @CodProduto,
            @NomeProduto,
            @Quantidade,
            @PrecoUnitario,
            @PrecoTotal,
            @CNPJ
        )";

                using var cmd = new SqlCommand(sql, conn, transaction);

                cmd.Parameters.AddWithValue("@CodVendaC", codVenda);
                cmd.Parameters.AddWithValue("@CodProduto", item.Codigo);
                cmd.Parameters.AddWithValue("@NomeProduto", item.Nome ?? "");
                cmd.Parameters.AddWithValue("@Quantidade", item.Quantidade);
                cmd.Parameters.AddWithValue("@PrecoUnitario", item.Preco);
                cmd.Parameters.AddWithValue("@PrecoTotal", item.Preco * item.Quantidade);
                cmd.Parameters.AddWithValue("@CNPJ", cnpj ?? "");

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<int> InserirPedidoDeliveryAsync(SqlConnection conn, SqlTransaction transaction, Pedido pedido, int codVenda)
        {
            var sql = @"
                INSERT INTO PedidoDelivery
                (
                    CodVenda,
                    CodCliente,
                    CodEmp,
                    NomeCliente,
                    TelefoneCliente,
                    EmailCliente,
                    EnderecoCompleto,
                    Bairro,
                    Cidade,
                    CEP,
                    Complemento,
                    Numero,
                    Referencia,
                    DataPedido,
                    ValorSubtotal,
                    ValorEntrega,
                    ValorDesconto,
                    ValorTotal,
                    Status,
                    FormaPagamento,
                    TipoEntrega,
                    Observacao,
                    CodEntregador,
                    LatitudeEntrega,
                    LongitudeEntrega,
                    UltimaAtualizacaoLocalizacao,
                    Usuario,
                    Notificado
                )
                VALUES
                (
                    @CodVenda,
                    @CodCliente,
                    @CodEmp,
                    @NomeCliente,
                    @TelefoneCliente,
                    @EmailCliente,
                    @EnderecoCompleto,
                    @Bairro,
                    @Cidade,
                    @CEP,
                    @Complemento,
                    @Numero,
                    @Referencia,
                    GETDATE(),
                    @ValorSubtotal,
                    @ValorEntrega,
                    @ValorDesconto,
                    @ValorTotal,
                    @Status,
                    @FormaPagamento,
                    @TipoEntrega,
                    @Observacao,
                    @CodEntregador,
                    @LatitudeEntrega,
                    @LongitudeEntrega,
                    @UltimaAtualizacaoLocalizacao,
                    @Usuario,
                    @Notificado
                );

                SELECT SCOPE_IDENTITY();";

            using var cmd = new SqlCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@CodVenda", codVenda);
            cmd.Parameters.AddWithValue("@CodCliente", pedido.CodCliente);
            cmd.Parameters.AddWithValue("@CodEmp", pedido.CodEmp);
            cmd.Parameters.AddWithValue("@NomeCliente", pedido.NomeCliente);
            cmd.Parameters.AddWithValue("@TelefoneCliente", pedido.TelefoneCliente);
            cmd.Parameters.AddWithValue("@EmailCliente", (object?)pedido.EmailCliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EnderecoCompleto", (object?)pedido.EnderecoCompleto ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Bairro", (object?)pedido.Bairro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cidade", (object?)pedido.Cidade ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CEP", (object?)pedido.CEP ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Complemento", (object?)pedido.Complemento ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Numero", (object?)pedido.Numero ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Referencia", (object?)pedido.Referencia ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ValorSubtotal", pedido.ValorSubtotal);
            cmd.Parameters.AddWithValue("@ValorEntrega", pedido.ValorEntrega);
            cmd.Parameters.AddWithValue("@ValorDesconto", pedido.ValorDesconto);
            cmd.Parameters.AddWithValue("@ValorTotal", pedido.ValorTotal);
            cmd.Parameters.AddWithValue("@Status", pedido.Status ?? "PENDENTE");
            cmd.Parameters.AddWithValue("@FormaPagamento", pedido.FormaPagamento);
            cmd.Parameters.AddWithValue("@TipoEntrega", pedido.TipoEntrega);
            cmd.Parameters.AddWithValue("@Observacao", (object?)pedido.Observacao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CodEntregador", (object?)pedido.CodEntregador ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LatitudeEntrega", (object?)pedido.LatitudeEntrega ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LongitudeEntrega", (object?)pedido.LongitudeEntrega ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UltimaAtualizacaoLocalizacao", (object?)pedido.UltimaAtualizacaoLocalizacao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Usuario", pedido.Usuario ?? "SITE");
            cmd.Parameters.AddWithValue("@Notificado", pedido.Notificado);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private Pedido MapearPedido(SqlDataReader reader)
        {
            return new Pedido
            {
                Codigo = Convert.ToInt32(reader["Codigo"]),
                CodVenda = Convert.ToInt32(reader["CodVenda"]),
                CodCliente = Convert.ToInt32(reader["CodCliente"]),
                NomeCliente = reader.GetStringOrEmpty("NomeCliente"),
                TelefoneCliente = reader.GetStringOrEmpty("TelefoneCliente"),
                EmailCliente = reader.GetStringOrEmpty("EmailCliente"),
                EnderecoCompleto = reader.GetStringOrEmpty("EnderecoCompleto"),
                Bairro = reader.GetStringOrEmpty("Bairro"),
                Cidade = reader.GetStringOrEmpty("Cidade"),
                CEP = reader.GetStringOrEmpty("CEP"),
                Complemento = reader.GetStringOrEmpty("Complemento"),
                Numero = reader.GetStringOrEmpty("Numero"),
                Referencia = reader.GetStringOrEmpty("Referencia"),
                DataPedido = reader["DataPedido"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["DataPedido"]),
                DataEntrega = reader.GetDateTimeOrNull("DataEntrega"),
                ValorSubtotal = reader["ValorSubtotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ValorSubtotal"]),
                ValorEntrega = reader["ValorEntrega"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ValorEntrega"]),
                ValorDesconto = reader["ValorDesconto"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ValorDesconto"]),
                ValorTotal = reader["ValorTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ValorTotal"]),
                Status = reader.GetStringOrEmpty("Status"),
                FormaPagamento = reader.GetStringOrEmpty("FormaPagamento"),
                TipoEntrega = reader.GetStringOrEmpty("TipoEntrega"),
                Observacao = reader.GetStringOrEmpty("Observacao"),
                CodEntregador = reader.GetInt32OrNull("CodEntregador"),
                NomeEntregador = reader.GetStringOrEmpty("NomeEntregador"),
                LatitudeEntrega = reader.GetStringOrEmpty("LatitudeEntrega"),
                LongitudeEntrega = reader.GetStringOrEmpty("LongitudeEntrega"),
                UltimaAtualizacaoLocalizacao = reader.GetDateTimeOrNull("UltimaAtualizacaoLocalizacao"),
                Usuario = reader.GetStringOrEmpty("Usuario"),
                CodEmp = reader["CodEmp"] == DBNull.Value ? 0 : Convert.ToInt32(reader["CodEmp"]),
                Notificado = reader["Notificado"] != DBNull.Value && Convert.ToBoolean(reader["Notificado"])
            };
        }
    }
}
