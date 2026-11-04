namespace EvolutCRM.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Assistente IA do atendente (chat com a IA dentro do ticket)
// A IA do cliente (AiService.SugerirAsync) continua separada e restrita à base.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Uma mensagem do chat do assistente (pergunta do atendente ou resposta da IA).</summary>
public sealed class AssistenteMensagem
{
    public long Codigo { get; set; }

    /// <summary>"atendente" ou "ia".</summary>
    public string Papel { get; set; } = "atendente";

    public string Usuario { get; set; } = "";
    public string Texto { get; set; } = "";
    public DateTime DataHora { get; set; } = DateTime.Now;

    /// <summary>Miniaturas (data URL) das imagens anexadas — só para mostrar na tela.</summary>
    public List<string> Miniaturas { get; set; } = new();

    public List<AssistenteFonte> Fontes { get; set; } = new();
    public bool PesquisouWeb { get; set; }

    /// <summary>Documentações / casos que a busca interna devolveu para esta pergunta (encontrados, usados ou não).</summary>
    public int DocsEncontrados { get; set; }
    public int CasosEncontrados { get; set; }

    /// <summary>Resposta ainda chegando (streaming).</summary>
    public bool Gerando { get; set; }

    /// <summary>Texto de status enquanto gera ("Pesquisando na web...").</summary>
    public string Status { get; set; } = "";

    public bool Erro { get; set; }

    /// <summary>Modelo que respondeu, ex.: "gpt-5-mini (high)".</summary>
    public string Modelo { get; set; } = "";

    public bool EhIA => Papel == "ia";
}

/// <summary>Modelo que o atendente pode escolher no painel (lista em IA:AssistenteModelos no appsettings).</summary>
public sealed class AssistenteModeloOpcao
{
    public string Id { get; set; } = "";          // id da OpenAI: gpt-4.1-mini, gpt-5...
    public string Nome { get; set; } = "";        // nome na tela: "Rápido", "Raciocínio"...
    public string Descricao { get; set; } = "";

    /// <summary>Modelo de raciocínio: aceita o "esforço" de raciocínio da OpenAI.</summary>
    public bool Raciocinio { get; set; }

    /// <summary>Esforço de raciocínio usado neste nível (low / medium / high). Só vale para modelo de raciocínio.</summary>
    public string? Esforco { get; set; }
}

/// <summary>De onde veio a informação: documentação (D1), caso anterior (C1) ou página da web.</summary>
public sealed class AssistenteFonte
{
    public string Id { get; set; } = "";         // D1, C2, W1...
    public string Tipo { get; set; } = "";       // doc | caso | web
    public string Titulo { get; set; } = "";
    public string Url { get; set; } = "";        // web: link; doc: /curso-tutorial/slug; caso: /ticket/123
    public int CodDoc { get; set; }
    public int CodTicket { get; set; }
    public string Slug { get; set; } = "";
    public float Score { get; set; }

    /// <summary>Trecho da documentação ou "caso + solução" — para o atendente conferir.</summary>
    public string Texto { get; set; } = "";

    /// <summary>A IA citou esta fonte na resposta.</summary>
    public bool Citada { get; set; }
}

/// <summary>Imagem que vai junto com a pergunta.</summary>
public sealed class AssistenteImagem
{
    /// <summary>"colado" (Ctrl+V, arrastar, anexar) ou "ticket" (mensagem do cliente).</summary>
    public string Origem { get; set; } = "colado";

    /// <summary>Data URL JPEG já reduzida (colada). Para "ticket" é carregada do banco.</summary>
    public string? DataUrl { get; set; }

    /// <summary>Codigo em TicketChamadoD quando a imagem é do ticket.</summary>
    public int CodTicketChamadoD { get; set; }

    /// <summary>Para mostrar na tela antes de enviar.</summary>
    public string Preview { get; set; } = "";
}

/// <summary>Tudo que a IA precisa saber do atendimento (ticket, card do comercial ou nenhum = modo geral).</summary>
public sealed class AssistenteContextoTicket
{
    /// <summary>"ticket", "card" ou "geral" (lista de tickets / funil comercial, sem conversa aberta).</summary>
    public string Origem { get; set; } = "ticket";

    /// <summary>"suporte" ou "comercial": muda o foco da IA.</summary>
    public string Modo { get; set; } = "suporte";

    public int CodTicket { get; set; }
    public int CodCard { get; set; }

    /// <summary>Cliente do ticket (para buscar o histórico de treinamento).</summary>
    public int CodCliente { get; set; }

    /// <summary>Produtos/serviços do card, funil, temperatura... (comercial).</summary>
    public string? Detalhes { get; set; }
    public int CodEmp { get; set; }
    public string Usuario { get; set; } = "";
    public string? Assunto { get; set; }
    public string? Cliente { get; set; }
    public string? Versao { get; set; }
    public string? Situacao { get; set; }

    /// <summary>Conversa do ticket, mais antiga primeiro: "[dd/MM HH:mm] Cliente: ...".</summary>
    public string Conversa { get; set; } = "";

    /// <summary>Últimas mensagens do cliente (para a busca nas documentações).</summary>
    public string UltimasDoCliente { get; set; } = "";

    public int TotalMensagens { get; set; }
}

/// <summary>Pergunta do atendente.</summary>
public sealed class AssistentePergunta
{
    public string Texto { get; set; } = "";
    public List<AssistenteImagem> Imagens { get; set; } = new();

    /// <summary>Modelo escolhido no painel (validado contra a lista permitida). Vazio = padrão.</summary>
    public string? Modelo { get; set; }

    /// <summary>Esforço de raciocínio: "low", "medium" ou "high" (só para modelos de raciocínio).</summary>
    public string? Esforco { get; set; }
}

/// <summary>O que a IA devolveu no final.</summary>
public sealed class AssistenteResultado
{
    public bool Ok { get; set; }
    public string Texto { get; set; } = "";
    public string Erro { get; set; } = "";
    public List<AssistenteFonte> Fontes { get; set; } = new();
    public bool PesquisouWeb { get; set; }
    public int DocsEncontrados { get; set; }
    public int CasosEncontrados { get; set; }
    public int TokensEntrada { get; set; }
    public int TokensSaida { get; set; }
    public int DuracaoMs { get; set; }
    public string Modelo { get; set; } = "";
    public long CodMensagemIA { get; set; }
    public long CodMensagemAtendente { get; set; }
}

/// <summary>Rascunho de insight gerado da conversa com o assistente, para o atendente revisar antes de salvar.</summary>
public sealed class AssistenteInsightRascunho
{
    public string TopicoTitulo { get; set; } = "";
    public string PerguntaCliente { get; set; } = "";
    public string Resumo { get; set; } = "";
    public string Categoria { get; set; } = "outro";
    public string CausaProvavel { get; set; } = "";
    public string Solucao { get; set; } = "";
    public string Tags { get; set; } = "";   // separadas por vírgula na tela
}
