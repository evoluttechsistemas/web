namespace EvolutCRM.Models
{
    public class ChangelogVersao
    {
        public string Versao { get; set; } = "";
        public string Sistema { get; set; } = "";
        public List<ChangelogItem> Itens { get; set; } = new();
    }

    public class ChangelogItem
    {
        public int Codigo { get; set; }
        public string Titulo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string Icone { get; set; } = "";
        public string TipoAlteracao { get; set; } = "";
        public string Versao { get; set; } = "";
        public bool Destaque { get; set; }
        public DateTime DataHora { get; set; }
        public string Referencia { get; set; } = "";
    }

    // ChangelogItemAdminDto.cs
    public class ChangelogItemAdminDto
    {
        public int Codigo { get; set; }
        public string Titulo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string Versao { get; set; } = "";
        public string Sistema { get; set; } = "HELP";
        public string TipoAlteracao { get; set; } = "MELHORIA";
        public string Icone { get; set; } = "ti-news";
        public string Referencia { get; set; } = "";
        public bool Destaque { get; set; }
        public DateTime DataHora { get; set; }
        public bool Ativo { get; set; }
    }

    // ParametrosHelpAdminModel.cs
    public class ParametrosHelpAdminModel
    {
        public int Codigo { get; set; }
        public string Titulo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string Versao { get; set; } = "";
        public string Sistema { get; set; } = "HELP";
        public string TipoAlteracao { get; set; } = "MELHORIA";
        public string Icone { get; set; } = "ti-news";
        public string Referencia { get; set; } = "";
        public bool Destaque { get; set; }
    }
}