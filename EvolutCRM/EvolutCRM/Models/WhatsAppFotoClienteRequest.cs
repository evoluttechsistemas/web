namespace EvolutCRM.Models
{
    public class WhatsAppFotoClienteRequest
    {
        public int CodEmp { get; set; }
        public string Telefone { get; set; } = "";
        public string Jid { get; set; } = "";
        public string FotoBase64 { get; set; } = "";
        public string ContentType { get; set; } = "image/jpeg";
    }
}
