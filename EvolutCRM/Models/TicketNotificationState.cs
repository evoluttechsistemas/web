namespace EvolutCRM.Models
{
    public class TicketNotificationState
    {
        public int Novos { get; private set; }

        public event Action? OnChange;

        public void SetNovos(int qtd)
        {
            if (Novos == qtd) return; // ← guard anti-piscar

            Novos = qtd;
            OnChange?.Invoke();
        }

        public void LimparNovos()
        {
            Novos = 0;
            OnChange?.Invoke();
        }
    }
}