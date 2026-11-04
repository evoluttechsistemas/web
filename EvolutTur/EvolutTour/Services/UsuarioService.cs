using EvolutTur.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvolutTur.Services
{
    public class UsuarioService
    {
        private readonly SqlConnection _connection;

        private readonly MasterDbContext _context;
       

        public UsuarioService(SqlConnection connection, IConfiguration configuration)
        {
            _connection = connection;            
        }

        public async Task<List<Models.UsuarioMaster>> BuscarUsuarios(int CodEmp, string filtroBusca)
        {
            string criterioBusca = "";
            if (filtroBusca != "")
                criterioBusca = $" and Nome like '%{filtroBusca}%'";

            var query = $"SELECT top 10 Codigo, Nome, Usuario, Senha, isnull(PermInserir,'N') PermInserir, isnull(PermAlterar,'N') PermAlterar, isnull(PermExcluir,'N') PermExcluir, isnull(Master,'N') Master FROM UsuarioMaster where CodEmpresaMaster = @CodEmpresaMaster {criterioBusca}";
            List<Models.UsuarioMaster> set = new List<Models.UsuarioMaster>();

            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@CodEmpresaMaster", CodEmp);
                command.Parameters.AddWithValue("@FiltroBusca", criterioBusca);

                _connection.Open();
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Models.UsuarioMaster cli = new Models.UsuarioMaster();

                        cli.Codigo = (int)reader["Codigo"];
                        cli.Nome = reader["Nome"].ToString();
                        cli.Usuario = reader["Usuario"].ToString();
                        cli.PermInserir = reader["PermInserir"].ToString();
                        cli.PermAlterar = reader["PermAlterar"].ToString();
                        cli.PermExcluir = reader["PermExcluir"].ToString();
                        cli.Master = reader["Master"].ToString();

                        set.Add(cli);
                    }
                }
                _connection.Close();
            }

            return set;
        }

        public async Task AdicionarUsuario(Models.UsuarioMaster usuario, int CodEmp)
        {
            var query = "INSERT INTO UsuarioMaster (Usuario, Senha, CodEmpresaMaster, PermInserir, PermAlterar, PermExcluir, Master, Nome, NomeBanco) VALUES (@Usuario, @Senha, @CodEmpresaMaster, @PermInserir, @PermAlterar, @PermExcluir, @Master, @Nome, '')";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {

                command.Parameters.AddWithValue("@Usuario", usuario.Usuario);
                command.Parameters.AddWithValue("@Senha", usuario.Senha);
                command.Parameters.AddWithValue("@CodEmpresaMaster", CodEmp);
                command.Parameters.AddWithValue("@PermInserir", string.IsNullOrEmpty(usuario.PermInserir) ? "N" : usuario.PermInserir);
                command.Parameters.AddWithValue("@PermAlterar", string.IsNullOrEmpty(usuario.PermAlterar) ? "N" : usuario.PermAlterar);
                command.Parameters.AddWithValue("@PermExcluir", string.IsNullOrEmpty(usuario.PermExcluir) ? "N" : usuario.PermExcluir);
                command.Parameters.AddWithValue("@Master", string.IsNullOrEmpty(usuario.Master) ? "N" : usuario.Master);
                command.Parameters.AddWithValue("@Nome", usuario.Nome);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }

        public async Task AtualizarUsuario(Models.UsuarioMaster usuario, int CodEmp)
        {
            var query = "UPDATE UsuarioMaster set Nome = @Nome, Senha = @Senha, Usuario = @Usuario, PermInserir = @PermInserir, PermAlterar = @PermAlterar, PermExcluir = @PermExcluir, Master = @Master WHERE Codigo = @Codigo and CodEmpresaMaster = @CodEmpresaMaster";
            try
            {
                using (SqlCommand command = new SqlCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@Codigo", usuario.Codigo);
                    command.Parameters.AddWithValue("@Nome", usuario.Nome);
                    command.Parameters.AddWithValue("@Usuario", usuario.Usuario);
                    command.Parameters.AddWithValue("@PermInserir", string.IsNullOrEmpty(usuario.PermInserir) ? "N" : usuario.PermInserir);
                    command.Parameters.AddWithValue("@PermAlterar", string.IsNullOrEmpty(usuario.PermAlterar) ? "N" : usuario.PermAlterar);
                    command.Parameters.AddWithValue("@PermExcluir", string.IsNullOrEmpty(usuario.PermExcluir) ? "N" : usuario.PermExcluir);
                    command.Parameters.AddWithValue("@Master", string.IsNullOrEmpty(usuario.Master) ? "N" : usuario.Master);
                    command.Parameters.AddWithValue("@CodEmpresaMaster", CodEmp);
                    command.Parameters.AddWithValue("@Senha", usuario.Senha);

                    _connection.Open();
                    await command.ExecuteNonQueryAsync();
                    _connection.Close();
                }
            }
            catch (Exception ex)
            {
                // Aqui você pode logar, lançar ou tratar o erro conforme necessário
                // Exemplo: logar no console ou em arquivo
                Console.WriteLine($"Erro ao atualizar usuário: {ex.Message}");
                throw; // Opcional: relança a exceção para tratamento externo
            }
        }

        public async Task<bool> UsuarioEhMasterAsync(int usuarioId)
        {
            // Exemplo usando Entity Framework
            var usuario = await _context.Usuarios
                .Where(u => u.Codigo == usuarioId)
                .Select(u => u.Master)
                .FirstOrDefaultAsync();

            return usuario == "S";
        }


        public async Task ExcluirUsuario(int CodUsuario, int CodEmp)
        {
            var query = "Delete from UsuarioMaster where Codigo = @Codigo and CodEmpresaMaster = @CodEmpresaMaster";
            using (SqlCommand command = new SqlCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@Codigo", CodUsuario);
                command.Parameters.AddWithValue("@CodEmpresaMaster", CodEmp);

                _connection.Open();
                await command.ExecuteNonQueryAsync();
                _connection.Close();
            }
        }
    }
}
