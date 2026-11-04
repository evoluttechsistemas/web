using EvolutCotacao.Models;
using Microsoft.EntityFrameworkCore;

namespace EvolutCotacao.Data
{
    public class Context : DbContext
    {
        public Context(DbContextOptions<Context> options) : base(options) { }

        public DbSet<ProdutoCotado> ProdutoCotado { get; set; }
       
    }
}
