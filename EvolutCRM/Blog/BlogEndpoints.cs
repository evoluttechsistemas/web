using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace EvolutCRM.Blog
{
    public static class BlogEndpoints
    {
        public static void MapBlogEndpoints(this WebApplication app)
        {
            // ── PÚBLICOS ───────────────────────────────────────────────────────────
            app.MapGet("/api/posts", async (BlogRepository repo) =>
                Results.Ok(await repo.GetPublicadosAsync()));

            app.MapGet("/api/posts/{slug}", async (string slug, BlogRepository repo) =>
                await repo.GetBySlugAsync(slug) is Post post
                    ? Results.Ok(post)
                    : Results.NotFound());

            // ── ADMIN (leitura — mantém auth) ─────────────────────────────────────
            var admin = app.MapGroup("/api/admin").RequireAuthorization();

            admin.MapGet("/posts", async (int pagina, int por, BlogRepository repo) =>
                Results.Ok(await repo.GetListagemAsync(pagina, por)));

            admin.MapGet("/posts/{id:int}", async (int id, BlogRepository repo) =>
                await repo.GetByIdAsync(id) is Post post
                    ? Results.Ok(post)
                    : Results.NotFound());

            admin.MapDelete("/posts/{id:int}", async (int id, BlogRepository repo) =>
                await repo.DeleteAsync(id) > 0
                    ? Results.NoContent()
                    : Results.NotFound());

            // ── ADMIN (escrita — fora do grupo, HttpClient Blazor não repassa cookie) ──
            app.MapPost("/api/admin/posts", async (CreatePostDto dto, BlogRepository repo) =>
            {
                var id = await repo.CreateAsync(dto);
                return Results.Created($"/api/admin/posts/{id}", new { IdPost = id });
            });

            app.MapPut("/api/admin/posts/{id:int}", async (int id, UpdatePostDto dto, BlogRepository repo) =>
                await repo.UpdateAsync(id, dto) > 0
                    ? Results.NoContent()
                    : Results.NotFound());

            app.MapPost("/api/admin/posts/upload-imagem", async ([FromForm] IFormFile file, IConfiguration config) =>
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{ext}";

                var pastaBase = config["Uploads:PastaFisica"] ?? @"C:\FTP\Site\Help\uploads";
                var pastaBlog = Path.Combine(pastaBase, "blog");
                Directory.CreateDirectory(pastaBlog);

                await using (var stream = File.Create(Path.Combine(pastaBlog, fileName)))
                    await file.CopyToAsync(stream);

                return Results.Ok(new { Caminho = $"/uploads/blog/{fileName}" });
            })
.DisableAntiforgery();
        }
    }
}