namespace EvolutCRM.Models
{
    public class MensagemPresaModel
    {
        public int Codigo { get; set; }
        public int CodTicketChamadoC { get; set; }
        public string Anotacao { get; set; } = "";
        public string StatusWhatsApp { get; set; } = "";
        public string NomeCliente { get; set; } = "";
        public string MotivoErroEnvio { get; set; } = "";
    }
}
