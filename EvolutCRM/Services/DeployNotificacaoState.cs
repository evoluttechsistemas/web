using System;

namespace EvolutCRM.Services
{
    public class DeployNotificacaoState
    {
        public event Action? OnChange;

        public bool Ativo { get; private set; } = false;
        public string Mensagem { get; private set; } = "";
        public string Tipo { get; private set; } = ""; // "aviso5min" ou "aviso30seg"

        public void Notificar(string tipo, string mensagem)
        {
            Tipo = tipo;
            Mensagem = mensagem;
            Ativo = true;
            OnChange?.Invoke();
        }

        public void Limpar()
        {
            Ativo = false;
            Mensagem = "";
            Tipo = "";
            OnChange?.Invoke();
        }
    }
}