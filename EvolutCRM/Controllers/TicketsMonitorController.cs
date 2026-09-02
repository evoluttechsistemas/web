using EvolutCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsMonitorController : ControllerBase
    {
        private readonly TicketService _ticketService;
        private readonly IConfiguration _config;

        public TicketsMonitorController(TicketService ticketService, IConfiguration config)
        {
            _ticketService = ticketService;
            _config = config;
        }

        [Authorize]
        [HttpGet("pendentes")]
        public async Task<IActionResult> GetPendentes()
        {
            var usuario = User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
                return Unauthorized();

            var result = await _ticketService.ObterPendentesParaUsuarioAsync(usuario);

            return Ok(result);
        }

        [HttpGet("anexo/{codigo:int}")]
        public async Task<IActionResult> ObterAnexo(int codigo)
        {
            var cs = _config.GetConnectionString("Connection");

            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();

            const string sql = @"
SELECT TOP 1
    Imagem,
    NomeImagem
FROM TicketChamadoD
WHERE Codigo = @Codigo
  AND Imagem IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (!await rd.ReadAsync())
                return NotFound("Anexo não encontrado.");

            var bytes = (byte[])rd["Imagem"];
            var nome = rd["NomeImagem"] as string;

            if (string.IsNullOrWhiteSpace(nome))
                nome = "imagem-whatsapp.jpg";

            var contentType = ObterMimeType(nome);

            // inline → abre no navegador (PDF, imagens, texto)
            // attachment → força download (zip, doc, exe, etc)
            var ext = Path.GetExtension(nome).ToLowerInvariant();
            var disposicao = ext switch
            {
                ".pdf" or ".jpg" or ".jpeg" or ".png" or ".gif"
                or ".webp" or ".bmp" or ".txt" or ".csv"
                or ".xml" or ".json" => "inline",
                _ => "attachment"
            };

            Response.Headers["Content-Disposition"] = $"{disposicao}; filename=\"{nome}\"";

            return File(bytes, contentType);
        }

        [HttpGet("audio/{codigo:int}")]
        public async Task<IActionResult> ObterAudio(int codigo)
        {
            var cs = _config.GetConnectionString("Connection");

            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();

            const string sql = @"
SELECT TOP 1
    Audio,
    AudioMimeType,
    AudioFileName
FROM TicketChamadoD
WHERE Codigo = @Codigo
  AND Audio IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (!await rd.ReadAsync())
                return NotFound("Áudio não encontrado.");

            var bytes = (byte[])rd["Audio"];
            var mimeType = rd["AudioMimeType"]?.ToString();

            if (string.IsNullOrWhiteSpace(mimeType))
                mimeType = "audio/mpeg";

            return File(bytes, mimeType);
        }

        [HttpGet("video/{codigo:int}")]
        public async Task<IActionResult> ObterVideo(int codigo)
        {
            var cs = _config.GetConnectionString("Connection");

            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();

            const string sql = @"
SELECT TOP 1
    Video,
    VideoMimeType,
    VideoFileName
FROM TicketChamadoD
WHERE Codigo = @Codigo
  AND Video IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = codigo;

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (!await rd.ReadAsync())
                return NotFound("Vídeo não encontrado.");

            var bytes = (byte[])rd["Video"];
            var mimeType = rd["VideoMimeType"]?.ToString();
            var nome = rd["VideoFileName"]?.ToString() ?? "video.mp4";

            if (string.IsNullOrWhiteSpace(mimeType))
                mimeType = "video/mp4";

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{nome}\"";

            return File(bytes, mimeType);
        }

        private static string ObterMimeType(string? nomeArquivo)
        {
            var ext = Path.GetExtension(nomeArquivo ?? "").ToLowerInvariant();

            return ext switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".zip" => "application/zip",
                ".rar" => "application/vnd.rar",
                ".xml" => "application/xml",
                ".json" => "application/json",
                ".pfx" => "application/x-pkcs12",
                ".p12" => "application/x-pkcs12",
                _ => "application/octet-stream"
            };
        }
    }
}