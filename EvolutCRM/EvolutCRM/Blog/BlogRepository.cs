using Dapper;
using System.Data;

namespace EvolutCRM.Blog
{
    public class BlogRepository(IDbConnection db)
    {
        public Task<IEnumerable<Post>> GetPublicadosAsync() =>
            db.QueryAsync<Post>(
                "SELECT * FROM Posts WHERE Status = 'publicado' ORDER BY DataCriacao DESC");

        public Task<Post?> GetBySlugAsync(string slug) =>
            db.QueryFirstOrDefaultAsync<Post>(
                "SELECT * FROM Posts WHERE Slug = @Slug AND Status = 'publicado'",
                new { Slug = slug });

        public Task<IEnumerable<Post>> GetAllAsync() =>
            db.QueryAsync<Post>("SELECT * FROM Posts ORDER BY DataCriacao DESC");

        public Task<Post?> GetByIdAsync(int id) =>
            db.QueryFirstOrDefaultAsync<Post>(
                "SELECT * FROM Posts WHERE IdPost = @Id", new { Id = id });

        public async Task<int> CreateAsync(CreatePostDto dto)
        {
            return await db.ExecuteScalarAsync<int>("""
            INSERT INTO Posts (Titulo, Slug, Conteudo, Imagem, Status)
            OUTPUT INSERTED.IdPost
            VALUES (@Titulo, @Slug, @Conteudo, @Imagem, @Status)
            """, dto);
        }

        public Task<int> UpdateAsync(int id, UpdatePostDto dto) =>
            db.ExecuteAsync("""
            UPDATE Posts
            SET Titulo = @Titulo, Slug = @Slug, Conteudo = @Conteudo,
                Imagem = @Imagem, Status = @Status,
                DataAtualizacao = GETDATE()
            WHERE IdPost = @Id
            """, new { dto.Titulo, dto.Slug, dto.Conteudo, dto.Imagem, dto.Status, Id = id });

        public Task<int> DeleteAsync(int id) =>
            db.ExecuteAsync("DELETE FROM Posts WHERE IdPost = @Id", new { Id = id });

        public async Task<IEnumerable<Post>> GetListagemAsync(int pagina = 1, int por = 10)
        {
            const string sql = """
        SELECT IdPost, Titulo, Slug, Status, Imagem, DataCriacao
        FROM Posts
        ORDER BY DataCriacao DESC
        OFFSET @offset ROWS FETCH NEXT @por ROWS ONLY
        """;
            return await db.QueryAsync<Post>(sql, new { offset = (pagina - 1) * por, por });
        }

        public async Task<(int Total, int Publicados, int Rascunhos)> GetContagensAsync()
        {
            const string sql = """
        SELECT
            COUNT(*) AS Total,
            SUM(CASE WHEN Status = 'publicado' THEN 1 ELSE 0 END) AS Publicados,
            SUM(CASE WHEN Status = 'rascunho'  THEN 1 ELSE 0 END) AS Rascunhos
        FROM Posts
        """;
            var r = await db.QuerySingleAsync(sql);
            return ((int)r.Total, (int)r.Publicados, (int)r.Rascunhos);
        }

        public async Task SalvarLayoutAsync(int idPost, string layoutJson)
        {
            const string sql = "UPDATE Posts SET Layout = @layoutJson WHERE IdPost = @idPost";
            await db.ExecuteAsync(sql, new { idPost, layoutJson });
        }

        public async Task<string?> GetLayoutAsync(int idPost)
        {
            const string sql = "SELECT Layout FROM Posts WHERE IdPost = @idPost";
            return await db.QuerySingleOrDefaultAsync<string>(sql, new { idPost });
        }


    }


}
