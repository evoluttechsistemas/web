namespace EvolutCRM.Models
{
    public sealed class TicketInsight
    {
        public int topicoIndex { get; set; } = 1;
        public string? topicoTitulo { get; set; }

        public string? perguntaCliente { get; set; }
        public string? resumo { get; set; }
        public string? categoria { get; set; }
        public string? intencao { get; set; }
        public string? gravidade { get; set; }
        public List<string>? tags { get; set; }
        public string? causaProvavel { get; set; }
        public string? solucao { get; set; }
        public bool solucaoConfirmada { get; set; }
        public int confianca { get; set; }
    }
}