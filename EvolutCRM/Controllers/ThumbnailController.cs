using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Controllers;

[ApiController]
[Route("api/thumbnail")]
public class ThumbnailController : ControllerBase
{
    private readonly IConfiguration _config;
    public ThumbnailController(IConfiguration config) => _config = config;

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var cs = _config.GetConnectionString("Connection")
               ?? _config.GetConnectionString("ConexaoPadrao")
               ?? _config.GetConnectionString("EvolutCRM");

        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Thumbnail, ThumbnailMime FROM DocumentacaoTutorial WHERE Codigo = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync() || rd.IsDBNull(0)) return NotFound();

        var dados = (byte[])rd["Thumbnail"];
        var mime = rd.GetString(rd.GetOrdinal("ThumbnailMime"));
        return File(dados, mime);
    }
}