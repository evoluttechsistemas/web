namespace EvolutCRM.Services
{
    public class ChatInternoNotificationState
    {
        private int _novas;

        public int Novas => _novas;

        public event Action? OnChange;

        public void SetNovas(int valor)
        {
            if (_novas == valor)
                return;              // ← nada mudou: não dispara render

            _novas = valor;
            OnChange?.Invoke();
        }
    }
}