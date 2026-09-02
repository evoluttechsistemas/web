namespace EvolutCRM.Models
{
    public class CardNotificationState
    {
        public int Novos { get; private set; }

        public event Action? OnChange;

        public void SetNovos(int qtd)
        {
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