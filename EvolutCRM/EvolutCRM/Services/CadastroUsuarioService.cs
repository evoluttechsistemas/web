using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class CadastroUsuarioService
    {
        private readonly string _connectionString;
        private readonly EmailService _emailService;

        public CadastroUsuarioService(IConfiguration config, EmailService emailService)
        {
            _connectionString = config.GetConnectionString("Connection")!;
            _emailService = emailService;
        }

        // ─────────────────────────────────────────────
        // Verifica se usuário já existe na tabela
        // ─────────────────────────────────────────────
        public async Task<bool> UsuarioExisteAsync(string usuario, string CodEmp)
        {
            const string sql = "SELECT COUNT(1) FROM Usuario WHERE Usuario = @Usuario and CodEmp = @CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            await conn.OpenAsync();
            var count = (int)await cmd.ExecuteScalarAsync();
            return count > 0;
        }

        // ─────────────────────────────────────────────
        // Cadastra novo usuário com segurança completa
        // ─────────────────────────────────────────────
        public async Task<CadastroUsuarioResult> CadastrarUsuarioAsync(CadastroUsuarioRequest request)
        {
            try
            {
                // 1. Validações básicas
                if (string.IsNullOrWhiteSpace(request.Usuario))
                    return CadastroUsuarioResult.Erro("Informe o nome do usuário.");

                if (string.IsNullOrWhiteSpace(request.Email))
                    return CadastroUsuarioResult.Erro("Informe o e-mail.");

                if (!EmailValido(request.Email))
                    return CadastroUsuarioResult.Erro("E-mail inválido.");

                var validacaoSenha = ValidarForcaSenha(request.Senha);
                if (!validacaoSenha.Sucesso)
                    return validacaoSenha;

                // 2. Verifica duplicidade
                if (await UsuarioExisteAsync(request.Usuario, request.CodEmpresa.ToString()))
                    return CadastroUsuarioResult.Erro("Usuário já existe no sistema.");

                // 3. Gera hash da senha com BCrypt
                var senhaHash = BCrypt.Net.BCrypt.HashPassword(request.Senha, workFactor: 12);

                // 4. Gera token de verificação de email
                var token = GerarToken();
                var expiracao = DateTime.Now.AddHours(24);

                // 5. Insere no banco
                const string sql = @"
    INSERT INTO Usuario
        (Usuario, Senha, Help, SenhaHash,
         Email, EmailVerificado, TokenVerificacao, TokenExpiracao,
         Requer2FA, TentativasLogin, UltimoLogin, CodEmp, Inativo)
    VALUES
        (@Usuario, @Senha, 'S', @SenhaHash,
         @Email, 'N', @Token, @TokenExpiracao,
         @Requer2FA, 0, NULL, @CodEmp, 'N')";

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("@Usuario", request.Usuario.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Senha", request.Senha);          // mantém texto puro para compatibilidade com EvolutTech
                cmd.Parameters.AddWithValue("@SenhaHash", senhaHash);
                cmd.Parameters.AddWithValue("@Email", request.Email.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.Parameters.AddWithValue("@TokenExpiracao", expiracao);
                cmd.Parameters.AddWithValue("@Requer2FA", request.Requer2FA);
                cmd.Parameters.AddWithValue("@CodEmp", request.CodEmpresa);

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();

                // 6. Envia email de verificação
                await _emailService.EnviarVerificacaoEmailAsync(
                    request.Email,
                    request.Usuario,
                    token);

                return CadastroUsuarioResult.Ok($"Usuário {request.Usuario} cadastrado com sucesso! Um e-mail de verificação foi enviado para {request.Email}.");
            }
            catch (Exception ex)
            {
                return CadastroUsuarioResult.Erro($"Erro interno ao cadastrar usuário: {ex.Message}");
            }
        }

        public async Task<CadastroUsuarioResult> ReenviarEmailVerificacaoAsync(string usuario, string CodEmp)
        {
            if (string.IsNullOrWhiteSpace(usuario))
                return CadastroUsuarioResult.Erro("Informe o usuário.");

            const string sqlBusca = @"
        SELECT TOP 1 Codigo, Usuario, Email, EmailVerificado
        FROM Usuario
        WHERE Usuario = @Usuario
          AND CodEmp = @CodEmp
          AND ISNULL(Inativo, 'N') <> 'S'
          AND ISNULL(Help, 'N') = 'S'";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmdBusca = new SqlCommand(sqlBusca, conn);
            cmdBusca.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpperInvariant());
            cmdBusca.Parameters.AddWithValue("@CodEmp", CodEmp);

            int codigo;
            string usuarioBanco;
            string email;
            string emailVerificado;

            using (var reader = await cmdBusca.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return CadastroUsuarioResult.Erro("Usuário não encontrado.");

                codigo = reader.GetInt32(reader.GetOrdinal("Codigo"));
                usuarioBanco = reader["Usuario"] as string ?? usuario.Trim().ToUpperInvariant();
                email = reader["Email"] as string ?? "";
                emailVerificado = reader["EmailVerificado"] as string ?? "N";
            }

            if (emailVerificado == "S")
                return CadastroUsuarioResult.Ok("E-mail já verificado.");

            if (string.IsNullOrWhiteSpace(email) || !EmailValido(email))
                return CadastroUsuarioResult.Erro("O usuário não possui um e-mail válido cadastrado.");

            var token = GerarToken();
            var expiracao = DateTime.Now.AddHours(24);

            const string sqlUpdate = @"
        UPDATE Usuario
        SET TokenVerificacao = @Token,
            TokenExpiracao = @TokenExpiracao
        WHERE Codigo = @Codigo
          AND CodEmp = @CodEmp";

            using var cmdUpdate = new SqlCommand(sqlUpdate, conn);
            cmdUpdate.Parameters.AddWithValue("@Token", token);
            cmdUpdate.Parameters.AddWithValue("@TokenExpiracao", expiracao);
            cmdUpdate.Parameters.AddWithValue("@Codigo", codigo);
            cmdUpdate.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmdUpdate.ExecuteNonQueryAsync();

            await _emailService.EnviarVerificacaoEmailAsync(email, usuarioBanco, token);

            return CadastroUsuarioResult.Ok($"Novo link de verificação enviado para {email}.");
        }

        // ─────────────────────────────────────────────
        // Verifica token de email e marca como verificado
        // ─────────────────────────────────────────────
        public async Task<CadastroUsuarioResult> VerificarEmailAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return CadastroUsuarioResult.Erro("Token inválido.");

            const string sqlBusca = @"
        SELECT Codigo, TokenExpiracao, EmailVerificado
        FROM Usuario
        WHERE TokenVerificacao = @Token";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmdBusca = new SqlCommand(sqlBusca, conn);
            cmdBusca.Parameters.AddWithValue("@Token", token);

            using var reader = await cmdBusca.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return CadastroUsuarioResult.Erro("Token não encontrado ou já utilizado.");

            var codigo = reader.GetInt32(reader.GetOrdinal("Codigo"));

            var ordExpiracao = reader.GetOrdinal("TokenExpiracao");
            DateTime? expiracao = reader.IsDBNull(ordExpiracao)
                ? null
                : reader.GetDateTime(ordExpiracao);

            var ordEmailVerificado = reader.GetOrdinal("EmailVerificado");
            var jaVerificado = !reader.IsDBNull(ordEmailVerificado)
                && reader.GetString(ordEmailVerificado).Trim().ToUpperInvariant() == "S";

            await reader.CloseAsync();

            if (jaVerificado)
                return CadastroUsuarioResult.Ok("E-mail já verificado anteriormente.");

            if (!expiracao.HasValue || DateTime.Now > expiracao.Value)
                return CadastroUsuarioResult.Erro("Token expirado. Tente fazer login novamente para receber outro link de verificação.");

            const string sqlUpdate = @"
        UPDATE Usuario
        SET EmailVerificado = 'S',
            TokenVerificacao = NULL,
            TokenExpiracao = NULL
        WHERE Codigo = @Codigo";

            using var cmdUpdate = new SqlCommand(sqlUpdate, conn);
            cmdUpdate.Parameters.AddWithValue("@Codigo", codigo);
            await cmdUpdate.ExecuteNonQueryAsync();

            return CadastroUsuarioResult.Ok("E-mail verificado com sucesso! Você já pode fazer login.");
        }

        // ─────────────────────────────────────────────
        // Valida login — senha + bloqueio + Help = 'S'
        // ─────────────────────────────────────────────
        public async Task<ValidarLoginResult> ValidarLoginAsync(string usuario, string senha, string CodEmp)
        {
            const string sql = @"
                SELECT Codigo, SenhaHash, Senha, Help, EmailVerificado,
                       Requer2FA, Email, TentativasLogin, BloqueadoAte
                FROM Usuario
                WHERE Usuario = @Usuario and CodEmp = @CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return ValidarLoginResult.Erro("Usuário ou senha incorretos.");

            var codigo = reader.GetInt32("Codigo");
            var senhaHash = reader["SenhaHash"] as string;
            var senhaTexto = reader["Senha"] as string;
            var help = reader["Help"] as string;
            var emailVerificado = reader["EmailVerificado"] as string;
            var requer2FA = reader["Requer2FA"] as string;
            var email = reader["Email"] as string ?? "";
            var tentativas = reader.IsDBNull(reader.GetOrdinal("TentativasLogin")) ? 0 : reader.GetInt32("TentativasLogin");
            var bloqueadoAte = reader["BloqueadoAte"] as DateTime?;

            await reader.CloseAsync();

            // 1. Verifica acesso ao HELP
            if (help != "S")
                return ValidarLoginResult.Erro("Usuário sem acesso ao HELP.");

            // 2. Verifica bloqueio por tentativas
            if (bloqueadoAte.HasValue && bloqueadoAte.Value > DateTime.Now)
            {
                var restante = (int)(bloqueadoAte.Value - DateTime.Now).TotalMinutes + 1;
                return ValidarLoginResult.Erro($"Conta bloqueada. Tente novamente em {restante} minuto(s).");
            }

            // 3. Valida senha — bcrypt se tiver hash, texto puro como fallback
            bool senhaValida;
            if (!string.IsNullOrEmpty(senhaHash))
            {
                senhaValida = BCrypt.Net.BCrypt.Verify(senha, senhaHash);
            }
            else
            {
                // fallback para senha texto puro + migração silenciosa para hash
                senhaValida = senha == senhaTexto;
                if (senhaValida)
                    await MigrarParaHashAsync(codigo, senha, conn, CodEmp);
            }

            if (!senhaValida)
            {
                await IncrementarTentativasAsync(codigo, tentativas, conn, CodEmp);
                return ValidarLoginResult.Erro("Usuário ou senha incorretos.");
            }

            // 4. Verifica email
            if (emailVerificado != "S")
                return ValidarLoginResult.Erro("E-mail ainda não verificado. Verifique sua caixa de entrada.");

            // 5. Login válido — zera tentativas e registra UltimoLogin
            await ZerarTentativasAsync(codigo, conn, CodEmp);

            return ValidarLoginResult.Ok(requer2FA == "S", email);
        }

        // ─────────────────────────────────────────────
        // Gera código 2FA, salva no banco e envia email
        // ─────────────────────────────────────────────
        public async Task GerarEEnviar2FAAsync(string usuario, string email, string CodEmp)
        {
            // Código de 6 dígitos
            var codigo = new Random().Next(100000, 999999).ToString();
            var expiracao = DateTime.Now.AddMinutes(10);

            const string sql = @"
                UPDATE Usuario
                SET Codigo2FA = @Codigo, Expiracao2FA = @Expiracao
                WHERE Usuario = @Usuario and CodEmp = @CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Expiracao", expiracao);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            await _emailService.EnviarCodigo2FAAsync(email, usuario, codigo);
        }

        // ─────────────────────────────────────────────
        // Valida o código 2FA digitado pelo usuário
        // ─────────────────────────────────────────────
        public async Task<bool> Verificar2FAAsync(string usuario, string codigoDigitado, string CodEmp)
        {
            const string sql = @"
                SELECT Codigo2FA, Expiracao2FA
                FROM Usuario
                WHERE Usuario = @Usuario and CodEmp = @CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return false;

            var codigoBanco = reader["Codigo2FA"] as string;
            var expiracao = reader["Expiracao2FA"] as DateTime?;

            await reader.CloseAsync();

            if (string.IsNullOrEmpty(codigoBanco)) return false;
            if (!expiracao.HasValue) return false;
            if (DateTime.Now > expiracao.Value) return false;
            if (codigoBanco != codigoDigitado.Trim()) return false;

            // Limpa o código após uso
            const string sqlLimpar = @"
                UPDATE Usuario
                SET Codigo2FA = NULL, Expiracao2FA = NULL
                WHERE Usuario = @Usuario";

            using var cmdLimpar = new SqlCommand(sqlLimpar, conn);
            cmdLimpar.Parameters.AddWithValue("@Usuario", usuario);
            await cmdLimpar.ExecuteNonQueryAsync();

            return true;
        }

        // ─────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────
        // ─────────────────────────────────────────────
        // Grava token de "lembrar dispositivo" no banco
        // ─────────────────────────────────────────────
        public async Task<string> GravarSessaoLembrarAsync(string usuario, string CodEmp, int minutos = 21600 )
        {
            var token = GerarToken();
            var expiracao = DateTime.Now.AddMinutes(minutos);

            const string sql = @"
                UPDATE Usuario
                SET SessaoToken     = @Token,
                    SessaoExpiracao = @Expiracao
                WHERE Usuario = @Usuario and CodEmp = @CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.Parameters.AddWithValue("@Expiracao", expiracao);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            return token;
        }

        // ─────────────────────────────────────────────
        // Valida token de "lembrar dispositivo"
        // ─────────────────────────────────────────────
        public async Task<bool> ValidarSessaoLembrarAsync(string usuario, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            const string sql = @"
                SELECT SessaoToken, SessaoExpiracao
                FROM Usuario
                WHERE Usuario = @Usuario AND Help = 'S'";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario);         

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return false;

            var tokenBanco = reader["SessaoToken"] as string;
            var expiracao = reader["SessaoExpiracao"] as DateTime?;

            if (string.IsNullOrEmpty(tokenBanco)) return false;
            if (!expiracao.HasValue) return false;
            if (DateTime.Now > expiracao.Value) return false;
            if (tokenBanco != token) return false;

            return true;
        }

        private static async Task MigrarParaHashAsync(int codigo, string senha, SqlConnection conn, string CodEmp)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(senha, workFactor: 12);
            const string sql = "UPDATE Usuario SET SenhaHash = @Hash WHERE Codigo = @Codigo and CodEmp = @CodEmp";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Hash", hash);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task IncrementarTentativasAsync(int codigo, int tentativasAtuais, SqlConnection conn, string CodEmp)
        {
            var novasTentativas = tentativasAtuais + 1;
            DateTime? bloqueadoAte = null;

            // Bloqueia por 15 minutos após 5 tentativas
            if (novasTentativas >= 5)
                bloqueadoAte = DateTime.Now.AddMinutes(15);

            const string sql = @"
                UPDATE Usuario
                SET TentativasLogin = @Tentativas,
                    BloqueadoAte    = @BloqueadoAte
                WHERE Codigo = @Codigo and CodEmp = @CodEmp";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Tentativas", novasTentativas);
            cmd.Parameters.AddWithValue("@BloqueadoAte", (object?)bloqueadoAte ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ZerarTentativasAsync(int codigo, SqlConnection conn, string CodEmp)
        {
            const string sql = @"
                UPDATE Usuario
                SET TentativasLogin = 0,
                    BloqueadoAte    = NULL,
                    UltimoLogin     = @Agora
                WHERE Codigo = @Codigo and CodEmp = @CodEmp";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Agora", DateTime.Now);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        private static string GerarToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(48);
            return Convert.ToBase64String(bytes)
                          .Replace("+", "-")
                          .Replace("/", "_")
                          .Replace("=", "");
        }

        private static bool EmailValido(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public async Task<EmpresaModels?> BuscarEmpresaDoUsuarioAsync(string usuario)
        {
            const string sql = @"
        SELECT TOP 1
            e.Codigo,
            e.NomeReduzido
        FROM Usuario u
        INNER JOIN Empresa e ON e.Codigo = u.CodEmp
        WHERE u.Usuario = @Usuario
          AND ISNULL(u.Inativo, 'N') <> 'S'
          AND ISNULL(u.Help, 'N') = 'S'
        ORDER BY u.CodEmp";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpperInvariant());

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new EmpresaModels
            {
                Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                NomeReduzido = reader["NomeReduzido"] as string ?? ""
            };
        }
        private static CadastroUsuarioResult ValidarForcaSenha(string senha)
        {
            if (string.IsNullOrEmpty(senha) || senha.Length < 8)
                return CadastroUsuarioResult.Erro("A senha deve ter no mínimo 8 caracteres.");

            if (!senha.Any(char.IsUpper))
                return CadastroUsuarioResult.Erro("A senha deve conter pelo menos uma letra maiúscula.");

            if (!senha.Any(char.IsDigit))
                return CadastroUsuarioResult.Erro("A senha deve conter pelo menos um número.");

            const string especiais = "!@#$%^&*()_+-=[]{}|;':\",./<>?";
            if (!senha.Any(c => especiais.Contains(c)))
                return CadastroUsuarioResult.Erro("A senha deve conter pelo menos um caractere especial.");

            return CadastroUsuarioResult.Ok();
        }
    }
}
