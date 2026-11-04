using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Ticket de WhatsApp do Suporte parado (sem mensagem do cliente nem do atendente): NÃO finaliza sozinho,
/// abre o aviso no meio da tela do responsável perguntando se pode finalizar (FinalizacaoConfirmacaoService
/// + MainLayout), seja a última mensagem do cliente ou do atendente. O que está abaixo sobre finalizar
/// sozinho (FinalizarAsync) ficou sem uso.
/// Roda a cada minuto. Parâmetros por empresa (grupo TICKET):
///  - TICKET_FINALIZAR_AUTOMATICO (S/N, padrão N): liga/desliga;
///  - TICKET_FINALIZAR_AUTOMATICO_MINUTOS (padrão 60, mínimo 15): tempo parado para finalizar;
///  - TICKET_FINALIZAR_AUTOMATICO_MENSAGEM: o aviso enviado ao cliente ({tempo} vira "1 hora", "30 minutos"...).
///
/// Segue a mesma lógica de quando o atendente finaliza:
///  - A pesquisa de satisfação continua com o EnvioWhatsAppBackgroundService (1 por ticket e no máximo
///    1 por número a cada 24h). Aqui só se olha se ela VAI sair:
///      vai sair  → antes de finalizar manda o aviso "conversa sendo finalizada automaticamente" e depois vem a pesquisa;
///      não vai   → finaliza em silêncio (o cliente já recebeu pesquisa hoje), sem aviso.
///  - Registra o treinamento do cliente. O insight é perguntado ao responsável (aviso "gerar insight?").
///  - NÃO resolve a agenda do ticket: ela continua pendente até ser resolvida na tela de Agendas.
///  - Deixa uma nota interna no ticket dizendo que foi finalizado automaticamente.
///
/// Não importa de quem foi a última mensagem: passou o tempo sem nenhuma mensagem, finaliza.
/// Cliente aguardando: se a última mensagem é do cliente (e não é só "ok", "obrigado"...), o ticket NÃO é
/// finalizado. Abre o aviso no meio da tela do responsável (FinalizacaoConfirmacaoService + MainLayout)
/// perguntando se finaliza. "Não finalizar" recomeça a contagem; se o cliente continuar sem resposta,
/// o aviso volta depois do mesmo tempo, e assim por diante.
///
/// Pausa: enquanto o responsável pelo ticket está num status que não conta como disponível (almoço,
/// por exemplo), a contagem fica travada e continua de onde parou quando ele volta. O tempo pausado de cada
/// ticket fica em dbo.TicketFinalizacaoPausa (zera quando chega mensagem nova).
/// Só fica de fora o ticket que o atendente também não conseguiria finalizar:
///  - setor Suporte (1) e situação 1 (em aberto), de WhatsApp, com cliente vinculado e responsável; setor 2 precisa de versão.
/// Ticket parado há mais de 24h não é mexido (o WhatsApp também não envia mensagem com mais de 24h).
/// </summary>
public sealed class FinalizacaoAutomaticaService : BackgroundService
{
    public const string ParamLigado = "TICKET_FINALIZAR_AUTOMATICO";
    public const string ParamMinutos = "TICKET_FINALIZAR_AUTOMATICO_MINUTOS";
    public const string ParamMensagem = "TICKET_FINALIZAR_AUTOMATICO_MENSAGEM";
    public const string UsuarioAutomatico = "AUTOMATICO";

    private const string DescLigado =
        "Finaliza sozinho o ticket de WhatsApp em Suporte parado (sem mensagem do cliente nem do atendente) pelo tempo de " +
        "TICKET_FINALIZAR_AUTOMATICO_MINUTOS. Se for sair a pesquisa de satisfacao, avisa o cliente antes. S=SIM N=NAO. PADRAO=N";
    private const string DescMinutos =
        "Minutos sem nenhuma mensagem (cliente ou atendente) para o HELP finalizar o ticket sozinho. Minimo 15. PADRAO=60";
    internal const string DescMensagem =
        "Aviso enviado ao cliente quando o ticket e finalizado automaticamente (so quando a pesquisa de satisfacao vai sair). " +
        "{tempo} vira o tempo parado. Use \\n para pular linha.";
    internal const string MensagemPadrao =
        "Olá! Como não tivemos interação há {tempo}, esta conversa está sendo finalizada automaticamente.\\n\\n" +
        "Se precisar de algo, é só mandar uma nova mensagem que iniciamos um novo atendimento. " +
        "E se ficamos de te retornar sobre algum assunto, pode ficar tranquilo(a): nós mesmos entraremos em contato.";

    // Os mesmos do EnvioWhatsAppBackgroundService (pesquisa de satisfação)
    internal const string ParamPesquisa = "TICKET_ENVIAR_PESQUISA_SATISFACAO";
    internal const string DescPesquisa =
        "Define se o sistema envia pesquisa de satisfacao ao finalizar o atendimento. S=SIM N=NAO. PADRAO=N";

    private const int MinutosPadrao = 60;
    private const int MinutosMinimo = 15;
    private const int JanelaHoras = 24;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    // Quem não é atendente de verdade (mesmo critério do TicketQueryRepository / EnvioWhatsApp)
    private static readonly HashSet<string> UsuariosAutomaticos = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "WHATSAPP", "CLIENTE", "BOT", "IA", "SISTEMA", "NOVO", "EVOLUTTECH", "POR WHATS", "PESQUISA", UsuarioAutomatico
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InsightAutomaticoService _insightAuto;
    private readonly string _cs;

    // Ticket que ficou de fora (sem cliente, cliente esperando resposta...): loga uma vez por "parada"
    private readonly ConcurrentDictionary<int, DateTime> _jaAvisado = new();

    public FinalizacaoAutomaticaService(IServiceScopeFactory scopeFactory, InsightAutomaticoService insightAuto,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _insightAuto = insightAuto;
        _cs = config.GetConnectionString("Connection")!;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }   // deixa o sistema subir
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await VerificarAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogHelper.Log("[FINALIZACAO AUTO] " + ex.Message);
            }

            try { await Task.Delay(Intervalo, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private sealed record Candidato(int Ticket, int CodEmp, int CodCliente, int CodSetor, string Versao,
        string Usuario, string Telefone, DateTime UltimaInteracao,
        bool ResponsavelPausado, string StatusResponsavel, DateTime? InicioStatus,
        DateTime? PausaUltimaInteracao, int PausaMinutos, DateTime? PausaInicio,
        DateTime? PerguntaUltimaInteracao, string PerguntaStatus, DateTime? PerguntaDecididoEm);

    /// <summary>Pausa do ticket depois de olhar o status do responsável agora.</summary>
    private sealed record EstadoPausa(int MinutosFechados, DateTime? Inicio, bool Mudou)
    {
        public double TotalMinutos(DateTime agora) =>
            MinutosFechados + (Inicio.HasValue ? Math.Max(0, (agora - Inicio.Value).TotalMinutes) : 0);
    }

    private sealed record ConfigEmpresa(bool Ligado, int Minutos, string Mensagem, bool Pesquisa);

    private async Task VerificarAsync(CancellationToken ct)
    {
        var candidatos = await BuscarCandidatosAsync(ct);
        if (candidatos.Count == 0) return;

        var configs = new Dictionary<int, ConfigEmpresa>();
        var agora = DateTime.Now;

        if (agora - _ultimaLimpezaPausa > TimeSpan.FromHours(1))
        {
            _ultimaLimpezaPausa = agora;
            await LimparPausasAsync(ct);
        }

        foreach (var c in candidatos)
        {
            ct.ThrowIfCancellationRequested();

            if (!configs.TryGetValue(c.CodEmp, out var cfg))
            {
                cfg = await LerConfigAsync(c.CodEmp);
                configs[c.CodEmp] = cfg;
            }
            if (!cfg.Ligado) continue;

            try
            {
                // Aviso "cliente aguardando" deste mesmo período parado
                var perguntaVale = c.PerguntaUltimaInteracao.HasValue && c.PerguntaUltimaInteracao.Value == c.UltimaInteracao;
                if (!perguntaVale && c.PerguntaUltimaInteracao.HasValue)
                    await ApagarPerguntaAsync(c.Ticket, ct);            // teve mensagem nova: o aviso antigo não vale
                if (perguntaVale && c.PerguntaStatus == "PENDENTE")
                    continue;                                           // o aviso já está na tela do responsável

                // "Não finalizar": conta de novo a partir da decisão
                var baseContagem = perguntaVale && c.PerguntaStatus == "MANTIDO"
                                   && c.PerguntaDecididoEm.HasValue && c.PerguntaDecididoEm.Value > c.UltimaInteracao
                    ? c.PerguntaDecididoEm.Value
                    : c.UltimaInteracao;

                // Responsável em almoço (ou outro status indisponível): a contagem fica travada
                var pausa = CalcularPausa(c, agora, baseContagem);
                if (pausa.Mudou)
                    await GravarPausaAsync(c, pausa, ct);
                if (c.ResponsavelPausado) continue;

                var minutosPausados = pausa.TotalMinutos(agora);
                if ((agora - baseContagem).TotalMinutes - minutosPausados < cfg.Minutos) continue;

                var motivo = await MotivoParaNaoFinalizarAsync(c, ct);
                if (motivo != null)
                {
                    // Loga só uma vez enquanto o ticket continuar parado do mesmo jeito
                    if (!_jaAvisado.TryGetValue(c.Ticket, out var ult) || ult != c.UltimaInteracao)
                    {
                        _jaAvisado[c.Ticket] = c.UltimaInteracao;
                        LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket}: não finalizado ({motivo}).");
                    }
                    continue;
                }

                // Não finaliza sozinho: pergunta ao responsável (aviso no meio da tela),
                // seja a última mensagem do cliente ou do atendente
                await CriarPerguntaAsync(c, ct);
                _jaAvisado.TryRemove(c.Ticket, out _);
                LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket}: parado há {DescreverTempo(cfg.Minutos)}; aviso para finalizar enviado para {c.Usuario}.");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tickets de WhatsApp em Suporte parados (de 1 minuto até 24 horas), com o status do responsável
    /// e a pausa já gravada. A partir de 1 minuto para registrar desde cedo o almoço do responsável.
    /// </summary>
    private async Task<List<Candidato>> BuscarCandidatosAsync(CancellationToken ct)
    {
        var lista = new List<Candidato>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await GarantirTabelaPausaAsync(conn, ct);
        await FinalizacaoConfirmacaoService.GarantirTabelaAsync(conn, ct);
        await using var cmd = new SqlCommand(@"
SELECT C.Codigo, C.CodEmp, ISNULL(C.CodCliente, 0), ISNULL(C.CodSetor, 0), ISNULL(C.Versao, ''),
       LTRIM(RTRIM(ISNULL(C.Usuario, ''))), ISNULL(C.TelefoneWhatsApp, ''),
       CASE WHEN C.DataHoraUltimaGravacao IS NOT NULL AND C.DataHoraUltimaGravacao > U.Ultima
            THEN C.DataHoraUltimaGravacao ELSE U.Ultima END,
       ISNULL(R.Pausado, 0), ISNULL(R.StatusDescricao, ''), R.DataHoraStatus,
       P.UltimaInteracao, ISNULL(P.MinutosPausados, 0), P.PausaInicio,
       Q.UltimaInteracao, ISNULL(Q.Status, ''), Q.DecididoEm
FROM TicketChamadoC C
CROSS APPLY (SELECT MAX(D.DataHora) AS Ultima
             FROM TicketChamadoD D
             WHERE D.CodTicketChamadoC = C.Codigo AND D.CodEmp = C.CodEmp) U
-- Status do responsável: num status que não conta como disponível (ex.: almoço) a contagem pausa
OUTER APPLY (SELECT TOP 1
                    CASE WHEN US.CodUsuarioStatus IS NOT NULL AND ISNULL(S.Disponivel, 'S') = 'N' THEN 1 ELSE 0 END AS Pausado,
                    S.Descricao AS StatusDescricao,
                    US.DataHoraStatus
             FROM Usuario US
             LEFT JOIN UsuarioStatus S ON S.CodEmp = US.CodEmp AND S.Codigo = US.CodUsuarioStatus
             WHERE US.CodEmp = C.CodEmp
               AND UPPER(LTRIM(RTRIM(US.Usuario))) = UPPER(LTRIM(RTRIM(ISNULL(C.Usuario, ''))))) R
LEFT JOIN dbo.TicketFinalizacaoPausa P ON P.CodTicketChamadoC = C.Codigo
LEFT JOIN dbo.TicketFinalizacaoPergunta Q ON Q.CodTicketChamadoC = C.Codigo
WHERE ISNULL(C.CodSituacao, 1) = 1          -- só em aberto (situação Suporte)
  AND ISNULL(C.CodSetor, 1) = 1             -- só tickets do setor Suporte (nunca Desenvolvimento ou outro)
  AND ISNULL(C.ChatInterno, 'N') <> 'S'
  AND ISNULL(C.TelefoneWhatsApp, '') <> ''
  AND U.Ultima IS NOT NULL
  AND U.Ultima >= DATEADD(HOUR, -@janela, GETDATE())
  AND U.Ultima <= DATEADD(MINUTE, -@minimo, GETDATE())
  AND ISNULL(C.DataHoraUltimaGravacao, U.Ultima) <= DATEADD(MINUTE, -@minimo, GETDATE());", conn);
        cmd.Parameters.AddWithValue("@janela", JanelaHoras);
        cmd.Parameters.AddWithValue("@minimo", 1);
        cmd.CommandTimeout = 60;

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add(new Candidato(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3),
                rd.GetString(4), rd.GetString(5), rd.GetString(6), rd.GetDateTime(7),
                rd.GetInt32(8) == 1, rd.GetString(9), rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                rd.IsDBNull(11) ? null : rd.GetDateTime(11), rd.GetInt32(12), rd.IsDBNull(13) ? null : rd.GetDateTime(13),
                rd.IsDBNull(14) ? null : rd.GetDateTime(14), rd.GetString(15), rd.IsDBNull(16) ? null : rd.GetDateTime(16)));
        return lista;
    }

    // ───────────────────────── Pausa (responsável em almoço) ─────────────────────────

    private static volatile bool _tabelaPausaOk;
    private DateTime _ultimaLimpezaPausa = DateTime.MinValue;

    private static async Task GarantirTabelaPausaAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_tabelaPausaOk) return;
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.TicketFinalizacaoPausa', 'U') IS NULL
    CREATE TABLE dbo.TicketFinalizacaoPausa
    (
        CodTicketChamadoC INT      NOT NULL CONSTRAINT PK_TicketFinalizacaoPausa PRIMARY KEY,
        CodEmp            INT      NOT NULL,
        UltimaInteracao   DATETIME NOT NULL,   -- a pausa vale para este período parado; mensagem nova zera
        MinutosPausados   INT      NOT NULL CONSTRAINT DF_TicketFinalizacaoPausa_Min DEFAULT (0),
        PausaInicio       DATETIME NULL        -- preenchido enquanto o responsável continua em pausa
    );", conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _tabelaPausaOk = true;
    }

    /// <summary>
    /// Pausas fechadas + a pausa aberta. Se o responsável entrou em almoço agora, a pausa começa quando ele
    /// mudou o status (nunca antes da última mensagem); quando ele volta, o tempo é somado e a contagem segue.
    /// </summary>
    private static EstadoPausa CalcularPausa(Candidato c, DateTime agora, DateTime baseContagem)
    {
        var mesmoPeriodo = c.PausaUltimaInteracao.HasValue && c.PausaUltimaInteracao.Value == c.UltimaInteracao;
        var minutos = mesmoPeriodo ? c.PausaMinutos : 0;
        var inicio = mesmoPeriodo ? c.PausaInicio : null;

        if (c.ResponsavelPausado && !inicio.HasValue)
        {
            var desde = c.InicioStatus ?? agora;
            if (desde < baseContagem) desde = baseContagem;
            if (desde > agora) desde = agora;
            inicio = desde;
        }
        else if (!c.ResponsavelPausado && inicio.HasValue)
        {
            minutos += (int)Math.Round(Math.Max(0, (agora - inicio.Value).TotalMinutes));
            inicio = null;
        }

        // Só grava quando muda; ticket sem pausa nenhuma não ganha linha
        var temLinha = c.PausaUltimaInteracao.HasValue;
        var mudou = temLinha
            ? (!mesmoPeriodo || minutos != c.PausaMinutos || inicio != c.PausaInicio)
            : (minutos > 0 || inicio.HasValue);

        return new EstadoPausa(minutos, inicio, mudou);
    }

    private async Task GravarPausaAsync(Candidato c, EstadoPausa pausa, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
MERGE dbo.TicketFinalizacaoPausa AS A
USING (SELECT @t AS CodTicketChamadoC) AS N ON A.CodTicketChamadoC = N.CodTicketChamadoC
WHEN MATCHED THEN UPDATE SET CodEmp = @e, UltimaInteracao = @ultima, MinutosPausados = @min, PausaInicio = @ini
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, CodEmp, UltimaInteracao, MinutosPausados, PausaInicio)
                      VALUES (@t, @e, @ultima, @min, @ini);", conn);
        cmd.Parameters.AddWithValue("@t", c.Ticket);
        cmd.Parameters.AddWithValue("@e", c.CodEmp);
        cmd.Parameters.Add("@ultima", SqlDbType.DateTime).Value = c.UltimaInteracao;
        cmd.Parameters.AddWithValue("@min", pausa.MinutosFechados);
        cmd.Parameters.Add("@ini", SqlDbType.DateTime).Value = pausa.Inicio.HasValue ? pausa.Inicio.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);

        if (c.ResponsavelPausado && !c.PausaInicio.HasValue)
            LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket}: contagem pausada ({c.Usuario} em \"{c.StatusResponsavel}\").");
    }

    // ───────────────────────── Cliente aguardando resposta ─────────────────────────

    /// <summary>
    /// A última mensagem de verdade (sem notas internas e sem mensagens automáticas) é do cliente
    /// e tem conteúdo — não é só "ok", "obrigado", "bom dia"... Print ou áudio contam como conteúdo.
    /// </summary>
    private async Task<bool> ClienteAguardandoAsync(Candidato c, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SELECT ISNULL(D.EnvioCliente, 'N'), LTRIM(RTRIM(ISNULL(D.Usuario, ''))),
       ISNULL(D.Anotacao, '') + ' ' + ISNULL(D.TranscricaoAudio, ''),
       CASE WHEN D.Imagem IS NOT NULL OR D.Audio IS NOT NULL OR D.Video IS NOT NULL THEN 1 ELSE 0 END
FROM TicketChamadoD D
WHERE D.CodTicketChamadoC = @t AND D.CodEmp = @e
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND ISNULL(D.Interno, 'N') <> 'S'
  AND ISNULL(D.StatusWhatsApp, '') NOT IN ('ia_processing', 'saudacao_auto', 'aguarde_auto', 'interno')
ORDER BY D.DataHora DESC, D.Codigo DESC;", conn);
        cmd.Parameters.AddWithValue("@t", c.Ticket);
        cmd.Parameters.AddWithValue("@e", c.CodEmp);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var cliente = rd.GetString(0) == "S";
            var usuario = rd.GetString(1);
            if (!cliente && UsuariosAutomaticos.Contains(usuario)) continue;   // aviso automático, pesquisa, bot...
            if (!cliente) return false;                                        // o atendente respondeu por último

            var temMidia = rd.GetInt32(3) == 1;
            return temMidia || InsightAutomaticoService.ConteudoUtil(rd.GetString(2)).Length > 0;
        }
        return false;
    }

    private async Task CriarPerguntaAsync(Candidato c, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
MERGE dbo.TicketFinalizacaoPergunta AS A
USING (SELECT @t AS CodTicketChamadoC) AS N ON A.CodTicketChamadoC = N.CodTicketChamadoC
WHEN MATCHED THEN UPDATE SET CodEmp = @e, UltimaInteracao = @ultima, Status = 'PENDENTE',
                             PerguntadoEm = GETDATE(), DecididoEm = NULL, DecididoPor = NULL
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, CodEmp, UltimaInteracao, Status, PerguntadoEm)
                      VALUES (@t, @e, @ultima, 'PENDENTE', GETDATE());", conn);
        cmd.Parameters.AddWithValue("@t", c.Ticket);
        cmd.Parameters.AddWithValue("@e", c.CodEmp);
        cmd.Parameters.Add("@ultima", SqlDbType.DateTime).Value = c.UltimaInteracao;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Aviso de insight para o responsável (Status INSIGHT; o ticket já está finalizado).</summary>
    private async Task CriarPerguntaInsightAsync(Candidato c)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await FinalizacaoConfirmacaoService.GarantirTabelaAsync(conn);
        await using var cmd = new SqlCommand(@"
MERGE dbo.TicketFinalizacaoPergunta AS A
USING (SELECT @t AS CodTicketChamadoC) AS N ON A.CodTicketChamadoC = N.CodTicketChamadoC
WHEN MATCHED THEN UPDATE SET CodEmp = @e, UltimaInteracao = @ultima, Status = 'INSIGHT',
                             PerguntadoEm = GETDATE(), DecididoEm = NULL, DecididoPor = NULL
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, CodEmp, UltimaInteracao, Status, PerguntadoEm)
                      VALUES (@t, @e, @ultima, 'INSIGHT', GETDATE());", conn);
        cmd.Parameters.AddWithValue("@t", c.Ticket);
        cmd.Parameters.AddWithValue("@e", c.CodEmp);
        cmd.Parameters.Add("@ultima", SqlDbType.DateTime).Value = c.UltimaInteracao;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ApagarPerguntaAsync(int ticket, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.TicketFinalizacaoPergunta WHERE CodTicketChamadoC = @t;", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Apaga a pausa de tickets que já não estão em aberto (de hora em hora).</summary>
    private async Task LimparPausasAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);
            await GarantirTabelaPausaAsync(conn, ct);
            await FinalizacaoConfirmacaoService.GarantirTabelaAsync(conn, ct);
            await using var cmd = new SqlCommand(@"
DELETE P
FROM dbo.TicketFinalizacaoPausa P
LEFT JOIN TicketChamadoC C ON C.Codigo = P.CodTicketChamadoC AND C.CodEmp = P.CodEmp
WHERE C.Codigo IS NULL
   OR ISNULL(C.CodSituacao, 1) <> 1
   OR P.UltimaInteracao < DATEADD(DAY, -2, GETDATE());

DELETE Q
FROM dbo.TicketFinalizacaoPergunta Q
LEFT JOIN TicketChamadoC C ON C.Codigo = Q.CodTicketChamadoC AND C.CodEmp = Q.CodEmp
WHERE C.Codigo IS NULL
   -- aviso pendente (ou ""não finalizar""): vale enquanto o ticket está aberto, por até 7 dias
   -- (ticket parado na sexta ainda pergunta na segunda)
   OR (Q.Status <> 'INSIGHT' AND (ISNULL(C.CodSituacao, 1) <> 1 OR ISNULL(Q.DecididoEm, Q.PerguntadoEm) < DATEADD(DAY, -7, GETDATE())))
   -- aviso de insight: vale enquanto o ticket está finalizado, por até 7 dias
   OR (Q.Status = 'INSIGHT' AND (ISNULL(C.CodSituacao, 1) <> 3 OR Q.PerguntadoEm < DATEADD(DAY, -7, GETDATE())));", conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            LogHelper.Log("[FINALIZACAO AUTO] Limpeza das pausas: " + ex.Message);
        }
    }

    private async Task<ConfigEmpresa> LerConfigAsync(int codEmp)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var p = scope.ServiceProvider.GetRequiredService<ParametroDinamicoService>();

            var ligado = await p.BuscarBoolAsync(codEmp, ParamLigado, "TICKET", DescLigado, false);
            if (!ligado) return new ConfigEmpresa(false, MinutosPadrao, "", false);

            var minTxt = await p.BuscarValorAsync(codEmp, ParamMinutos, "TICKET", DescMinutos, MinutosPadrao.ToString());
            var minutos = int.TryParse((minTxt ?? "").Trim(), out var m) ? m : MinutosPadrao;
            if (minutos < MinutosMinimo) minutos = MinutosMinimo;

            var msg = await p.BuscarValorAsync(codEmp, ParamMensagem, "TICKET", DescMensagem, MensagemPadrao);
            if (string.IsNullOrWhiteSpace(msg)) msg = MensagemPadrao;

            var pesquisa = await p.BuscarBoolAsync(codEmp, ParamPesquisa, "TICKET", DescPesquisa, false);

            return new ConfigEmpresa(true, minutos, msg, pesquisa);
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[FINALIZACAO AUTO] Empresa {codEmp}: não leu os parâmetros ({ex.Message}).");
            return new ConfigEmpresa(false, MinutosPadrao, "", false);   // na dúvida, não finaliza
        }
    }

    /// <summary>Devolve o motivo para NÃO finalizar, ou null quando pode finalizar.</summary>
    private static Task<string?> MotivoParaNaoFinalizarAsync(Candidato c, CancellationToken ct)
    {
        string? motivo = null;
        if (c.CodCliente <= 0) motivo = "sem cliente vinculado";
        else if (UsuariosAutomaticos.Contains(c.Usuario)) motivo = "sem responsável";
        else if (c.CodSetor == 2 && string.IsNullOrWhiteSpace(c.Versao)) motivo = "setor exige a versão do sistema";
        return Task.FromResult(motivo);
    }

    private async Task FinalizarAsync(Candidato c, ConfigEmpresa cfg, int minutosPausados, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);

        // A pesquisa vai sair? Mesmo critério do EnvioWhatsAppBackgroundService
        var vaiTerPesquisa = cfg.Pesquisa && await PesquisaVaiSairAsync(conn, c.Ticket, c.CodEmp, c.Telefone, ct);

        var tempo = DescreverTempo(cfg.Minutos);
        var destino = vaiTerPesquisa ? await ObterDestinoAsync(conn, c, ct) : "";

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Finaliza só se ninguém mexeu no ticket desde a leitura (mensagem nova ou alguém salvou)
            await using (var cmdF = new SqlCommand(@"
UPDATE TicketChamadoC
SET CodSituacao = 3,
    Novo = 'N',
    UsuarioUltimaGravacao = @auto,
    DataHoraUltimaGravacao = GETDATE()
WHERE Codigo = @t AND CodEmp = @e
  AND ISNULL(CodSituacao, 1) = 1
  AND ISNULL(CodSetor, 1) = 1
  AND ISNULL(DataHoraUltimaGravacao, @ultima) <= @ultima
  AND NOT EXISTS (SELECT 1 FROM TicketChamadoD D
                  WHERE D.CodTicketChamadoC = @t AND D.CodEmp = @e AND D.DataHora > @ultima);", conn, tx))
            {
                cmdF.Parameters.AddWithValue("@t", c.Ticket);
                cmdF.Parameters.AddWithValue("@e", c.CodEmp);
                cmdF.Parameters.Add("@auto", SqlDbType.VarChar, 50).Value = UsuarioAutomatico;
                cmdF.Parameters.Add("@ultima", SqlDbType.DateTime).Value = c.UltimaInteracao;
                if (await cmdF.ExecuteNonQueryAsync(ct) == 0)
                {
                    await tx.RollbackAsync(ct);
                    return;   // teve movimento no meio do caminho: fica para a próxima volta
                }
            }

            // Aviso ao cliente: só quando a pesquisa vai sair (primeira finalização do dia para esse número)
            if (vaiTerPesquisa)
            {
                var texto = MontarAvisoEncerramento(cfg.Mensagem, cfg.Minutos);

                await using var cmdM = new SqlCommand(@"
INSERT INTO TicketChamadoD
    (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
     TelefoneWhatsApp, CodInstancia, LidoSuporte, EnvioCliente, LidoCliente, StatusWhatsApp, Interno)
VALUES
    (@e, @t, @texto, GETDATE(), @auto,
     NULLIF(@tel, ''), NULL, 'S', 'N', NULL, NULL, 'N');", conn, tx);
                cmdM.Parameters.AddWithValue("@t", c.Ticket);
                cmdM.Parameters.AddWithValue("@e", c.CodEmp);
                cmdM.Parameters.Add("@texto", SqlDbType.NVarChar, -1).Value = texto;
                cmdM.Parameters.Add("@tel", SqlDbType.VarChar, 30).Value = destino;
                cmdM.Parameters.Add("@auto", SqlDbType.VarChar, 50).Value = UsuarioAutomatico;
                await cmdM.ExecuteNonQueryAsync(ct);
            }

            // Nota interna para o atendente ver o que aconteceu
            await using (var cmdN = new SqlCommand(@"
INSERT INTO TicketChamadoD
    (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
     TelefoneWhatsApp, CodInstancia, LidoSuporte, EnvioCliente, LidoCliente, StatusWhatsApp, Interno)
VALUES
    (@e, @t, @nota, GETDATE(), @auto, NULL, NULL, 'S', 'N', NULL, 'interno', 'S');", conn, tx))
            {
                var nota = $"Ticket finalizado automaticamente: {tempo} sem interação" +
                           (minutosPausados > 0 ? $" (sem contar {DescreverTempo(minutosPausados)} em que o responsável estava em pausa)." : ".") +
                           (vaiTerPesquisa
                               ? " O cliente foi avisado e vai receber a pesquisa de satisfação."
                               : " Sem aviso ao cliente (ele já recebeu pesquisa de satisfação nas últimas 24h ou a pesquisa está desligada).");
                cmdN.Parameters.AddWithValue("@t", c.Ticket);
                cmdN.Parameters.AddWithValue("@e", c.CodEmp);
                cmdN.Parameters.Add("@nota", SqlDbType.NVarChar, -1).Value = nota;
                cmdN.Parameters.Add("@auto", SqlDbType.VarChar, 50).Value = UsuarioAutomatico;
                await cmdN.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }

        LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket} finalizado ({tempo} sem interação; " +
                      (vaiTerPesquisa ? "aviso + pesquisa)." : "sem aviso, já teve pesquisa)."));

        await DepoisDeFinalizarAsync(c);
    }

    /// <summary>O que o Ticket.razor faz depois de finalizar: treinamento do cliente e insight automático.</summary>
    private async Task DepoisDeFinalizarAsync(Candidato c)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var p = scope.ServiceProvider.GetRequiredService<ParametroDinamicoService>();

            var treino = await p.BuscarBoolAsync(c.CodEmp, "TREINAMENTO_REGISTRAR_AUTOMATICO", "TICKET",
                "Ao finalizar o ticket, a IA registra no histórico de treinamento do cliente o que foi ensinado. S=SIM N=NAO. PADRAO=S",
                true);
            if (treino)
                scope.ServiceProvider.GetRequiredService<TreinamentoClienteService>()
                    .ProcessarTicketFinalizadoEmSegundoPlano(c.Ticket, c.CodEmp, c.Usuario);

            // Insight: o responsável decide no aviso "Ticket finalizado automaticamente — gerar insight?"
            // (até ele responder, a rotina da noite também não gera)
            await _insightAuto.MarcarAguardandoDecisaoAsync(c.Ticket);
            await CriarPerguntaInsightAsync(c);
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{c.Ticket}: finalizado, mas falhou treinamento/insight ({ex.Message}).");
        }
    }

    /// <summary>Mesmo destino da resposta normal (TicketService.ObterJidDestinoAsync): número da última mensagem do cliente.</summary>
    private static Task<string> ObterDestinoAsync(SqlConnection conn, Candidato c, CancellationToken ct) =>
        ObterDestinoAsync(conn, c.Ticket, c.CodEmp, c.Telefone, ct);

    internal static async Task<string> ObterDestinoAsync(SqlConnection conn, int ticket, int codEmp, string telefoneCabecalho,
        CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(@"
SELECT TOP 1 TelefoneWhatsApp FROM TicketChamadoD
WHERE CodTicketChamadoC = @t AND CodEmp = @e
  AND ISNULL(EnvioCliente, 'N') = 'S' AND ISNULL(TelefoneWhatsApp, '') <> ''
ORDER BY DataHora DESC, Codigo DESC;", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@e", codEmp);
        var jid = SoDigitos((await cmd.ExecuteScalarAsync(ct))?.ToString());
        return jid.Length >= 12 ? jid : SoDigitos(telefoneCabecalho);
    }

    private static string SoDigitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// A pesquisa de satisfação vai sair para este ticket? Mesmo critério do EnvioWhatsAppBackgroundService
    /// (1 por ticket e no máximo 1 por número a cada 24h). Não olha o parâmetro da pesquisa.
    /// </summary>
    internal static async Task<bool> PesquisaVaiSairAsync(SqlConnection conn, int ticket, int codEmp, string telefoneCabecalho,
        CancellationToken ct = default)
    {
        await using var cmdP = new SqlCommand(@"
SELECT CASE WHEN EXISTS (SELECT 1 FROM TicketPesquisaSatisfacao P
                         WHERE P.CodTicketChamadoC = @t AND P.CodEmp = @e)
              OR EXISTS (SELECT 1 FROM TicketPesquisaSatisfacao P2
                         WHERE P2.TelefoneWhatsApp = @tel AND P2.CodEmp = @e
                           AND P2.DataEnvio >= DATEADD(HOUR, -24, GETDATE()))
            THEN 0 ELSE 1 END;", conn);
        cmdP.Parameters.AddWithValue("@t", ticket);
        cmdP.Parameters.AddWithValue("@e", codEmp);
        cmdP.Parameters.Add("@tel", SqlDbType.VarChar, 30).Value = telefoneCabecalho ?? "";
        return Convert.ToInt32(await cmdP.ExecuteScalarAsync(ct)) == 1;
    }

    /// <summary>Texto do aviso de encerramento ({tempo} trocado, \n vira quebra de linha).</summary>
    internal static string MontarAvisoEncerramento(string? modelo, int minutos)
    {
        var texto = string.IsNullOrWhiteSpace(modelo) ? MensagemPadrao : modelo;
        return texto.Replace("\\n", "\n").Replace("{tempo}", DescreverTempo(Math.Max(1, minutos))).Trim();
    }

    internal static string DescreverTempo(int minutos)
    {
        if (minutos < 60) return $"{minutos} minutos";
        var h = minutos / 60;
        var m = minutos % 60;
        var horas = h == 1 ? "1 hora" : $"{h} horas";
        return m == 0 ? horas : $"{horas} e {m} minutos";
    }
}
