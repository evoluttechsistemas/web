namespace HelpMobile.Services
{
    /// <summary>Menu lateral (abre/fecha) e o que o usuário pode ver nele.</summary>
    public class MenuState
    {
        public bool Aberto { get; private set; }

        /// <summary>null = ainda não consultado no HELP.</summary>
        public bool? PodeFinanceiro { get; set; }

        public event Action? Mudou;

        public void Abrir() { Aberto = true; Mudou?.Invoke(); }
        public void Fechar() { if (!Aberto) return; Aberto = false; Mudou?.Invoke(); }
        public void Limpar() { Aberto = false; PodeFinanceiro = null; Mudou?.Invoke(); }
    }
}
