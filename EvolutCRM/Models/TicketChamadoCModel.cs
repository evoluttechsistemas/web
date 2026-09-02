using Microsoft.AspNetCore.Components;

namespace EvolutCRM.Models
{
    public class TicketChamadoCModel
    {
        public int Codigo { get; set; }
        public int Status { get; set; }
        public int CodSetor { get; set; }
        public int CodTipo { get; set; }
        public string Versao { get; set; }
        public int? CodCategoria { get; set; }
        public DateTime DataAbertura { get; set; }
        public DateTime DataHoraAbertura { get; set; }
        public string Usuario { get; set; } = "";
        public string UsuarioUltimaGravacao { get; set; } = "";
        public DateTime DataHoraUltimaGravacao { get; set; }
        public int CodCliente { get; set; }
        public string Assunto { get; set; } = "";
        public int CodSituacao { get; set; }
        public string Novo { get; set; } = "S";
        public string? NomeCliente { get; set; }
        public string? ApelidoCliente { get; set; }
        public string TelefoneCliente { get; set; } = "";
        public string? UsuarioAbertura { get; set; }
        public int Prioridade { get; set; } = 2;
        public string? TelefoneWhatsApp { get; set; }
        public string? StatusAprovacao { get; set; }
        // Em TicketChamadoCModel:
        public int? CodInstanciaWhatsApp { get; set; }

        public string? FotoClienteUrl { get; set; }

        public string? AssuntoSugerido { get; set; }
        public string? SentimentoCliente { get; set; }  // 'positivo','neutro','negativo','frustrado','urgente'
        public string? SentimentoEmoji { get; set; }     // '😊','😐','😠','😤','🚨'
        public string? AssuntoSugeridoStatus { get; set; } // 'pendente','aplicado','ignorado'
        public string? ObservacaoCliente { get; set; }
    }

    public class ClienteModel
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Apelido { get; set; } = "";
    }

    public class UsuarioClienteDto
    {
        public int Codigo { get; set; }
        public int CodCliente { get; set; }
        public string Usuario { get; set; } = "";
    }

}