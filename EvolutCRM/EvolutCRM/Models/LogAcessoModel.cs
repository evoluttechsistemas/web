namespace EvolutCRM.Models
{
    public class LogAcessoModel
    {
        public int Id { get; set; }
        public string Usuario { get; set; } = "";
        public int CodEmp { get; set; }
        public string NomeEmpresa { get; set; } = "";
        public DateTime DataHora { get; set; }
        public string TipoEvento { get; set; } = ""; // LOGIN | LOGOUT | SESSAO_EXPIRADA
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
    }
}