namespace EvolutCRM.Models
{
    public class AgendaModel
    {
        public int Codigo { get; set; }
        public string Origem { get; set; } 
        public int? CodCRMC { get; set; }


        public string Descricao { get; set; }
        public DateTime DataAgendamento { get; set; }
        public TimeSpan HoraAgendamento { get; set; }

        public int CodCliente { get; set; }
        public string NomeCliente { get; set; }
        public string ApelidoCliente { get; set; }
        public string EmailCliente { get; set; }

        public string Usuario { get; set; }
        public string Status { get; set; } = "AGENDADO";
        public int CodEmp { get; set; }
        public string NumeroTelefone { get; set; }

        // ===== CAMPOS DE REPETIÇÃO =====
        public string TipoRepeticao { get; set; }
        public string FrequenciaRepeticao { get; set; }
        public DateTime? DataFimRepeticao { get; set; }
        public string DiasRepeticao { get; set; }
        public bool EhOcorrenciaGerada { get; set; }
        public int? AgendaPaiCodigo { get; set; }
    }
}
