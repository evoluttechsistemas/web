namespace EvolutCRM.Models
{
    public class ChatInternoUsuarioModel
    {
        public string Usuario { get; set; } = "";
        public bool Online { get; set; }
        public DateTime? UltimaAtividade { get; set; }
    }

    public class ChatInternoConversaModel
    {
        // ── ChatInternoC ──────────────────────────────
        public int Codigo { get; set; }
        public string ChatTipo { get; set; } = "";   // INDIVIDUAL | GRUPO
        public string NomeGrupo { get; set; } = "";
        public string Participantes { get; set; } = "";   // "JOAO;MARIA;"
        public bool Ativa { get; set; } = true;

        // ── Calculados / exibição ─────────────────────
        public string Titulo { get; set; } = "";   // nome do outro ou do grupo
        public DateTime? UltimaMensagem { get; set; }
        public string UltimaAnotacao { get; set; } = "";   // preview última msg
        public string UltimoUsuario { get; set; } = "";
        public string UltimaMsgTipo { get; set; } = "TEXT";
        public int MensagensNaoLidas { get; set; }
    }

    public class ChatInternoMensagemModel
    {
        // ── ChatInternoD ──────────────────────────────
        public int Codigo { get; set; }
        public int CodConversa { get; set; }
        public string Usuario { get; set; } = "";
        public string Tipo { get; set; } = "TEXT"; // TEXT | IMAGE | FILE
        public string Texto { get; set; } = "";
        public DateTime DataHora { get; set; }
        public bool Excluida { get; set; }
        public string LidoPor { get; set; } = "";     // "JOAO;MARIA;"

        // ── Arquivo / imagem ──────────────────────────
        public string? ArquivoNome { get; set; }
        public string? ArquivoUrl { get; set; }
        public string? ArquivoMime { get; set; }
        public long? ArquivoTamanho { get; set; }

        // ── Resposta / quote ──────────────────────────
        public int? RespostaCodigo { get; set; }
        public string? RespostaUsuario { get; set; }
        public string? RespostaTexto { get; set; }

        // ── Reações (JSON) ────────────────────────────
        public string? Reacoes { get; set; }

        // ── Calculado no service ──────────────────────
        public bool MinhaMensagem { get; set; }
        public bool Lida { get; set; }  // true se o outro já leu
    }
}