using EvolutCRM.Models;
using EvolutCRM.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly LoginService _loginService;
    private readonly JwtService _jwtService;

    public AuthController(LoginService loginService, JwtService jwtService)
    {
        _loginService = loginService;
        _jwtService = jwtService;
    }

    // =========================
    // LOGIN
    // =========================
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        Console.WriteLine($"[AUTH] Login request -> {request.Username}");

        var user = await _loginService.LoginAsync(request.Username, request.Password);

        if (user == null)
        {
            Console.WriteLine("[AUTH] Login inválido");
            return Unauthorized();
        }

        var token = _jwtService.GenerateToken(user.Usuario, "Admin");
        var refresh = _jwtService.GenerateRefreshToken();

        Console.WriteLine($"[AUTH] Token gerado para {user.Usuario}");

        RefreshTokenStore.Salvar(
            user.Usuario,
            refresh,
            DateTime.UtcNow.AddDays(7)
        );

        return Ok(new LoginResponse
        {
            Username = user.Usuario,
            Token = token,
            RefreshToken = refresh
        });
    }


    // =========================
    // REFRESH TOKEN
    // =========================
    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        var valido = RefreshTokenStore.Validar(
            request.Username,
            request.RefreshToken
        );

        if (!valido)
            return Unauthorized();

        var novoToken = _jwtService.GenerateToken(request.Username);

        return Ok(new
        {
            Token = novoToken
        });
    }
}
