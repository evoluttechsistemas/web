using System;

namespace EvolutCRM.Services
{
    public class DeployNotificacaoState
    {
        public event Action? OnChange;

        public bool Ativo { get; private set; } = false;
        public string Mensagem { get; private set; } = "";
        public string Tipo { get; private set; } = "";
        public DateTime? FimUtc { get; private set; }  

        public void Notificar(string tipo, string mensagem, DateTime? fimUtc = null) 
        {
            Tipo = tipo;
            Mensagem = mensagem;
            FimUtc = fimUtc;                 
            Ativo = true;
            OnChange?.Invoke();
        }

        public void Limpar()
        {
            Ativo = false;
            Mensagem = "";
            Tipo = "";
            FimUtc = null;        
            OnChange?.Invoke();
        }
    }
}