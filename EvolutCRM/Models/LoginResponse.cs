namespace EvolutCRM.Models;

public class LoginResponse
{
    public string Username { get; set; } = "";
    public string Token { get; set; } = "";

    // 🔹 NOVO (não remove o antigo)
    public string RefreshToken { get; set; } = "";
}
