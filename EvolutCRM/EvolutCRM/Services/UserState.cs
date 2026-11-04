namespace EvolutCRM.Services
{
    public class UserState
    {
        public string CurrentUser { get; private set; } = string.Empty;
        public int CurrentCompanyId { get; private set; }
        public string CurrentCompany { get; private set; } = string.Empty;

        public bool IsReady { get; private set; }
        public bool IsOnline { get; private set; }

        public event Action? OnChange;

        public void SetUser(string usuario, int codEmpresa, string nomeEmpresa)
        {
            CurrentUser = usuario;
            CurrentCompanyId = codEmpresa;
            CurrentCompany = nomeEmpresa;
            IsReady = true;
            IsOnline = true;

            NotifyStateChanged();
        }

        public void SetOnline(bool online)
        {
            IsOnline = online;
            NotifyStateChanged();
        }

        public void Clear()
        {
            CurrentUser = string.Empty;
            CurrentCompany = string.Empty;
            CurrentCompanyId = 0;
            IsReady = false;
            IsOnline = false;

            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            OnChange?.Invoke();
        }
    }
}