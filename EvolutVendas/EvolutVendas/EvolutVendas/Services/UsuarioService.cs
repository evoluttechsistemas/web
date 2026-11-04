using Microsoft.Data.SqlClient;

namespace EvolutVendas.Services
{
    public class UsuarioService
    {
        private readonly string _connectionString;

        public UsuarioService(IConfiguration config)
        {
            // Pega a string de conexão do appsettings.json
            _connectionString = config.GetConnectionString("ConnectionComercial");
        }

        public async Task<List<Models.UsuarioLogin>> BuscarUsuarios(string cnpj, string usuario, string senha)
        {
            var lista = new List<Models.UsuarioLogin>();

            cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "").Replace(",", "");

            const string query = @"SELECT Codigo, CodVendedor, Usuario, Senha, CNPJ
                                   FROM Usuario
                                   WHERE Usuario = @Usuario AND Senha = @Senha AND CNPJ = @CNPJ";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@Senha", senha);
            cmd.Parameters.AddWithValue("@CNPJ", cnpj);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Models.UsuarioLogin
                {
                    Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                    CodVendedor = reader.GetInt32(reader.GetOrdinal("CodVendedor")),
                    Usuario = reader["Usuario"].ToString(),
                    Senha = reader["Senha"].ToString(),
                    CNPJ = reader["CNPJ"].ToString()
                });
            }

            return lista;
        }
    }
}
