namespace EvolutCRM.Models
{
    public class NotificacaoPainelModel
    {
        public string Chave { get; set; } = "";
        public string TipoOrigem { get; set; } = ""; // TICKET, CRM, AGENDA, CHAT
        public int CodigoOrigem { get; set; }

        public string Titulo { get; set; } = "";
        public string Mensagem { get; set; } = "";
        public string Tipo { get; set; } = "info";
        public string Url { get; set; } = "";
        public DateTime DataHora { get; set; } = DateTime.Now;

        // NOVO: itens agrupados dentro deste card
        public List<NotificacaoItemModel> Itens { get; set; } = new();
    }

    public class NotificacaoItemModel
    {
        public int Codigo { get; set; }
        public string Descricao { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTime DataHora { get; set; } = DateTime.Now;
    }
}