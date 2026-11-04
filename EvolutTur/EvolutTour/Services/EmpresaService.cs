using EvolutTur.Data;
using EvolutTur.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvolutTur.Services
{
    public class EmpresaService
    {
        private readonly MasterDbContext _context;

        public EmpresaService(MasterDbContext context)
        {
            _context = context;
        }

        public async Task<Empresa> BuscaEmpresa(int CodEmp)
        {
            return await _context.Empresas
                .Where(e => e.Codigo == CodEmp)
                .Select(e => new Empresa
                {
                    Razao = e.Razao,
                    Fantasia = e.Fantasia,
                    CNPJ = e.CNPJ
                })
                .FirstOrDefaultAsync();
        }
    }
}
