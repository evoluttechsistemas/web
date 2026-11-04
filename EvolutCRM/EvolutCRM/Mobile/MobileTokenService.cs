using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EvolutCRM.Mobile
{
    /// <summary>
    /// Gera o token do HELP Mobile. Usa a mesma chave/issuer/audience do JWT que já existe no HELP
    /// (seção "Jwt" do appsettings), só que com validade longa: o usuário loga uma vez, igual ao WhatsApp.
    /// Para "derrubar" alguém, basta inativar o usuário: toda requisição confere se ele continua ativo.
    /// </summary>
    public class MobileTokenService
    {
        public const string ClaimCodEmp = "codEmp";
        public const string ClaimCanal = "canal";

        private readonly IConfiguration _config;

        public MobileTokenService(IConfiguration config) => _config = config;

        public string GerarToken(string usuario, int codEmp)
        {
            var jwt = _config.GetSection("Jwt");
            var dias = int.TryParse(_config["Mobile:TokenDias"], out var d) && d > 0 ? d : 365;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.Role, "Mobile"),
                new Claim(ClaimCodEmp, codEmp.ToString()),
                new Claim(ClaimCanal, "mobile"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddDays(dias),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
