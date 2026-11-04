using EvolutTur.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EvolutTur.Services
{
    public class AutenticacaoService
    {
        private readonly MasterDbContext _context;

        private readonly EmailSender _emailSender;

        public AutenticacaoService(MasterDbContext context, EmailSender emailSender)
        {
            _context = context;
            _emailSender = emailSender;
        }

        public async Task<bool> EnviarSenhaPorEmailAsync(string email)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Usuario == email);
            if (usuario == null)
                return false;

            // Gera nova senha aleatória
            var novaSenha = GerarSenhaAleatoria();
            usuario.Senha = HashSenha(novaSenha); // Use seu método de hash

            await _context.SaveChangesAsync();

            // Envia a nova senha por e-mail
            await _emailSender.SendAsync(email, "Nova senha de acesso", $"Sua nova senha é: {novaSenha}");

            return true;
        }

        private string HashSenha(string senha)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(senha);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private string GerarSenhaAleatoria(int tamanho = 8)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, tamanho)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public async Task<int> ValidarLoginAsync(string usuario, string senha)
        {
            var user = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Usuario == usuario && u.Senha == senha);

            if (user != null)
                return user.CodEmpresaMaster;

            return 0; // Ou outro valor/sinalização para login inválido
        }

        public async Task<string> vericaUsuarioMaster(string usuario, string senha)
        {
            var user = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Usuario == usuario && u.Senha == senha);

            if (user != null)
                return user.Master;

            return "N"; // Ou outro valor/sinalização para login inválido
        }
    }
}
