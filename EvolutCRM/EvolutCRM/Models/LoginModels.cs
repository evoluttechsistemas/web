namespace EvolutCRM.Models
{
    public class UserModels
    {
        public int Codigo { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public bool Inativo { get; set; }
        public int CodEmp { get; set; }
    }

    public class EmpresaModels
    {
        public int Codigo { get; set; }
        public string Fantasia { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string NomeReduzido { get; set; } = string.Empty;
    }
}