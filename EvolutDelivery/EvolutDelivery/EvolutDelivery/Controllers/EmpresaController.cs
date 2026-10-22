using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EvolutDelivery.Controllers
{
    [ApiController]
    [Route("api")]
    public class EmpresaController : ControllerBase
    {
        private readonly string _connectionString;

        public EmpresaController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConnectionComercial");
        }

        [HttpGet("empresas-por-cnpj")]
        public async Task<IActionResult> BuscarEmpresas(string cnpj)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
    SELECT Codigo, Fantasia
    FROM Empresa
    WHERE CNPJ = @CNPJ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CNPJ", cnpj);

            var reader = await cmd.ExecuteReaderAsync();

            var lista = new List<object>();

            while (await reader.ReadAsync())
            {
                lista.Add(new
                {
                    CodEmp = Convert.ToInt32(reader["Codigo"]),
                    Nome = reader["Fantasia"]?.ToString()
                });
            }

            return Ok(lista);
        }
    }
}