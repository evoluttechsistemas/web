using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Gera o insight (o "caso aprendido" que a IA usa) de todo ticket finalizado, sem perguntar ao atendente.
///  - Na hora: o Ticket.razor chama <see cref="Enfileirar"/> ao finalizar; roda em segundo plano, um por vez.
///  - Toda noite (02:30): só tenta de novo os tickets em que alguém escolheu "gerar insight" e a geração
///    não terminou (erro da IA, sistema reiniciado...). Ticket sem essa escolha não ganha insight.
/// Parâmetro TICKET_INSIGHT_AUTOMATICO (S/N, padrão S) liga/desliga, por empresa.
/// O que a IA gerar só é usado para responder depois que o cliente confirmou a solução ou alguém
/// aprovou na Curadoria da IA (IaCuradoriaSchema), então gerar para todos não suja a base.
///
/// Para não gastar crédito à toa, a IA NÃO é chamada quando:
///  - o atendente finalizou com "Finalizar sem insight" (<see cref="MarcarPuladoAsync"/>);
///  - o filtro sem IA vê que não há o que aprender: o cliente só cumprimentou/agradeceu ou mandou
///    quase nada, ou nenhum atendente respondeu de verdade (<see cref="MotivoParaPularAsync"/>).
/// Os dois casos ficam registrados em AiInsightTentativa (QtdInsights -1 e -2).
/// Quando alguém escolhe "gerar insight", a fila grava antes QtdInsights -4 (pedido); só esse código
/// é procurado pela rotina da noite, e ele é trocado pelo resultado quando a geração termina.
/// </summary>
public sealed class InsightAutomaticoService : BackgroundService
{
    public const string Parametro = "TICKET_INSIGHT_AUTOMATICO";
    public const string ParametroDescricao =
        "Ao finalizar o ticket, a IA gera o insight (caso aprendido) em segundo plano, sem perguntar ao atendente. " +
        "Só é usado nas respostas depois de confirmado pelo cliente ou aprovado na Curadoria da IA. S=SIM N=NAO. PADRAO=S";

    private static readonly TimeSpan HorarioNoturno = new(2, 30, 0);
    private const int DiasVarredura = 30;
    private const int LimitePorNoite = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cs;
    private readonly Channel<(int Ticket, int CodEmp)> _fila =
        Channel.CreateBounded<(int, int)>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly HashSet<int> _naFila = new();
    private readonly object _trava = new();

    private readonly DocumentacaoAutomaticaService _docAuto;

    public InsightAutomaticoService(IServiceScopeFactory scopeFactory, IConfiguration config, DocumentacaoAutomaticaService docAuto)
    {
        _scopeFactory = scopeFactory;
        _docAuto = docAuto;
        _cs = config.GetConnectionString("Connection")!;
    }

    /// <summary>Coloca o ticket na fila (não espera). Ticket repetido enquanto está na fila é ignorado.</summary>
    /// <summary>
    /// Alguém escolheu "gerar insight": grava o pedido (QtdInsights -4) e coloca na fila.
    /// Se a geração não terminar, a rotina da noite tenta de novo.
    /// </summary>
    public void Enfileirar(int codTicket, int codEmp)
    {
        if (codTicket <= 0) return;
        _ = Task.Run(async () =>
        {
            try { await RegistrarTentativaAsync(codTicket, Pedido, CancellationToken.None); }
            catch (Exception ex) { LogHelper.Log($"[INSIGHT AUTO] Ticket #{codTicket}: não gravou o pedido ({ex.Message})."); }
            Colocar(codTicket, codEmp);
        });
    }

    /// <summary>QtdInsights do ticket em que alguém pediu o insight e a geração ainda não terminou.</summary>
    private const int Pedido = -4;

    private void Colocar(int codTicket, int codEmp)
    {
        lock (_trava)
        {
            if (!_naFila.Add(codTicket)) return;
        }
        if (!_fila.Writer.TryWrite((codTicket, codEmp)))
            lock (_trava) _naFila.Remove(codTicket);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(ProcessarFilaAsync(stoppingToken), VarreduraNoturnaAsync(stoppingToken));

    // ─────────────────────────────── Fila ───────────────────────────────

    private async Task ProcessarFilaAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (ticket, codEmp) in _fila.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await GerarSePrecisarAsync(ticket, codEmp, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[INSIGHT AUTO] Ticket #{ticket}: {ex.Message}");
                }
                finally
                {
                    lock (_trava) _naFila.Remove(ticket);
                }

                try { await Task.Delay(500, ct); }   // não estourar o limite da OpenAI
                catch (OperationCanceledException) { return; }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Gera quando o ticket não tem insight automático ou teve mensagem nova depois dele.
    /// Registra a tentativa (AiInsightTentativa) para não ficar tentando todo dia o ticket
    /// em que a IA não achou assunto de suporte.
    /// </summary>
    private async Task GerarSePrecisarAsync(int ticket, int codEmp, CancellationToken ct)
    {
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync(ct);
            await GarantirTabelaTentativaAsync(conn);

            await using var cmd = new SqlCommand(@"
SELECT
    (SELECT MAX(D.DataHora) FROM TicketChamadoD D WHERE D.CodTicketChamadoC = @t),
    (SELECT MAX(I.CriadoEm) FROM dbo.AiTicketInsights I WHERE I.CodTicketChamadoC = @t AND I.TopicoIndex < 900),
    (SELECT COUNT(*) FROM dbo.AiTicketInsights I WHERE I.CodTicketChamadoC = @t AND I.TopicoIndex < 900);", conn);
            cmd.Parameters.AddWithValue("@t", ticket);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))
            {
                DateTime? ultimaMsg = rd.IsDBNull(0) ? null : rd.GetDateTime(0);
                DateTime? gerado = rd.IsDBNull(1) ? null : rd.GetDateTime(1);
                int qtdExistente = rd.IsDBNull(2) ? 0 : rd.GetInt32(2);

                // Tudo que chega na fila foi pedido ("gerar insight"); aqui só evita gerar de novo o que já está em dia
                if (ultimaMsg == null || (gerado != null && gerado >= ultimaMsg))
                {
                    await rd.CloseAsync();
                    await RegistrarTentativaAsync(ticket, ultimaMsg == null ? 0 : qtdExistente, ct);   // pedido atendido
                    return;
                }
            }
        }

        // Filtro sem IA: conversa sem nada para aprender não gasta crédito
        var motivo = await MotivoParaPularAsync(ticket, ct);
        if (motivo != null)
        {
            await RegistrarTentativaAsync(ticket, -2, ct);
            LogHelper.Log($"[INSIGHT AUTO] Ticket #{ticket}: pulado sem chamar a IA ({motivo}).");
            return;
        }

        // Trava de gasto: passou do máximo do dia, o pedido (-4) fica guardado e a madrugada tenta amanhã
        if (!LimiteDiarioIa.Consumir(LimiteDiarioIa.Insight))
        {
            LogHelper.Log($"[INSIGHT AUTO] Ticket #{ticket}: limite diário de insight automático atingido; fica para amanhã.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var auto = scope.ServiceProvider.GetRequiredService<AiAutoLearningService>();
        var insights = await auto.GenerateAndSaveManyAsync(ticket, force: true, codEmp: codEmp);

        await RegistrarTentativaAsync(ticket, insights.Count, ct);

        // Insight gerado: vira (ou melhora) uma documentação interna detalhada
        if (insights.Count > 0)
            _docAuto.Enfileirar(ticket, codEmp);

        LogHelper.Log($"[INSIGHT AUTO] Ticket #{ticket}: {insights.Count} insight(s), " +
                      $"{insights.Count(i => i.solucaoConfirmada)} confirmado(s) pelo cliente.");
    }

    /// <summary>
    /// O atendente escolheu "Finalizar sem insight": registra para a rotina da noite também não gerar
    /// (até o ticket receber mensagem nova).
    /// </summary>
    public async Task MarcarPuladoAsync(int codTicket)
    {
        if (codTicket <= 0) return;
        try
        {
            await RegistrarTentativaAsync(codTicket, -1, CancellationToken.None);
            LogHelper.Log($"[INSIGHT AUTO] Ticket #{codTicket}: finalizado sem insight pelo atendente.");
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[INSIGHT AUTO] Ticket #{codTicket}: não registrou o 'sem insight': {ex.Message}");
        }
    }

    /// <summary>
    /// Ticket finalizado automaticamente: espera o responsável responder no aviso se gera o insight.
    /// Enquanto isso a rotina da noite não gera (QtdInsights -3).
    /// </summary>
    public async Task MarcarAguardandoDecisaoAsync(int codTicket)
    {
        if (codTicket <= 0) return;
        try
        {
            await RegistrarTentativaAsync(codTicket, -3, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[INSIGHT AUTO] Ticket #{codTicket}: não registrou o 'aguardando decisão': {ex.Message}");
        }
    }

    /// <summary>O responsável decidiu gerar o insight depois (ex.: ticket finalizado automaticamente): gera agora.</summary>
    public Task GerarPorDecisaoAsync(int codTicket, int codEmp)
    {
        // O pedido (-4) substitui o "aguardando decisão" (-3)
        Enfileirar(codTicket, codEmp);
        return Task.CompletedTask;
    }

    /// <summary>QtdInsights: quantos a IA gerou; -1 = atendente pulou; -2 = filtro sem IA pulou; -3 = aguardando decisão; -4 = pedido, ainda não gerado.</summary>
    private async Task RegistrarTentativaAsync(int ticket, int qtd, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await GarantirTabelaTentativaAsync(conn);
        await using var cmd = new SqlCommand(@"
MERGE dbo.AiInsightTentativa AS A
USING (SELECT @t AS CodTicketChamadoC) AS N ON A.CodTicketChamadoC = N.CodTicketChamadoC
WHEN MATCHED THEN UPDATE SET DataHora = GETDATE(), QtdInsights = @q
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, DataHora, QtdInsights) VALUES (@t, GETDATE(), @q);", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@q", qtd);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Palavras de cumprimento, agradecimento e confirmação: mensagem feita só delas não tem dúvida
    private static readonly HashSet<string> PalavrasSemConteudo = new(StringComparer.Ordinal)
    {
        "oi","oii","oie","ola","opa","eai","ae","bom","boa","dia","dias","tarde","tardes","noite","noites",
        "tudo","td","bem","bom","blz","beleza","ok","okay","oks","certo","certinho","sim","s","nao","n",
        "obrigado","obrigada","obg","obgd","brigado","brigada","vlw","valeu","grato","grata","agradeco",
        "entendi","entendido","perfeito","show","top","otimo","otima","joia","massa","legal","combinado",
        "e","a","o","voce","vc","pra","por","pela","pelo","de","da","do","muito","mto","mt","mais","ate",
        "logo","tchau","abraco","abracos","abs","fico","aguardo","no","aguardando","kk","kkk","kkkk","rs","rsrs",
        "deus","abencoe","amem","isso","esse","essa","ta","tá","tranquilo","pode","ser","fechado","sem","problema"
    };

    private static string SemAcento(string s)
    {
        var n = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(n.Length);
        foreach (var c in n)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().ToLowerInvariant();
    }

    /// <summary>Texto que sobra da mensagem tirando cumprimento, agradecimento e confirmação ("" = sem conteúdo).</summary>
    internal static string ConteudoUtil(string texto)
    {
        var palavras = Regex.Split(SemAcento(texto ?? ""), @"[^a-z0-9]+")
            .Where(p => p.Length > 0 && !PalavrasSemConteudo.Contains(p))
            .ToList();
        return string.Join(" ", palavras);
    }

    /// <summary>
    /// Verificação sem IA (custo zero) antes de gastar crédito. Devolve o motivo para pular, ou null para gerar.
    ///  - nenhum atendente respondeu de verdade (nem nota interna): não há solução para aprender;
    ///  - o cliente só cumprimentou/agradeceu ou mandou quase nada (menos de ~4 palavras com conteúdo)
    ///    e não mandou print nem áudio.
    /// </summary>
    private async Task<string?> MotivoParaPularAsync(int ticket, CancellationToken ct)
    {
        var textosCliente = new List<string>();
        int midiasCliente = 0, respostasAtendente = 0, notas = 0;

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SELECT ISNULL(D.EnvioCliente, 'N'), ISNULL(D.Interno, 'N'), LTRIM(RTRIM(ISNULL(D.Usuario, ''))),
       ISNULL(D.Anotacao, ''), ISNULL(D.TranscricaoAudio, ''),
       CASE WHEN D.Imagem IS NOT NULL OR D.Audio IS NOT NULL THEN 1 ELSE 0 END
FROM TicketChamadoD D
WHERE D.CodTicketChamadoC = @t
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND ISNULL(D.StatusWhatsApp, '') NOT IN ('ia_processing', 'saudacao_auto', 'aguarde_auto');", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                var cliente = rd.GetString(0) == "S";
                var interno = rd.GetString(1) == "S";
                var usuario = rd.GetString(2).ToUpperInvariant();
                var texto = (rd.GetString(3) + " " + rd.GetString(4)).Trim();
                var temMidia = rd.GetInt32(5) == 1;

                if (cliente)
                {
                    if (texto.Length > 0) textosCliente.Add(texto);
                    if (temMidia) midiasCliente++;
                }
                else if (interno)
                {
                    if (texto.Length > 0 && !UsuariosAutomaticos.Contains(usuario)) notas++;
                }
                else if (texto.Length > 0 && !UsuariosAutomaticos.Contains(usuario))
                {
                    respostasAtendente++;
                }
            }
        }

        if (respostasAtendente == 0 && notas == 0)
            return "nenhum atendente respondeu";

        var palavrasUteis = textosCliente
            .Select(ConteudoUtil)
            .Sum(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        if (palavrasUteis < 4 && midiasCliente == 0)
            return textosCliente.Count == 0 ? "cliente não escreveu nada" : "cliente só cumprimentou/agradeceu";

        return null;
    }

    // Mesmo critério do TicketQueryRepository: quem não é atendente de verdade
    private static readonly HashSet<string> UsuariosAutomaticos = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "WHATSAPP", "CLIENTE", "BOT", "IA", "SISTEMA", "NOVO", "EVOLUTTECH", "POR WHATS", "PESQUISA",
        FinalizacaoAutomaticaService.UsuarioAutomatico
    };

    private static volatile bool _tabelaOk;

    private static async Task GarantirTabelaTentativaAsync(SqlConnection conn)
    {
        if (_tabelaOk) return;
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.AiInsightTentativa', 'U') IS NULL
    CREATE TABLE dbo.AiInsightTentativa
    (
        CodTicketChamadoC INT      NOT NULL CONSTRAINT PK_AiInsightTentativa PRIMARY KEY,
        DataHora          DATETIME NOT NULL CONSTRAINT DF_AiInsightTentativa_DataHora DEFAULT (GETDATE()),
        QtdInsights       INT      NOT NULL CONSTRAINT DF_AiInsightTentativa_Qtd DEFAULT (0)
    );", conn);
        await cmd.ExecuteNonQueryAsync();
        _tabelaOk = true;
    }

    // ─────────────────────────── Varredura noturna ───────────────────────────

    private async Task VarreduraNoturnaAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var agora = DateTime.Now;
            var proxima = agora.Date + HorarioNoturno;
            if (proxima <= agora) proxima = proxima.AddDays(1);

            try { await Task.Delay(proxima - agora, ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                var tickets = await BuscarPendentesAsync(ct);
                var ligadoPorEmpresa = new Dictionary<int, bool>();
                int colocados = 0;

                foreach (var (ticket, codEmp) in tickets)
                {
                    if (!ligadoPorEmpresa.TryGetValue(codEmp, out var ligado))
                    {
                        ligado = await LigadoAsync(codEmp);
                        ligadoPorEmpresa[codEmp] = ligado;
                    }
                    if (!ligado) continue;

                    Colocar(ticket, codEmp);   // o pedido já está gravado
                    colocados++;
                }

                LogHelper.Log($"[INSIGHT AUTO] Varredura noturna: {colocados} pedido(s) de insight que não tinham terminado voltaram para a fila.");
            }
            catch (Exception ex)
            {
                LogHelper.Log("[INSIGHT AUTO] Varredura noturna: " + ex.Message);
            }
        }
    }

    private async Task<List<(int Ticket, int CodEmp)>> BuscarPendentesAsync(CancellationToken ct)
    {
        var lista = new List<(int, int)>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await GarantirTabelaTentativaAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT TOP (@limite) T.Codigo, T.CodEmp
FROM TicketChamadoC T
CROSS APPLY (SELECT MAX(D.DataHora) AS Ultima FROM TicketChamadoD D WHERE D.CodTicketChamadoC = T.Codigo) U
INNER JOIN dbo.AiInsightTentativa A ON A.CodTicketChamadoC = T.Codigo
WHERE T.CodSituacao = 3
  AND A.QtdInsights = @pedido               -- só quem escolheu ""gerar insight"" e não terminou
  AND U.Ultima >= DATEADD(DAY, -@dias, GETDATE())
ORDER BY U.Ultima DESC;", conn);
        cmd.Parameters.AddWithValue("@limite", LimitePorNoite);
        cmd.Parameters.AddWithValue("@dias", DiasVarredura);
        cmd.Parameters.AddWithValue("@pedido", Pedido);
        cmd.CommandTimeout = 120;

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add((rd.GetInt32(0), rd.GetInt32(1)));
        return lista;
    }

    private async Task<bool> LigadoAsync(int codEmp)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var parametros = scope.ServiceProvider.GetRequiredService<ParametroDinamicoService>();
            return await parametros.BuscarBoolAsync(codEmp, Parametro, "TICKET", ParametroDescricao, true);
        }
        catch
        {
            return true; // sem conseguir ler o parâmetro, vale o padrão (S)
        }
    }
}
