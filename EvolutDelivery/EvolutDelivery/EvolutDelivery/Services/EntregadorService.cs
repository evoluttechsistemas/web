using EvolutDelivery.Models;
using EvolutDelivery.Helpers;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class EntregadorService
    {
        private readonly string _connectionString;

        public EntregadorService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new ArgumentNullException("ConnectionString não configurada");
        }

        // ========== LISTAR TODOS ==========
        public async Task<List<Entregador>> ListarTodosAsync(int codEmp, bool apenasAtivos = true)
        {
            var entregadores = new List<Entregador>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var query = "SELECT * FROM Entregador WHERE CodEmp = @CodEmp";
            if (apenasAtivos)
                query += " AND Ativo = 1";

            query += " ORDER BY Nome";

            var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entregadores.Add(MapearEntregador(reader));
            }

            return entregadores;
        }

        // ========== BUSCAR DISPONÍVEIS ==========
        public async Task<List<Entregador>> BuscarDisponiveisAsync(int codEmp)
        {
            var entregadores = new List<Entregador>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
        SELECT * FROM Entregador 
        WHERE CodEmp = @CodEmp 
          AND Ativo = 1
        ORDER BY Nome", conn);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entregadores.Add(MapearEntregador(reader));
            }

            return entregadores;
        }

        // ========== ATUALIZAR STATUS ==========
        public async Task<bool> AtualizarStatusAsync(int codigo, string novoStatus)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                UPDATE Entregador 
                SET StatusAtual = @Status,
                    Disponivel = CASE WHEN @Status = 'LIVRE' THEN 1 ELSE 0 END
                WHERE Codigo = @Codigo", conn);

            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Status", novoStatus);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ========== ATUALIZAR LOCALIZAÇÃO ==========
        public async Task<bool> AtualizarLocalizacaoAsync(int codigo, string latitude, string longitude)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                UPDATE Entregador 
                SET LatitudeAtual = @Latitude,
                    LongitudeAtual = @Longitude,
                    UltimaAtualizacaoGPS = GETDATE()
                WHERE Codigo = @Codigo", conn);

            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Latitude", latitude);
            cmd.Parameters.AddWithValue("@Longitude", longitude);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ========== INCREMENTAR ENTREGAS ==========
        public async Task<bool> RegistrarEntregaConcluidaAsync(int codigo)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                UPDATE Entregador 
                SET TotalEntregas = TotalEntregas + 1,
                    StatusAtual = 'LIVRE',
                    Disponivel = 1
                WHERE Codigo = @Codigo", conn);

            cmd.Parameters.AddWithValue("@Codigo", codigo);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ========== SALVAR/ATUALIZAR ==========
        public async Task<int> SalvarAsync(Entregador entregador)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            if (entregador.Codigo > 0)
            {
                // Atualizar
                var cmd = new SqlCommand(@"
                    UPDATE Entregador SET
                        Nome = @Nome,
                        Telefone = @Telefone,
                        Email = @Email,
                        CPF = @CPF,
                        TipoVeiculo = @TipoVeiculo,
                        PlacaVeiculo = @PlacaVeiculo,
                        CorVeiculo = @CorVeiculo,
                        Ativo = @Ativo,
                        Disponivel = @Disponivel
                    WHERE Codigo = @Codigo", conn);

                cmd.Parameters.AddWithValue("@Codigo", entregador.Codigo);
                AdicionarParametros(cmd, entregador);

                await cmd.ExecuteNonQueryAsync();
                return entregador.Codigo;
            }
            else
            {
                // Inserir
                var cmd = new SqlCommand(@"
                    INSERT INTO Entregador 
                    (Nome, Telefone, Email, CPF, TipoVeiculo, PlacaVeiculo, CorVeiculo, 
                     Ativo, Disponivel, StatusAtual, DataCadastro, CodEmp)
                    VALUES 
                    (@Nome, @Telefone, @Email, @CPF, @TipoVeiculo, @PlacaVeiculo, @CorVeiculo,
                     @Ativo, @Disponivel, 'LIVRE', GETDATE(), @CodEmp);
                    SELECT SCOPE_IDENTITY();", conn);

                AdicionarParametros(cmd, entregador);
                cmd.Parameters.AddWithValue("@CodEmp", entregador.CodEmp);

                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
        }

        // ========== MAPEAMENTO ==========
        private Entregador MapearEntregador(SqlDataReader reader)
        {
            return new Entregador
            {
                Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                Nome = reader["Nome"]?.ToString() ?? "",
                Telefone = reader["Telefone"]?.ToString() ?? "",
                Email = reader["Email"] == DBNull.Value ? "" : reader["Email"].ToString() ?? "",
                CPF = reader["CPF"] == DBNull.Value ? "" : reader["CPF"].ToString() ?? "",
                TipoVeiculo = reader["TipoVeiculo"] == DBNull.Value ? "" : reader["TipoVeiculo"].ToString() ?? "",
                PlacaVeiculo = reader["PlacaVeiculo"] == DBNull.Value ? null : reader["PlacaVeiculo"].ToString(),
                CorVeiculo = reader["CorVeiculo"] == DBNull.Value ? null : reader["CorVeiculo"].ToString(),
                Ativo = reader["Ativo"] != DBNull.Value && Convert.ToBoolean(reader["Ativo"]),
                CodEmp = reader["CodEmp"] == DBNull.Value ? 0 : Convert.ToInt32(reader["CodEmp"]),

                Disponivel = true,
                StatusAtual = "LIVRE",
                LatitudeAtual = null,
                LongitudeAtual = null,
                UltimaAtualizacaoGPS = null,
                TotalEntregas = 0,
                AvaliacaoMedia = 0,
                TotalAvaliacoes = 0,
                DataCadastro = DateTime.Now
            };
        }

        private void AdicionarParametros(SqlCommand cmd, Entregador entregador)
        {
            cmd.Parameters.AddWithValue("@Nome", entregador.Nome);
            cmd.Parameters.AddWithValue("@Telefone", entregador.Telefone);
            cmd.Parameters.AddWithValue("@Email", entregador.Email ?? "");
            cmd.Parameters.AddWithValue("@CPF", entregador.CPF ?? "");
            cmd.Parameters.AddWithValue("@TipoVeiculo", entregador.TipoVeiculo);
            cmd.Parameters.AddWithValue("@PlacaVeiculo", entregador.PlacaVeiculo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CorVeiculo", entregador.CorVeiculo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Ativo", entregador.Ativo);
            cmd.Parameters.AddWithValue("@Disponivel", entregador.Disponivel);
        }
    }
}
