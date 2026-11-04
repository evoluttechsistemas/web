namespace HelpMobile.Models
{
    // ─────────────────────────────────────────────────────────────
    // DTOs da API do HELP Mobile (/api/mobile/...)
    // Cópia dos DTOs do HELP (EvolutCRM/Mobile/MobileModels.cs). Mantenha os dois iguais.
    // ─────────────────────────────────────────────────────────────

    public class MobileLoginRequest
    {
        public string Usuario { get; set; } = "";
        public string Senha { get; set; } = "";
    }

    public class MobileLoginResponse
    {
        public string Token { get; set; } = "";
        public string Usuario { get; set; } = "";
        public int CodEmp { get; set; }
        public string Empresa { get; set; } = "";
    }

    public class MobileEuDto
    {
        public string Usuario { get; set; } = "";
        public int CodEmp { get; set; }
        public bool PodeResponder { get; set; }

        /// <summary>Acesso ao Financeiro (Baixa a Receber / Cobrança) — permissão ADMIN_ACESSAR.</summary>
        public bool PodeFinanceiro { get; set; }

        /// <summary>TICKETS_ALTERAR_STATUS: pode finalizar ticket.</summary>
        public bool PodeAlterarStatus { get; set; }
    }

    /// <summary>Um item da lista de conversas (estilo WhatsApp).</summary>
    public class MobileTicketItemDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string? FotoUrl { get; set; }
        public string Assunto { get; set; } = "";
        public string Responsavel { get; set; } = "";
        public int CodSituacao { get; set; }
        public string Situacao { get; set; } = "";
        public bool Novo { get; set; }
        public int Prioridade { get; set; }

        // Última mensagem (prévia)
        public string Previa { get; set; } = "";
        public string PreviaTipo { get; set; } = "texto"; // texto | imagem | audio | video | interno
        public bool PreviaDoCliente { get; set; }
        public string PreviaUsuario { get; set; } = "";
        public string PreviaStatus { get; set; } = "";
        public DateTime DataHora { get; set; }

        /// <summary>Mensagens do cliente depois da última resposta de um atendente.</summary>
        public int NaoRespondidas { get; set; }
        public int NaoLidas { get; set; }
    }

    public class MobileTicketDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string? FotoUrl { get; set; }
        public string Assunto { get; set; } = "";
        public string Responsavel { get; set; } = "";
        public int CodSituacao { get; set; }
        public string Situacao { get; set; } = "";
        public int CodCliente { get; set; }
        public string SentimentoEmoji { get; set; } = "";

        public bool PodeResponder { get; set; }
        public bool PodeAlterarStatus { get; set; }
        public bool Finalizado { get; set; }
        public bool SemCliente { get; set; }
        public bool TemWhatsApp { get; set; }
    }

    public class MobileMensagemDto
    {
        public int Codigo { get; set; }
        public string Texto { get; set; } = "";
        public DateTime DataHora { get; set; }
        public string Usuario { get; set; } = "";
        public bool DoCliente { get; set; }
        public bool Interna { get; set; }
        public bool Automatica { get; set; }
        public bool Sistema { get; set; }
        public bool Excluida { get; set; }
        public bool Alterada { get; set; }
        public string Status { get; set; } = "";

        public string? ImagemUrl { get; set; }
        public string? NomeArquivo { get; set; }
        public bool ArquivoEhImagem { get; set; }
        public string? AudioUrl { get; set; }
        public string? Transcricao { get; set; }
        public string? VideoUrl { get; set; }

        public int? CodRespondida { get; set; }
        public string? TextoRespondida { get; set; }
        public string? UsuarioRespondida { get; set; }
    }

    public class MobileAtualizacaoDto
    {
        public List<MobileMensagemDto> Novas { get; set; } = new();
        public Dictionary<int, string> Status { get; set; } = new();

        /// <summary>Só vem preenchido quando chegou mensagem nova (cabeçalho pode ter mudado).</summary>
        public MobileTicketDto? Ticket { get; set; }
    }

    /// <summary>Abrir a conversa: cabeçalho + últimas mensagens numa chamada só.</summary>
    public class MobileAbrirDto
    {
        public MobileTicketDto Ticket { get; set; } = new();
        public List<MobileMensagemDto> Mensagens { get; set; } = new();
        public bool TemMais { get; set; }
    }

    public class MobileTransferirRequest
    {
        public string Usuario { get; set; } = "";
    }

    public class MobileEnviarRequest
    {
        public string Texto { get; set; } = "";
        public bool Interna { get; set; }
        public int? CodRespondida { get; set; }
        public string? TextoRespondida { get; set; }
        public string? UsuarioRespondida { get; set; }
    }

    public class MobileEnviarResponse
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = "";
    }

    public class MobilePushInscricaoRequest
    {
        public string Endpoint { get; set; } = "";
        public string P256dh { get; set; } = "";
        public string Auth { get; set; } = "";
        public string Dispositivo { get; set; } = "";
    }

    public class MobilePushCancelarRequest
    {
        public string Endpoint { get; set; } = "";
    }

    public class MobileClienteDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
    }

    public class MobileVincularClienteRequest
    {
        public int CodCliente { get; set; }
    }

    // ── Novo ticket ──
    public class MobileClienteSugestaoDto
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = "";
        public string Contato { get; set; } = "";   // contato que bateu com a busca (ex.: "Gabriel (+1)")
    }

    public class MobileBuscaNovoTicketDto
    {
        public List<MobileClienteSugestaoDto> Clientes { get; set; } = new();
        public string Telefone { get; set; } = "";            // preenchido quando o que foi digitado é um telefone
        public string TelefoneFormatado { get; set; } = "";
        public bool OferecerNovoContato { get; set; }         // telefone que não existe → cadastrar contato
    }

    public class MobileContatoDto
    {
        public string Nome { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string TelefoneFormatado { get; set; } = "";
    }

    public class MobileContatosDto
    {
        public List<MobileContatoDto> Contatos { get; set; } = new();
        public bool Filtrado { get; set; }                    // true = a lista só tem os que bateram com a busca
    }

    public class MobileNovoTicketRequest
    {
        public int CodCliente { get; set; }
        public string NovoClienteNome { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string NomeContato { get; set; } = "";
        public bool ContatoNovo { get; set; }
        public string Mensagem { get; set; } = "";
    }

    public class MobileNovoTicketResponse
    {
        public bool Sucesso { get; set; }
        public int Codigo { get; set; }
        public string Mensagem { get; set; } = "";
    }

    /// <summary>Pessoa da equipe para a faixa do topo do app (quem está online e em que status).</summary>
    public class MobileEquipeMembroDto
    {
        public string Usuario { get; set; } = "";
        public bool Online { get; set; }
        public bool Disponivel { get; set; }
        public string Status { get; set; } = "";
        /// <summary>Cor do status (anel do avatar), ex.: #22c55e.</summary>
        public string Cor { get; set; } = "";
        /// <summary>Há quanto tempo está no status ("há 5 min"); vazio quando não se aplica.</summary>
        public string Tempo { get; set; } = "";
        public int Atendimentos { get; set; }
    }
}
