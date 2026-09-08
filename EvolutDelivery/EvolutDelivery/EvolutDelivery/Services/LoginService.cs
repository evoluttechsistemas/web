using System.Data;
using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class LoginService
    {
        private readonly string _connectionString;
        private readonly EmpresaService _empresaService;

        public LoginService(IConfiguration config, EmpresaService empresaService)
        {
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new InvalidOperationException("Connection string 'ConnectionComercial' não encontrada.");

            _empresaService = empresaService;
        }

        public async Task<UsuarioLoginModel?> ValidarLoginAsync(string cnpj, string usuario, string senha, int codigoEmpresa)
        {
            const string query = @"
        SELECT TOP 1
            Codigo,
            Usuario,
            Senha,
            CodVendedor,
            CNPJ
        FROM Usuario
        WHERE CNPJ = @CNPJ
          AND Usuario = @Usuario
          AND Senha = @Senha";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(query, conn);

            cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar).Value = usuario;
            cmd.Parameters.Add("@Senha", SqlDbType.VarChar).Value = senha;

            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var usuarioLogado = new UsuarioLoginModel
                {
                    Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                    Usuario = reader["Usuario"]?.ToString() ?? string.Empty,
                    Senha = reader["Senha"]?.ToString() ?? string.Empty,
                    CodVendedor = reader["CodVendedor"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["CodVendedor"]),
                    CNPJ = reader["CNPJ"]?.ToString() ?? string.Empty
                };

                reader.Close();

                await _empresaService.GarantirSlugAsync(codigoEmpresa, cnpj);

                return usuarioLogado;
            }

            return null;
        }
    }
}