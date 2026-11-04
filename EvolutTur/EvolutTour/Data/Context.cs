using Microsoft.EntityFrameworkCore;

namespace EvolutTur.Data
{
    public class Context : DbContext
    {
        public Context(DbContextOptions<Context> options) : base(options) { }

        
    }
}
