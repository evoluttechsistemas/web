namespace EvolutCRM.Models
{
    public class TicketsPendentesDto
    {
        public int Novos { get; set; }
        public int Atualizar { get; set; }

        public int UltimoTicketId { get; set; }
        public int UltimoTicketAtualizarId { get; set; }

        public List<int> TicketsAtualizar { get; set; } = new();
        public List<int> TicketsNovos { get; set; } = new();
    }
}
