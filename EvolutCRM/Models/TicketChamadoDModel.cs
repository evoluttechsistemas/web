using Microsoft.AspNetCore.Components;

namespace EvolutCRM.Models
{
    public class TicketChamadoDModel
    {
        public int Codigo { get; set; }
        public int CodTicketChamadoC { get; set; }
        public string Anotacao { get; set; } = "";
        public DateTime DataHora { get; set; }
        public string Usuario { get; set; } = "";
        public string? CaminhoImagem { get; set; }
        public string? NomeImagem { get; set; }
        public string EnvioCliente { get; set; }

        public int? CodMensagemRespondida { get; set; }
        public string? TextoMensagemRespondida { get; set; }
        public string? UsuarioMensagemRespondida { get; set; }
        public string telefoneWhatsApp { get; set; }

        public string NovaAgenda { get; set; } = "N";
        public DateTime? DataHoraAgenda { get; set; }
        public string AgendaResolvida { get; set; } = "N";
        public DateTime? UltimaNotificacaoAgenda { get; set; }
        public string NotificarAgenda { get; set; } = "S";

        public string? AudioUrl { get; set; }
        public string? AudioMimeType { get; set; }
        public string? AudioFileName { get; set; }
        public string? StatusEnvioWhatsApp { get; set; }
        // Em TicketChamadoDModel
        public string? AudioTranscrito { get; set; }  // 'S' ou 'N'

        public string MensagemExcluida { get; set; } = "N";
        public bool FoiExcluida => MensagemExcluida == "S";

        public string Alterado { get; set; } = "N";
        public bool FoiAlterado => Alterado == "S";
        public string TranscricaoAudio { get; set; } = "";

        public string VideoUrl { get; set; } = "";
        public string VideoMimeType { get; set; } = "";
        public string VideoFileName { get; set; } = "";
        public string Interno { get; set; } = "N";

    }
}