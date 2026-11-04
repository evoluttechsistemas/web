using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvolutTur.Data
{
    public class MasterDbContext : DbContext
    {
        public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options) { }

        public DbSet<UsuarioMaster> Usuarios { get; set; }
        public DbSet<EmpresaMaster> Empresas { get; set; }
    }

    [Table("UsuarioMaster")]
    public class UsuarioMaster
    {
        [Key]
        public int Codigo { get; set; }
        public string Usuario { get; set; }
        public string Senha { get; set; }
        public string NomeBanco { get; set; }
        public int CodEmpresaMaster { get; set; }
        public string Master { get; set; } = string.Empty;

    }

    [Table("EmpresaMaster")]
    public class EmpresaMaster
    {
        [Key]
        public int Codigo { get; set; }
        public string Razao { get; set; }
        public string Fantasia { get; set; }
        public string CNPJ { get; set; }       
    }


}
