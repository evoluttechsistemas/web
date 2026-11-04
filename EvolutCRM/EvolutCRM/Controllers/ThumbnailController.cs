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
        cmd.CommandText = "SELECT Thumbnail, ThumbnailMime, UrlThumbnail FROM DocumentacaoTutorial WHERE Codigo = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return NotFound();

        if (rd.IsDBNull(0))
        {
            // capa já migrada para arquivo: links antigos para /api/thumbnail continuam funcionando
            var url = rd.IsDBNull(2) ? "" : rd.GetString(2);
            return url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
                ? RedirectPermanent(url)
                : NotFound();
        }

        var dados = (byte[])rd["Thumbnail"];
        var mime = rd.IsDBNull(rd.GetOrdinal("ThumbnailMime")) ? "image/png" : rd.GetString(rd.GetOrdinal("ThumbnailMime"));

        // Com ?v=<data de alteração> a URL muda sempre que a capa muda: pode ficar em cache por 1 ano.
        // Sem versão, cache curto para não mostrar capa antiga por muito tempo.
        Response.Headers.CacheControl = Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "public, max-age=300";

        return File(dados, mime);
    }
}