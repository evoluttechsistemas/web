using EvolutDelivery.Helpers;
using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class EmpresaService
    {
        private readonly string _connectionString;

        public EmpresaService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConnectionComercial");
        }

        public async Task<EmpresaModel?> BuscarPorSlugAsync(string slug)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT TOP 1 Codigo, Fantasia, CNPJ, Slug
                FROM Empresa
                WHERE Slug = @Slug";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Slug", slug);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new EmpresaModel
                {
                    Codigo = reader["Codigo"] != DBNull.Value ? Convert.ToInt32(reader["Codigo"]) : 0,
                    Fantasia = reader["Fantasia"]?.ToString() ?? string.Empty,
                    CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty,
                    Slug = reader["Slug"]?.ToString() ?? string.Empty
                };
            }

            return null;
        }

        public async Task<EmpresaModel?> BuscarPorCodigoAsync(int codigo)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT TOP 1 Codigo, Fantasia, CNPJ, Slug
                FROM Empresa
                WHERE Codigo = @Codigo";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new EmpresaModel
                {
                    Codigo = reader["Codigo"] != DBNull.Value ? Convert.ToInt32(reader["Codigo"]) : 0,
                    Fantasia = reader["Fantasia"]?.ToString() ?? string.Empty,
                    CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty,
                    Slug = reader["Slug"]?.ToString() ?? string.Empty
                };
            }

            return null;
        }

        public async Task<string> GerarSlugUnicoAsync(string fantasia)
        {
            var slugBase = SlugHelper.GerarSlugBase(fantasia);
            var slugFinal = slugBase;
            var contador = 2;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            while (true)
            {
                var sql = "SELECT COUNT(1) FROM Empresa WHERE Slug = @Slug";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Slug", slugFinal);

                var total = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (total == 0)
                    return slugFinal;

                slugFinal = $"{slugBase}-{contador}";
                contador++;
            }
        }

        public async Task<string> GarantirSlugAsync(int codigoEmpresa, string cnpj)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sqlBuscar = @"
        SELECT TOP 1 Codigo, Fantasia, CNPJ, Slug
        FROM Empresa
        WHERE Codigo = @Codigo
          AND CNPJ = @CNPJ";

            using var cmdBuscar = new SqlCommand(sqlBuscar, conn);
            cmdBuscar.Parameters.AddWithValue("@Codigo", codigoEmpresa);
            cmdBuscar.Parameters.AddWithValue("@CNPJ", cnpj);

            using var reader = await cmdBuscar.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new Exception("Empresa não encontrada.");

            var slugAtual = reader["Slug"]?.ToString() ?? string.Empty;
            var fantasia = reader["Fantasia"]?.ToString() ?? string.Empty;

            reader.Close();

            if (!string.IsNullOrWhiteSpace(slugAtual))
                return slugAtual;

            var slugBase = SlugHelper.GerarSlugBase(fantasia);
            var slugFinal = slugBase;
            var contador = 2;

            while (true)
            {
                var sqlExiste = "SELECT COUNT(1) FROM Empresa WHERE Slug = @Slug";
                using var cmdExiste = new SqlCommand(sqlExiste, conn);
                cmdExiste.Parameters.AddWithValue("@Slug", slugFinal);

                var total = Convert.ToInt32(await cmdExiste.ExecuteScalarAsync());

                if (total == 0)
                    break;

                slugFinal = $"{slugBase}-{contador}";
                contador++;
            }

            var sqlUpdate = @"
        UPDATE Empresa
        SET Slug = @Slug
        WHERE Codigo = @Codigo
          AND CNPJ = @CNPJ";

            using var cmdUpdate = new SqlCommand(sqlUpdate, conn);
            cmdUpdate.Parameters.AddWithValue("@Slug", slugFinal);
            cmdUpdate.Parameters.AddWithValue("@Codigo", codigoEmpresa);
            cmdUpdate.Parameters.AddWithValue("@CNPJ", cnpj);

            await cmdUpdate.ExecuteNonQueryAsync();

            return slugFinal;
        }
    }
}