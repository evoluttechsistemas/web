using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient; // ← ADICIONAR ESTE

namespace EvolutCRM.Controllers
{
    [ApiController]
    [Route("api/usuario")]
    public class UsuarioController : ControllerBase
    {
        private readonly IConfiguration _config;

        public UsuarioController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("marcar-offline")]
        public async Task<IActionResult> MarcarOffline([FromBody] MarcarOfflineDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Usuario))
                return Ok();

            try
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("Connection"));
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    UPDATE Usuario
                    SET Online = 'N',
                        DataHoraUltimoPing = NULL
                    WHERE UPPER(LTRIM(RTRIM(Usuario)))
                        = UPPER(LTRIM(RTRIM(@Usuario)))
                      AND ISNULL(Inativo, 'N') = 'N'", conn);

                cmd.Parameters.AddWithValue("@Usuario", dto.Usuario.Trim());
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }

            return Ok();
        }
    }

    public class MarcarOfflineDto
    {
        public string Usuario { get; set; } = "";
    }
}