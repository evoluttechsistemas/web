namespace EvolutCRM.Models
{
    public class ComercialFluxoMensagemModel
    {
        public int Codigo { get; set; }
        public int CodEmp { get; set; }
        public int DiasParaDisparo { get; set; }
        public string Descricao { get; set; } = "";
        public string TextoWhatsApp { get; set; } = "";
        public string Link { get; set; } = "";
        public string TipoMidia { get; set; } = "";
        public string NomeArquivo { get; set; } = "";
        public string MimeType { get; set; } = "";
        public byte[] Arquivo { get; set; }
        public string Ativo { get; set; } = "S";

        public TimeSpan? HoraInicio { get; set; }

        public int IntervaloMinutos { get; set; }

        public string Funil { get; set; }

        public string CamposPersonalizados { get; set; }

        public string HorarioComercial { get; set; }

        public string EnviarTodosFunis { get; set; }
        public int Ordem { get; set; }

       
    }
}
