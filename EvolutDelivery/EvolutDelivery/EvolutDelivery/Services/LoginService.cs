using System.Data;
using EvolutDelivery.Models;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Services
{
    public class LoginService
    {
        private readonly string _connectionString;
        private readonly EmpresaService _empresaService;
        private readonly ILogger<LoginService> _logger;

        public LoginService(IConfiguration config, EmpresaService empresaService, ILogger<LoginService> logger)
        {
            _logger = logger;
            _connectionString = config.GetConnectionString("ConnectionComercial")
                ?? throw new InvalidOperationException("Connection string 'ConnectionComercial' não encontrada.");

            _empresaService = empresaService;
        }

        public async Task<UsuarioLoginModel?> ValidarLoginAsync(string cnpj, string usuario, string senha, int codigoEmpresa)
        {
            if (codigoEmpresa <= 0 || string.IsNullOrWhiteSpace(cnpj) || string.IsNullOrWhiteSpace(usuario))
                return null;

            // A empresa escolhida precisa pertencer ao CNPJ do usuário
            if (!await EmpresaPertenceAoCnpjAsync(codigoEmpresa, cnpj))
                return null;

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

                try
                {
                    await _empresaService.GarantirSlugAsync(codigoEmpresa, cnpj);
                }
                catch (Exception ex)
                {
                    // O slug é só para o link da loja: não pode impedir o login
                    _logger.LogWarning(ex, "Falha ao garantir o slug da empresa {CodEmp}", codigoEmpresa);
                }

                return usuarioLogado;
            }

            return null;
        }

        private async Task<bool> EmpresaPertenceAoCnpjAsync(int codigoEmpresa, string cnpj)
        {
            const string sql = @"
        SELECT COUNT(1)
        FROM Empresa
        WHERE Codigo = @Codigo
          AND CNPJ = @CNPJ";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigoEmpresa;
            cmd.Parameters.Add("@CNPJ", SqlDbType.VarChar).Value = cnpj;

            await conn.OpenAsync();
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
    }
}