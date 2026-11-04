using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using EvolutCRM.Data;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Documentação interna automática: cada ticket que gera insight vira (ou melhora) uma documentação
/// detalhada na Documentação, escrita pela IA (modelo intermediário, gpt-4.1).
///  - Interna (Visibilidade I) e já publicada: a equipe vê na hora e a IA passa a usar nas respostas.
///  - Mesmo assunto já documentado: a IA junta o caso novo na documentação que existe, em vez de criar outra.
///    Achar o "mesmo assunto": parecido pelo embedding e a própria IA confirma antes de juntar.
///  - Faxina (ao iniciar e toda noite às 03:30): compara as documentações automáticas entre si e junta numa só
///    as que a IA confirmar que são do mesmo assunto (a outra é apagada e os tickets passam para a que ficou).
///    Pares que a IA disse que não são o mesmo assunto ficam em dbo.DocumentacaoAutomaticaPar e não são perguntados de novo.
///  - Só documenta insight com solução (confiança >= 60). Ticket sem insight não gera documentação.
/// Parâmetro DOCUMENTACAO_AUTOMATICA (S/N, padrão S), grupo TICKET.
/// Controle: dbo.DocumentacaoAutomatica (uma linha por documentação gerada) e
/// dbo.DocumentacaoAutomaticaTicket (qual documentação recebeu cada assunto de cada ticket).
/// </summary>
public sealed class DocumentacaoAutomaticaService : BackgroundService
{
    public const string Parametro = "DOCUMENTACAO_AUTOMATICA";
    private const string ParametroDescricao =
        "Cada ticket que gera insight vira (ou melhora) uma documentacao interna detalhada, escrita pela IA e ja publicada. " +
        "Assunto repetido entra na documentacao que ja existe. S=SIM N=NAO. PADRAO=S";

    public const string Categoria = "Casos de atendimento";
    private const string UsuarioIa = "IA";
    private const int ConfiancaMinima = 60;
    private const float SemelhancaMinima = 0.65f;   // abaixo disso nem pergunta à IA se é o mesmo assunto
    private const int CandidatosPorAssunto = 2;     // quantas documentações parecidas a IA confere antes de criar outra
    private const float SemelhancaFaxina = 0.60f;   // faxina: pares a partir daqui vão para a IA conferir
    private const int ParesPorFaxina = 20;          // limite de perguntas à IA por faxina (e LimiteDiarioIa.Faxina por dia)
    private static readonly TimeSpan HorarioFaxina = new(3, 30, 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cs;
    private readonly Channel<(int Ticket, int CodEmp)> _fila =
        Channel.CreateBounded<(int, int)>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly HashSet<int> _naFila = new();
    private readonly object _trava = new();

    public DocumentacaoAutomaticaService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _cs = config.GetConnectionString("Connection")!;
    }

    /// <summary>Chamado logo depois que o insight do ticket foi gerado (não espera).</summary>
    public void Enfileirar(int codTicket, int codEmp)
    {
        if (codTicket <= 0 || codEmp <= 0) return;
        lock (_trava)
        {
            if (!_naFila.Add(codTicket)) return;
        }
        if (!_fila.Writer.TryWrite((codTicket, codEmp)))
            lock (_trava) _naFila.Remove(codTicket);
    }

    protected override Task ExecuteAsync(CancellationToken ct)
        => Task.WhenAll(ProcessarFilaAsync(ct), FaxinaAsync(ct));

    private async Task ProcessarFilaAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (ticket, codEmp) in _fila.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await DocumentarTicketAsync(ticket, codEmp, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[DOC AUTO] Ticket #{ticket}: {ex.Message}");
                }
                finally
                {
                    lock (_trava) _naFila.Remove(ticket);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ───────────────────────────── Processo ─────────────────────────────

    private async Task DocumentarTicketAsync(int ticket, int codEmp, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var parametros = sp.GetRequiredService<ParametroDinamicoService>();
        if (!await parametros.BuscarBoolAsync(codEmp, Parametro, "TICKET", ParametroDescricao, true))
            return;

        var insights = (await sp.GetRequiredService<AiTicketInsightsRepository>().ListByCodTicketChamadoCAsync(ticket))
            .Where(i => i.topicoIndex < 900
                        && !string.IsNullOrWhiteSpace(i.solucao)
                        && i.confianca >= ConfiancaMinima)
            .ToList();
        if (insights.Count == 0)
        {
            LogHelper.Log($"[DOC AUTO] Ticket #{ticket}: nenhum insight com solução para documentar.");
            return;
        }

        // Trava de gasto (gpt-4.1): passou do máximo do dia, não documenta mais hoje
        if (!LimiteDiarioIa.Consumir(LimiteDiarioIa.Documentacao))
        {
            LogHelper.Log($"[DOC AUTO] Ticket #{ticket}: limite diário de documentação automática atingido; não documentado.");
            return;
        }

        await GarantirTabelasAsync(ct);

        var openAi = sp.GetRequiredService<OpenAiClient>();
        var docs = sp.GetRequiredService<DocumentacaoService>();
        var conversa = (await sp.GetRequiredService<TicketQueryRepository>()
            .MontarParaInsightAsync(ticket, codEmp, 12000, incluirImagens: false)).Texto;

        foreach (var insight in insights)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DocumentarAssuntoAsync(ticket, codEmp, insight, conversa, openAi, docs, ct);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DOC AUTO] Ticket #{ticket}, assunto {insight.topicoIndex}: {ex.Message}");
            }
        }
    }

    private async Task DocumentarAssuntoAsync(int ticket, int codEmp, TicketInsight insight, string conversa,
        OpenAiClient openAi, DocumentacaoService docs, CancellationToken ct)
    {
        var assunto = TextoAssunto(insight);
        var caso = TextoCaso(insight);

        // 1) Este assunto deste ticket já foi documentado antes (ex.: o insight foi gerado de novo)?
        var codLigado = await DocumentacaoDoTicketAsync(ticket, insight.topicoIndex, ct);

        // 2) Senão, procura as documentações geradas com assunto parecido (a IA confere uma por uma)
        float[] embedding = await openAi.GetEmbeddingAsync(assunto);
        var candidatos = codLigado is int lig
            ? new List<int> { lig }
            : await DocumentacoesParecidasAsync(codEmp, embedding, CandidatosPorAssunto, ct);

        foreach (var cod in candidatos)
        {
            var existente = await docs.BuscarAsync(cod, codEmp);
            if (existente == null)
            {
                await EsquecerDocumentacaoAsync(cod, ct);   // apagada pela equipe
                continue;
            }

            var r = await openAi.DocumentarCasoAsync(conversa, caso, existente.Titulo, HtmlDaDocumentacao(existente));
            if (!r.MesmoAssunto || string.IsNullOrWhiteSpace(r.Html))
                continue;   // a IA disse que não é o mesmo assunto: confere a próxima (sem apagar o conteúdo que existe)

            existente.Titulo = Limitar(r.Titulo, 150, existente.Titulo);
            existente.Descricao = Limitar(r.Resumo, 300, existente.Descricao);
            existente.Blocos = BlocoUnico(r.Html);
            existente.UsuarioAlteracao = UsuarioIa;
            await docs.SalvarAsync(existente);   // mantém publicação/visibilidade que a equipe deixou

            await RegistrarCasoAsync(cod, codEmp, ticket, insight.topicoIndex, novoCaso: !codLigado.HasValue, ct);
            LogHelper.Log($"[DOC AUTO] Ticket #{ticket}: caso juntado na documentação #{cod} \"{existente.Titulo}\".");
            return;
        }

        // 3) Documentação nova
        var nova = await openAi.DocumentarCasoAsync(conversa, caso, null, null);
        var doc = new DocumentacaoTutorialModel
        {
            CodEmp = codEmp,
            Titulo = Limitar(nova.Titulo, 150, insight.topicoTitulo ?? "Caso de atendimento"),
            Descricao = Limitar(nova.Resumo, 300, insight.resumo ?? ""),
            Categoria = Categoria,
            Publicado = "S",
            Visibilidade = "I",
            Sistema = "EVOLUTTECH",
            UsuarioCriacao = UsuarioIa,
            UsuarioAlteracao = UsuarioIa,
            Blocos = BlocoUnico(nova.Html)
        };
        doc.Slug = await SlugLivreAsync(docs, doc.Titulo, ticket, codEmp);

        var codNovo = await docs.SalvarAsync(doc);
        await CriarRegistroAsync(codNovo, codEmp, assunto, embedding, ticket, insight.topicoIndex, ct);
        LogHelper.Log($"[DOC AUTO] Ticket #{ticket}: documentação interna #{codNovo} criada \"{doc.Titulo}\".");
    }

    // ───────────────────────────── Textos ─────────────────────────────

    private static string TextoAssunto(TicketInsight i) =>
        string.Join(" — ", new[] { i.topicoTitulo, i.perguntaCliente, i.resumo }.Where(t => !string.IsNullOrWhiteSpace(t)));

    private static string TextoCaso(TicketInsight i)
    {
        var sb = new StringBuilder();
        void Linha(string rotulo, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor)) sb.AppendLine($"{rotulo}: {valor.Trim()}");
        }
        Linha("Assunto", i.topicoTitulo);
        Linha("Pergunta do cliente", i.perguntaCliente);
        Linha("Resumo", i.resumo);
        Linha("Categoria", i.categoria);
        Linha("Causa provável", i.causaProvavel);
        Linha("Solução", i.solucao);
        if (i.tags?.Count > 0) Linha("Tags", string.Join(", ", i.tags));
        sb.AppendLine(i.solucaoConfirmada ? "O cliente confirmou que resolveu." : "O cliente não confirmou se resolveu.");
        return sb.ToString();
    }

    private static string HtmlDaDocumentacao(DocumentacaoTutorialModel d)
    {
        if (d.Blocos.Count == 1 && d.Blocos[0].Tipo == "TEXTO") return d.Blocos[0].Conteudo ?? "";
        var sb = new StringBuilder();
        foreach (var b in d.Blocos.OrderBy(b => b.Ordem))
        {
            if (!string.IsNullOrWhiteSpace(b.Titulo)) sb.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(b.Titulo)).Append("</h2>");
            if (!string.IsNullOrWhiteSpace(b.Conteudo)) sb.Append(b.Conteudo);
        }
        return sb.ToString();
    }

    /// <summary>O editor da Documentação guarda o HTML inteiro num bloco TEXTO "Conteúdo".</summary>
    private static List<DocumentacaoTutorialBlocoModel> BlocoUnico(string html) => new()
    {
        new DocumentacaoTutorialBlocoModel { Tipo = "TEXTO", Titulo = "Conteúdo", Conteudo = HtmlSeguro.Limpar(html), Ordem = 1 }
    };

    private static string Limitar(string? texto, int max, string? alternativa)
    {
        var t = string.IsNullOrWhiteSpace(texto) ? (alternativa ?? "") : texto.Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    private static async Task<string> SlugLivreAsync(DocumentacaoService docs, string titulo, int ticket, int codEmp)
    {
        var baseSlug = Slugificar(titulo);
        if (baseSlug.Length > 70) baseSlug = baseSlug[..70].Trim('-');
        if (baseSlug.Length == 0) baseSlug = "caso";

        var slug = $"{baseSlug}-{ticket}";
        for (var n = 2; await docs.SlugEmUsoAsync(slug, codEmp, 0) != null; n++)
            slug = $"{baseSlug}-{ticket}-{n}";
        return slug;
    }

    private static string Slugificar(string texto)
    {
        var n = (texto ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return Regex.Replace(Regex.Replace(sb.ToString(), "[^a-z0-9]+", "-"), "-{2,}", "-").Trim('-');
    }

    // ───────────────────────────── Tabelas de controle ─────────────────────────────

    private static volatile bool _tabelasOk;

    private async Task GarantirTabelasAsync(CancellationToken ct)
    {
        if (_tabelasOk) return;
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.DocumentacaoAutomatica', 'U') IS NULL
    CREATE TABLE dbo.DocumentacaoAutomatica
    (
        CodDocumentacao INT            NOT NULL CONSTRAINT PK_DocumentacaoAutomatica PRIMARY KEY,
        CodEmp          INT            NOT NULL,
        Assunto         NVARCHAR(1000) NOT NULL,
        Embedding       VARBINARY(MAX) NULL,
        QtdCasos        INT            NOT NULL CONSTRAINT DF_DocumentacaoAutomatica_Qtd DEFAULT (1),
        CriadoEm        DATETIME       NOT NULL CONSTRAINT DF_DocumentacaoAutomatica_Criado DEFAULT (GETDATE()),
        AtualizadoEm    DATETIME       NOT NULL CONSTRAINT DF_DocumentacaoAutomatica_Atual DEFAULT (GETDATE())
    );

IF OBJECT_ID('dbo.DocumentacaoAutomaticaTicket', 'U') IS NULL
    CREATE TABLE dbo.DocumentacaoAutomaticaTicket
    (
        CodTicketChamadoC INT      NOT NULL,
        TopicoIndex       INT      NOT NULL,
        CodDocumentacao   INT      NOT NULL,
        DataHora          DATETIME NOT NULL CONSTRAINT DF_DocumentacaoAutomaticaTicket_Data DEFAULT (GETDATE()),
        CONSTRAINT PK_DocumentacaoAutomaticaTicket PRIMARY KEY (CodTicketChamadoC, TopicoIndex)
    );

-- Pares que a IA já conferiu e disse que NÃO são o mesmo assunto (CodA < CodB)
IF OBJECT_ID('dbo.DocumentacaoAutomaticaPar', 'U') IS NULL
    CREATE TABLE dbo.DocumentacaoAutomaticaPar
    (
        CodA         INT      NOT NULL,
        CodB         INT      NOT NULL,
        CodEmp       INT      NOT NULL,
        ConferidoEm  DATETIME NOT NULL CONSTRAINT DF_DocumentacaoAutomaticaPar_Data DEFAULT (GETDATE()),
        CONSTRAINT PK_DocumentacaoAutomaticaPar PRIMARY KEY (CodA, CodB)
    );", conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _tabelasOk = true;
    }

    private async Task<int?> DocumentacaoDoTicketAsync(int ticket, int topico, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SELECT T.CodDocumentacao
FROM dbo.DocumentacaoAutomaticaTicket T
INNER JOIN dbo.DocumentacaoAutomatica A ON A.CodDocumentacao = T.CodDocumentacao
WHERE T.CodTicketChamadoC = @t AND T.TopicoIndex = @top;", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@top", topico);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r == null || r == DBNull.Value ? null : Convert.ToInt32(r);
    }

    /// <summary>Documentações geradas mais parecidas com o assunto (da mais parecida para a menos), só as que passam do mínimo.</summary>
    private async Task<List<int>> DocumentacoesParecidasAsync(int codEmp, float[] embedding, int max, CancellationToken ct)
    {
        var notas = new List<(int Cod, float Nota)>();
        if (embedding.Length == 0) return new List<int>();

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SELECT A.CodDocumentacao, A.Embedding
FROM dbo.DocumentacaoAutomatica A
INNER JOIN DocumentacaoTutorial D ON D.Codigo = A.CodDocumentacao AND D.CodEmp = A.CodEmp
WHERE A.CodEmp = @e AND A.Embedding IS NOT NULL;", conn);
        cmd.Parameters.AddWithValue("@e", codEmp);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var nota = Cosseno(embedding, ParaVetor((byte[])rd[1]));
            if (nota >= SemelhancaMinima) notas.Add((rd.GetInt32(0), nota));
        }
        return notas.OrderByDescending(n => n.Nota).Take(max).Select(n => n.Cod).ToList();
    }

    private async Task CriarRegistroAsync(int codDoc, int codEmp, string assunto, float[] embedding, int ticket, int topico,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
INSERT INTO dbo.DocumentacaoAutomatica (CodDocumentacao, CodEmp, Assunto, Embedding, QtdCasos)
VALUES (@d, @e, @assunto, @emb, 1);

MERGE dbo.DocumentacaoAutomaticaTicket AS A
USING (SELECT @t AS CodTicketChamadoC, @top AS TopicoIndex) AS N
   ON A.CodTicketChamadoC = N.CodTicketChamadoC AND A.TopicoIndex = N.TopicoIndex
WHEN MATCHED THEN UPDATE SET CodDocumentacao = @d, DataHora = GETDATE()
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, TopicoIndex, CodDocumentacao) VALUES (@t, @top, @d);", conn);
        cmd.Parameters.AddWithValue("@d", codDoc);
        cmd.Parameters.AddWithValue("@e", codEmp);
        cmd.Parameters.Add("@assunto", SqlDbType.NVarChar, 1000).Value = assunto.Length > 1000 ? assunto[..1000] : assunto;
        cmd.Parameters.Add("@emb", SqlDbType.VarBinary, -1).Value = embedding.Length > 0 ? ParaBytes(embedding) : DBNull.Value;
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@top", topico);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RegistrarCasoAsync(int codDoc, int codEmp, int ticket, int topico, bool novoCaso, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
UPDATE dbo.DocumentacaoAutomatica
SET QtdCasos = QtdCasos + @soma, AtualizadoEm = GETDATE()
WHERE CodDocumentacao = @d;

MERGE dbo.DocumentacaoAutomaticaTicket AS A
USING (SELECT @t AS CodTicketChamadoC, @top AS TopicoIndex) AS N
   ON A.CodTicketChamadoC = N.CodTicketChamadoC AND A.TopicoIndex = N.TopicoIndex
WHEN MATCHED THEN UPDATE SET CodDocumentacao = @d, DataHora = GETDATE()
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, TopicoIndex, CodDocumentacao) VALUES (@t, @top, @d);", conn);
        cmd.Parameters.AddWithValue("@d", codDoc);
        cmd.Parameters.AddWithValue("@soma", novoCaso ? 1 : 0);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@top", topico);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EsquecerDocumentacaoAsync(int codDoc, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
DELETE FROM dbo.DocumentacaoAutomaticaTicket WHERE CodDocumentacao = @d;
DELETE FROM dbo.DocumentacaoAutomatica WHERE CodDocumentacao = @d;
IF OBJECT_ID('dbo.DocumentacaoAutomaticaPar', 'U') IS NOT NULL
    DELETE FROM dbo.DocumentacaoAutomaticaPar WHERE CodA = @d OR CodB = @d;", conn);
        cmd.Parameters.AddWithValue("@d", codDoc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ───────────────────────────── Faxina (juntar documentações do mesmo assunto) ─────────────────────────────

    private async Task FaxinaAsync(CancellationToken ct)
    {
        // Primeira faxina logo depois de iniciar (junta as que já estão duplicadas), depois toda noite
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await JuntarDuplicadasAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                LogHelper.Log("[DOC AUTO] Faxina: " + ex.Message);
            }

            var agora = DateTime.Now;
            var proxima = agora.Date + HorarioFaxina;
            if (proxima <= agora) proxima = proxima.AddDays(1);
            try { await Task.Delay(proxima - agora, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private sealed class DocFaxina
    {
        public int Cod;
        public int QtdCasos;
        public string Titulo = "";
        public float[] Assunto = Array.Empty<float>();
        public float[] Tituloemb = Array.Empty<float>();
    }

    private async Task JuntarDuplicadasAsync(CancellationToken ct)
    {
        await GarantirTabelasAsync(ct);

        // Empresas que têm documentação automática
        var empresas = new List<int>();
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT DISTINCT CodEmp FROM dbo.DocumentacaoAutomatica;", conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) empresas.Add(rd.GetInt32(0));
        }

        foreach (var codEmp in empresas)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            if (!await sp.GetRequiredService<ParametroDinamicoService>()
                    .BuscarBoolAsync(codEmp, Parametro, "TICKET", ParametroDescricao, true))
                continue;

            await JuntarDuplicadasDaEmpresaAsync(codEmp, sp.GetRequiredService<OpenAiClient>(),
                sp.GetRequiredService<DocumentacaoService>(), ct);
        }
    }

    private async Task JuntarDuplicadasDaEmpresaAsync(int codEmp, OpenAiClient openAi, DocumentacaoService docs, CancellationToken ct)
    {
        // Documentações automáticas que ainda existem
        var lista = new List<DocFaxina>();
        var jaConferidos = new HashSet<(int, int)>();
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SELECT A.CodDocumentacao, A.QtdCasos, ISNULL(D.Titulo, ''), ISNULL(D.Descricao, ''), A.Embedding
FROM dbo.DocumentacaoAutomatica A
INNER JOIN DocumentacaoTutorial D ON D.Codigo = A.CodDocumentacao AND D.CodEmp = A.CodEmp
WHERE A.CodEmp = @e;

SELECT P.CodA, P.CodB FROM dbo.DocumentacaoAutomaticaPar P WHERE P.CodEmp = @e;", conn);
            cmd.Parameters.AddWithValue("@e", codEmp);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            var descricoes = new Dictionary<int, string>();
            while (await rd.ReadAsync(ct))
            {
                var d = new DocFaxina
                {
                    Cod = rd.GetInt32(0),
                    QtdCasos = rd.GetInt32(1),
                    Titulo = rd.GetString(2),
                    Assunto = rd.IsDBNull(4) ? Array.Empty<float>() : ParaVetor((byte[])rd[4])
                };
                descricoes[d.Cod] = rd.GetString(3);
                lista.Add(d);
            }
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct))
                jaConferidos.Add((rd.GetInt32(0), rd.GetInt32(1)));

            await rd.CloseAsync();
            if (lista.Count < 2) return;

            // Embedding do título + resumo de cada documentação (pega o "mesmo assunto" escrito de jeito diferente)
            foreach (var d in lista)
            {
                ct.ThrowIfCancellationRequested();
                try { d.Tituloemb = await openAi.GetEmbeddingAsync($"{d.Titulo} — {descricoes[d.Cod]}"); }
                catch (Exception ex) { LogHelper.Log($"[DOC AUTO] Faxina: embedding da documentação #{d.Cod}: {ex.Message}"); }
            }
        }

        // Pares parecidos que a IA ainda não conferiu, do mais parecido para o menos
        var pares = new List<(DocFaxina A, DocFaxina B, float Nota)>();
        for (var i = 0; i < lista.Count; i++)
            for (var j = i + 1; j < lista.Count; j++)
            {
                var a = lista[i]; var b = lista[j];
                var chave = (Math.Min(a.Cod, b.Cod), Math.Max(a.Cod, b.Cod));
                if (jaConferidos.Contains(chave)) continue;
                var nota = Math.Max(Cosseno(a.Assunto, b.Assunto), Cosseno(a.Tituloemb, b.Tituloemb));
                if (nota >= SemelhancaFaxina) pares.Add((a, b, nota));
            }
        if (pares.Count == 0) return;

        var apagadas = new HashSet<int>();
        int perguntas = 0, juntadas = 0;

        foreach (var (p1, p2, nota) in pares.OrderByDescending(p => p.Nota))
        {
            if (perguntas >= ParesPorFaxina) break;
            if (apagadas.Contains(p1.Cod) || apagadas.Contains(p2.Cod)) continue;
            ct.ThrowIfCancellationRequested();
            if (!LimiteDiarioIa.Consumir(LimiteDiarioIa.Faxina)) break;   // trava de gasto do dia

            // Fica a que tem mais casos (empate: a mais antiga)
            var (fica, sai) = p1.QtdCasos > p2.QtdCasos || (p1.QtdCasos == p2.QtdCasos && p1.Cod < p2.Cod) ? (p1, p2) : (p2, p1);

            var docFica = await docs.BuscarAsync(fica.Cod, codEmp);
            var docSai = await docs.BuscarAsync(sai.Cod, codEmp);
            if (docFica == null) { await EsquecerDocumentacaoAsync(fica.Cod, ct); apagadas.Add(fica.Cod); continue; }
            if (docSai == null) { await EsquecerDocumentacaoAsync(sai.Cod, ct); apagadas.Add(sai.Cod); continue; }

            perguntas++;
            OpenAiClient.DocumentacaoGerada r;
            try
            {
                r = await openAi.JuntarDocumentacoesAsync(docFica.Titulo, HtmlDaDocumentacao(docFica),
                                                          docSai.Titulo, HtmlDaDocumentacao(docSai));
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DOC AUTO] Faxina: #{fica.Cod} x #{sai.Cod}: {ex.Message}");
                continue;   // tenta de novo na próxima faxina
            }

            if (!r.MesmoAssunto)
            {
                await RegistrarParConferidoAsync(codEmp, fica.Cod, sai.Cod, ct);
                continue;
            }

            docFica.Titulo = Limitar(r.Titulo, 150, docFica.Titulo);
            docFica.Descricao = Limitar(r.Resumo, 300, docFica.Descricao);
            docFica.Blocos = BlocoUnico(r.Html);
            docFica.UsuarioAlteracao = UsuarioIa;
            await docs.SalvarAsync(docFica);

            await PassarTicketsAsync(sai.Cod, fica.Cod, ct);
            await docs.ExcluirAsync(sai.Cod, codEmp);

            fica.QtdCasos += sai.QtdCasos;
            fica.Titulo = docFica.Titulo;
            apagadas.Add(sai.Cod);
            juntadas++;
            LogHelper.Log($"[DOC AUTO] Faxina: documentação #{sai.Cod} \"{docSai.Titulo}\" juntada na #{fica.Cod} \"{docFica.Titulo}\" (semelhança {nota:0.00}).");
        }

        LogHelper.Log($"[DOC AUTO] Faxina empresa {codEmp}: {perguntas} par(es) conferido(s) pela IA, {juntadas} documentação(ões) juntada(s).");
    }

    /// <summary>A documentação <paramref name="de"/> foi juntada na <paramref name="para"/>: os tickets e os casos passam para ela.</summary>
    private async Task PassarTicketsAsync(int de, int para, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
SET XACT_ABORT ON;
BEGIN TRAN;

UPDATE dbo.DocumentacaoAutomaticaTicket SET CodDocumentacao = @para WHERE CodDocumentacao = @de;

UPDATE P SET QtdCasos = P.QtdCasos + ISNULL(D.QtdCasos, 0), AtualizadoEm = GETDATE()
FROM dbo.DocumentacaoAutomatica P
LEFT JOIN dbo.DocumentacaoAutomatica D ON D.CodDocumentacao = @de
WHERE P.CodDocumentacao = @para;

DELETE FROM dbo.DocumentacaoAutomatica WHERE CodDocumentacao = @de;
DELETE FROM dbo.DocumentacaoAutomaticaPar WHERE CodA = @de OR CodB = @de;

COMMIT;", conn);
        cmd.Parameters.AddWithValue("@de", de);
        cmd.Parameters.AddWithValue("@para", para);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RegistrarParConferidoAsync(int codEmp, int cod1, int cod2, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.DocumentacaoAutomaticaPar WHERE CodA = @a AND CodB = @b)
    INSERT INTO dbo.DocumentacaoAutomaticaPar (CodA, CodB, CodEmp) VALUES (@a, @b, @e);", conn);
        cmd.Parameters.AddWithValue("@a", Math.Min(cod1, cod2));
        cmd.Parameters.AddWithValue("@b", Math.Max(cod1, cod2));
        cmd.Parameters.AddWithValue("@e", codEmp);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ───────────────────────────── Vetores ─────────────────────────────

    private static byte[] ParaBytes(float[] v)
    {
        var b = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }

    private static float[] ParaVetor(byte[] b)
    {
        var v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, v.Length * sizeof(float));
        return v;
    }

    private static float Cosseno(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}
