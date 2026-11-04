using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Generic;

namespace EvolutTur.Models
{
    public class Configuracoes
    {
        // Dependências
        public IJSRuntime JS { get; set; }

        // Controle de UI
        public InputFile InputLogoElement { get; set; }
        public int AbaAtiva { get; set; } = 1;
        public string MensagemSucesso { get; set; } = string.Empty;
        public string MensagemErro { get; set; } = string.Empty;
        public ElementReference InputFotoPerfilElement { get; set; }

     

        // Usuários
        public List<Configuracoes> Usuarios { get; set; } = new List<Configuracoes>();
        public int IdUsuario { get; set; }
        public string NomeUsuario { get; set; }
        public string EmailUsuario { get; set; }
        public PerfilAgente PerfilUsuario { get; set; }
        public bool AtivoUsuario { get; set; }
        public string FotoPerfilUrlUsuario { get; set; }
        public bool MostrarModalUsuario { get; set; } = false;
        public string SenhaTemporaria { get; set; } = string.Empty;
             

        public enum PerfilAgente
        {
            Agente,
            Operador,
            Supervisor
        }
    }

    
}
