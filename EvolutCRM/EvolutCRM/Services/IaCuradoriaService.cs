using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Modelos da tela Configurações › Curadoria da IA
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Caso aprendido (AiTicketInsights) esperando revisão.</summary>
public sealed class IaCasoRevisao
{
    public int Codigo { get; set; }
    public int CodTicket { get; set; }
    public string TopicoTitulo { get; set; } = "";
    public string PerguntaCliente { get; set; } = "";
    public string Resumo { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Tags { get; set; } = "";
    public string CausaProvavel { get; set; } = "";
    public string Solucao { get; set; } = "";
    public int Confianca { get; set; }
    public DateTime CriadoEm { get; set; }
    public string Status { get; set; } = "";       // "" / pendente / suspenso
    public int QtdNegativos { get; set; }
    public int QtdPositivos { get; set; }
    public string Motivo { get; set; } = "";
    public bool Suspenso => Status == "suspenso";
}

/// <summary>Uma pergunta que a IA não soube responder bem.</summary>
public sealed class IaLacunaExemplo
{
    public long CodLog { get; set; }
    public int CodTicket { get; set; }
    public DateTime Data { get; set; }
    public string Pergunta { get; set; } = "";
    public bool SemBase { get; set; }
    public bool Reescrita { get; set; }
    public bool Negativa { get; set; }
}

/// <summary>Assunto que a base não cobre: perguntas parecidas agrupadas.</summary>
public sealed class IaLacunaGrupo
{
    public string Titulo { get; set; } = "";
    public int QtdTickets { get; set; }
    public int QtdPerguntas { get; set; }
    public int SemBase { get; set; }
    public int Reescritas { get; set; }
    public int Negativas { get; set; }
    public DateTime Ultima { get; set; }
    public List<IaLacunaExemplo> Exemplos { get; set; } = new();
}

/// <summary>Documentação usada em sugestões que levaram 👎.</summary>
public sealed class IaDocNegativa
{
    public int CodDoc { get; set; }
    public string Titulo { get; set; } = "";
    public int QtdNegativos { get; set; }
    public DateTime Ultima { get; set; }
}

/// <summary>Um caso dentro de um grupo de casos parecidos.</summary>
public sealed class IaCasoParecido
{
    public int CodInsight { get; set; }
    public int CodTicket { get; set; }
    public string Resumo { get; set; } = "";
    public string Solucao { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    public string Versao { get; set; } = "";
}

/// <summary>Casos que tratam do mesmo problema. Tipo: "conflito" (soluções diferentes), "documentar" (3+ iguais) ou "repetido".</summary>
public sealed class IaGrupoCasos
{
    public string Tipo { get; set; } = "repetido";
    public string Titulo { get; set; } = "";
    public float MenorParecencaSolucao { get; set; }
    public List<IaCasoParecido> Casos { get; set; } = new();
}

/// <summary>
/// Curadoria da IA:
///  1) fila de revisão dos casos aprendidos que o cliente não confirmou (aprovar / corrigir / descartar)
///     e dos casos suspensos por 👎;
///  2) lacunas: perguntas em que a IA ficou sem base, foi reescrita pelo atendente ou levou 👎,
///     agrupadas por assunto;
///  3) documentações que aparecem em sugestões com 👎.
/// </summary>
public sealed class IaCuradoriaService
{
    private readonly string _cs;
    private readonly EmbeddingIndexerService _indexer;
    private readonly EmbeddingIndex _index;
    private readonly OpenAiClient _openAi;

    // Lacunas custam embeddings + 1 chamada à IA: guarda o resultado por alguns minutos
    private static readonly ConcurrentDictionary<(int Emp, int Dias), (DateTime Em, List<IaLacunaGrupo> Grupos)> _cacheLacunas = new();
    private static readonly TimeSpan ValidadeCache = TimeSpan.FromMinutes(15);

    private const float SimilaridadeMesmoAssunto = 0.80f;   // cosseno para duas perguntas serem do mesmo assunto
    private const float MesmoProblema = 0.88f;              // cosseno entre dois casos (problema + solução) para juntar
    private const float SolucaoDiferente = 0.80f;           // abaixo disso, duas soluções do mesmo grupo são diferentes
    private const int MaxCasosAgrupar = 4000;
    private static readonly ConcurrentDictionary<int, (DateTime Em, List<IaGrupoCasos> Grupos)> _cacheGrupos = new();
    private const int MaxPerguntasLacuna = 400;
    private const int MaxGrupos = 40;

    public IaCuradoriaService(IConfiguration config, EmbeddingIndexerService indexer, EmbeddingIndex index, OpenAiClient openAi)
    {
        _cs = config.GetConnectionString("Connection")!;
        _indexer = indexer;
        _index = index;
        _openAi = openAi;
    }

    /// <summary>false = as colunas de revisão não existem (sem permissão de ALTER no banco).</summary>
    public async Task<bool> DisponivelAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        return await IaCuradoriaSchema.GarantirAsync(conn);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1) Fila de revisão
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>filtro: "pendentes" (cliente não confirmou), "suspensos" (2+ 👎) ou "todos".</summary>
    public async Task<List<IaCasoRevisao>> ListarCasosAsync(int codEmp, string filtro, string? busca)
    {
        var lista = new List<IaCasoRevisao>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        if (!await IaCuradoriaSchema.GarantirAsync(conn)) return lista;

        await using var cmd = new SqlCommand(@"
SELECT TOP 200
       I.Codigo, I.CodTicketChamadoC, ISNULL(I.TopicoTitulo, ''), ISNULL(I.PerguntaCliente, ''), ISNULL(I.Resumo, ''),
       ISNULL(I.Categoria, ''), ISNULL(I.Tags, ''), ISNULL(I.CausaProvavel, ''), ISNULL(I.Solucao, ''),
       ISNULL(I.Confianca, 0), I.CriadoEm, ISNULL(I.StatusRevisao, ''), I.QtdNegativos, I.QtdPositivos,
       ISNULL(I.MotivoRevisao, '')
FROM dbo.AiTicketInsights I
LEFT JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
WHERE (@emp = 0 OR T.CodEmp = @emp OR T.CodEmp IS NULL)
  AND ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
  AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
  AND (
        (@f IN ('pendentes', 'todos') AND ISNULL(I.StatusRevisao, '') IN ('', 'pendente') AND I.SolucaoConfirmada = 0)
     OR (@f IN ('suspensos', 'todos') AND I.StatusRevisao = 'suspenso')
      )
  AND (@busca = '' OR I.Resumo LIKE @like OR I.Solucao LIKE @like OR I.Tags LIKE @like
       OR I.PerguntaCliente LIKE @like OR CAST(I.CodTicketChamadoC AS VARCHAR(20)) = @busca)
ORDER BY CASE WHEN I.StatusRevisao = 'suspenso' THEN 0 ELSE 1 END, I.Confianca DESC, I.CriadoEm DESC;", conn);
        var b = (busca ?? "").Trim();
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@f", filtro is "suspensos" or "todos" ? filtro : "pendentes");
        cmd.Parameters.AddWithValue("@busca", b);
        cmd.Parameters.AddWithValue("@like", "%" + b.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IaCasoRevisao
            {
                Codigo = rd.GetInt32(0),
                CodTicket = rd.GetInt32(1),
                TopicoTitulo = rd.GetString(2),
                PerguntaCliente = rd.GetString(3),
                Resumo = rd.GetString(4),
                Categoria = rd.GetString(5),
                Tags = rd.GetString(6),
                CausaProvavel = rd.GetString(7),
                Solucao = rd.GetString(8),
                Confianca = Convert.ToInt32(rd.GetValue(9)),
                CriadoEm = rd.IsDBNull(10) ? DateTime.MinValue : rd.GetDateTime(10),
                Status = rd.GetString(11),
                QtdNegativos = rd.GetInt32(12),
                QtdPositivos = rd.GetInt32(13),
                Motivo = rd.GetString(14)
            });
        }
        return lista;
    }

    public async Task<(int Pendentes, int Suspensos)> ContarAsync(int codEmp)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        if (!await IaCuradoriaSchema.GarantirAsync(conn)) return (0, 0);

        await using var cmd = new SqlCommand(@"
SELECT
    SUM(CASE WHEN ISNULL(I.StatusRevisao, '') IN ('', 'pendente') AND I.SolucaoConfirmada = 0 THEN 1 ELSE 0 END),
    SUM(CASE WHEN I.StatusRevisao = 'suspenso' THEN 1 ELSE 0 END)
FROM dbo.AiTicketInsights I
LEFT JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
WHERE (@emp = 0 OR T.CodEmp = @emp OR T.CodEmp IS NULL)
  AND ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
  AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> '';", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return (0, 0);
        return (rd.IsDBNull(0) ? 0 : rd.GetInt32(0), rd.IsDBNull(1) ? 0 : rd.GetInt32(1));
    }

    // ── Em lote (só os não confirmados pelo cliente; suspensos sempre um por um) ──

    /// <summary>Confiança mínima do "Aprovar em lote".</summary>
    public const int LoteAprovarMinimo = 85;
    /// <summary>Abaixo disso, o "Descartar em lote".</summary>
    public const int LoteDescartarAbaixoDe = 50;

    private const string FiltroPendentesLote = @"
FROM dbo.AiTicketInsights I
LEFT JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
WHERE (@emp = 0 OR T.CodEmp = @emp OR T.CodEmp IS NULL)
  AND ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
  AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
  AND ISNULL(I.StatusRevisao, '') IN ('', 'pendente')
  AND I.SolucaoConfirmada = 0";

    /// <summary>Quantos casos cada botão em lote pegaria.</summary>
    public async Task<(int Aprovar, int Descartar)> ContarLoteAsync(int codEmp)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        if (!await IaCuradoriaSchema.GarantirAsync(conn)) return (0, 0);

        await using var cmd = new SqlCommand($@"
SELECT SUM(CASE WHEN ISNULL(I.Confianca, 0) >= @min THEN 1 ELSE 0 END),
       SUM(CASE WHEN ISNULL(I.Confianca, 0) <  @max THEN 1 ELSE 0 END)
{FiltroPendentesLote};", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@min", LoteAprovarMinimo);
        cmd.Parameters.AddWithValue("@max", LoteDescartarAbaixoDe);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return (0, 0);
        return (rd.IsDBNull(0) ? 0 : rd.GetInt32(0), rd.IsDBNull(1) ? 0 : rd.GetInt32(1));
    }

    /// <summary>Aprova todos os não confirmados com confiança >= <see cref="LoteAprovarMinimo"/>.</summary>
    public async Task<int> AprovarLoteAsync(int codEmp, string usuario)
    {
        int n;
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            if (!await IaCuradoriaSchema.GarantirAsync(conn)) return 0;

            await using var cmd = new SqlCommand($@"
UPDATE I
   SET StatusRevisao = 'aprovado', QtdNegativos = 0, RevisadoPor = @u, RevisadoEm = GETDATE(),
       MotivoRevisao = N'Aprovado em lote (confiança >= ' + CAST(@min AS NVARCHAR(10)) + N')'
{FiltroPendentesLote}
  AND ISNULL(I.Confianca, 0) >= @min;", conn);
            cmd.Parameters.AddWithValue("@emp", codEmp);
            cmd.Parameters.AddWithValue("@min", LoteAprovarMinimo);
            cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100));
            cmd.CommandTimeout = 120;
            n = await cmd.ExecuteNonQueryAsync();
        }

        if (n > 0)
        {
            // Gera os embeddings que faltarem e recarrega o índice inteiro de uma vez
            try
            {
                await _indexer.IndexarInsightsAsync();
                await _index.ReloadAsync();
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[IA CURADORIA] {n} caso(s) aprovado(s) em lote; índice atualiza na madrugada: {ex.Message}");
            }
        }
        return n;
    }

    /// <summary>Descarta todos os não confirmados com confiança abaixo de <see cref="LoteDescartarAbaixoDe"/>.</summary>
    public async Task<int> DescartarLoteAsync(int codEmp, string usuario)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        if (!await IaCuradoriaSchema.GarantirAsync(conn)) return 0;

        // Não estavam na IA (não confirmados): não precisa mexer no índice
        await using var cmd = new SqlCommand($@"
UPDATE I
   SET StatusRevisao = 'descartado', RevisadoPor = @u, RevisadoEm = GETDATE(),
       MotivoRevisao = N'Descartado em lote (confiança abaixo de ' + CAST(@max AS NVARCHAR(10)) + N')'
{FiltroPendentesLote}
  AND ISNULL(I.Confianca, 0) < @max;", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@max", LoteDescartarAbaixoDe);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100));
        cmd.CommandTimeout = 120;
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Libera o caso para a IA. Com os campos preenchidos, grava a correção antes.</summary>
    public async Task AprovarAsync(int codigo, string usuario,
        string? resumo = null, string? causa = null, string? solucao = null, string? tags = null)
    {
        var corrigido = resumo != null || causa != null || solucao != null || tags != null;
        if (corrigido && (string.IsNullOrWhiteSpace(resumo) || string.IsNullOrWhiteSpace(solucao)))
            throw new InvalidOperationException("Resumo e solução são obrigatórios.");

        int ticket;
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            if (!await IaCuradoriaSchema.GarantirAsync(conn))
                throw new InvalidOperationException("As colunas de revisão não existem no banco. Rode Scripts/2026-10-28_IA_Curadoria.sql.");

            var sql = corrigido
                ? @"
UPDATE dbo.AiTicketInsights
   SET Resumo = @resumo, CausaProvavel = @causa, Solucao = @solucao, Tags = @tags,
       Embedding = NULL,   -- texto mudou: gera de novo
       StatusRevisao = 'aprovado', QtdNegativos = 0, RevisadoPor = @u, RevisadoEm = GETDATE(),
       MotivoRevisao = N'Corrigido na curadoria'
OUTPUT INSERTED.CodTicketChamadoC
 WHERE Codigo = @c;"
                : @"
UPDATE dbo.AiTicketInsights
   SET StatusRevisao = 'aprovado', QtdNegativos = 0, RevisadoPor = @u, RevisadoEm = GETDATE(), MotivoRevisao = NULL
OUTPUT INSERTED.CodTicketChamadoC
 WHERE Codigo = @c;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@c", codigo);
            cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100));
            if (corrigido)
            {
                cmd.Parameters.AddWithValue("@resumo", Cortar(Limpar(resumo), 300));
                cmd.Parameters.AddWithValue("@causa", Cortar(Limpar(causa), 1000));
                cmd.Parameters.AddWithValue("@solucao", Cortar(Limpar(solucao), 1200));
                cmd.Parameters.AddWithValue("@tags", Cortar(NormalizarTags(tags), 200));
            }
            var r = await cmd.ExecuteScalarAsync();
            if (r == null || r == DBNull.Value) throw new InvalidOperationException("Caso não encontrado.");
            ticket = Convert.ToInt32(r);
        }

        // Gera o embedding (se faltar) e coloca no índice da IA na hora
        try
        {
            await _indexer.IndexarInsightsDoTicketAsync(ticket);
            await _index.AtualizarInsightsDoTicketAsync(ticket);
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[IA CURADORIA] Caso {codigo} aprovado, mas não entrou no índice agora (entra na rotina da madrugada): {ex.Message}");
        }
    }

    /// <summary>Joga o caso fora: a IA nunca mais usa (mesmo se tiver sido confirmado).</summary>
    public async Task DescartarAsync(int codigo, string usuario, string? motivo)
    {
        int ticket;
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            if (!await IaCuradoriaSchema.GarantirAsync(conn))
                throw new InvalidOperationException("As colunas de revisão não existem no banco. Rode Scripts/2026-10-28_IA_Curadoria.sql.");

            await using var cmd = new SqlCommand(@"
UPDATE dbo.AiTicketInsights
   SET StatusRevisao = 'descartado', RevisadoPor = @u, RevisadoEm = GETDATE(), MotivoRevisao = @m
OUTPUT INSERTED.CodTicketChamadoC
 WHERE Codigo = @c;", conn);
            cmd.Parameters.AddWithValue("@c", codigo);
            cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100));
            cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(motivo) ? "Descartado na curadoria" : Cortar(motivo.Trim(), 300));
            var r = await cmd.ExecuteScalarAsync();
            if (r == null || r == DBNull.Value) throw new InvalidOperationException("Caso não encontrado.");
            ticket = Convert.ToInt32(r);
        }

        try { await _index.AtualizarInsightsDoTicketAsync(ticket); }
        catch (Exception ex) { LogHelper.Log($"[IA CURADORIA] Caso {codigo} descartado; índice atualiza na madrugada: {ex.Message}"); }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2) Lacunas: o que a base não cobre
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Perguntas dos últimos <paramref name="dias"/> em que a IA ficou sem base (nível BAIXO ou sem fonte),
    /// o atendente reescreveu a sugestão (similaridade &lt; 40) ou deu 👎, agrupadas por assunto.
    /// </summary>
    public async Task<List<IaLacunaGrupo>> ListarLacunasAsync(int codEmp, int dias, bool forcar = false)
    {
        dias = Math.Clamp(dias, 1, 365);
        var chave = (codEmp, dias);
        if (!forcar && _cacheLacunas.TryGetValue(chave, out var c) && DateTime.UtcNow - c.Em < ValidadeCache)
            return c.Grupos;

        // 1) Perguntas
        var exemplos = new List<IaLacunaExemplo>();
        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.AiSugestaoLog', 'U') IS NOT NULL
SELECT TOP (@max) L.CodAiSugestaoLog, ISNULL(L.CodTicketChamadoC, 0), L.DataHora, L.Pergunta,
       CASE WHEN L.Nivel = 'BAIXO' OR ISNULL(L.Fontes, '[]') = '[]' THEN 1 ELSE 0 END AS SemBase,
       CASE WHEN L.DataHoraEnvio IS NOT NULL AND L.Similaridade < 40 THEN 1 ELSE 0 END AS Reescrita,
       CASE WHEN L.Feedback = -1 THEN 1 ELSE 0 END AS Negativa
FROM dbo.AiSugestaoLog L
WHERE L.Origem = 'ticket'
  AND L.DataHora >= DATEADD(DAY, -@dias, GETDATE())
  AND (@emp = 0 OR L.CodEmp = @emp)
  AND ISNULL(LTRIM(RTRIM(L.Pergunta)), '') <> ''
  AND (L.Nivel = 'BAIXO' OR ISNULL(L.Fontes, '[]') = '[]'
       OR (L.DataHoraEnvio IS NOT NULL AND L.Similaridade < 40)
       OR L.Feedback = -1)
ORDER BY L.DataHora DESC;", conn);
            cmd.Parameters.AddWithValue("@max", MaxPerguntasLacuna);
            cmd.Parameters.AddWithValue("@dias", dias);
            cmd.Parameters.AddWithValue("@emp", codEmp);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                exemplos.Add(new IaLacunaExemplo
                {
                    CodLog = rd.GetInt64(0),
                    CodTicket = rd.GetInt32(1),
                    Data = rd.GetDateTime(2),
                    Pergunta = (rd.GetString(3) ?? "").Trim(),
                    SemBase = rd.GetInt32(4) == 1,
                    Reescrita = rd.GetInt32(5) == 1,
                    Negativa = rd.GetInt32(6) == 1
                });
            }
        }

        // A mesma dúvida pedida várias vezes no mesmo ticket conta uma vez só (fica a mais recente)
        exemplos = exemplos
            .GroupBy(e => (e.CodTicket, Chave: EmbeddingIndex.Normalizar(e.Pergunta)))
            .Select(g => g.OrderByDescending(x => x.Data).First())
            .OrderByDescending(e => e.Data)
            .ToList();

        var grupos = new List<IaLacunaGrupo>();
        if (exemplos.Count == 0)
        {
            _cacheLacunas[chave] = (DateTime.UtcNow, grupos);
            return grupos;
        }

        // 2) Agrupa por significado (embeddings numa chamada só); sem embedding, cada pergunta é um grupo
        List<float[]> vetores;
        try { vetores = await _openAi.GetEmbeddingsAsync(exemplos.Select(e => e.Pergunta).ToList()); }
        catch (Exception ex)
        {
            LogHelper.Log("[IA CURADORIA] Lacunas sem agrupamento (embeddings falharam): " + ex.Message);
            vetores = exemplos.Select(_ => Array.Empty<float>()).ToList();
        }

        var montagem = new List<(List<int> Itens, float[] Soma)>();
        for (int i = 0; i < exemplos.Count; i++)
        {
            var v = i < vetores.Count ? vetores[i] : Array.Empty<float>();
            int melhor = -1;
            float melhorCos = 0;
            if (v.Length > 0)
            {
                for (int g = 0; g < montagem.Count; g++)
                {
                    var cos = Cosseno(v, montagem[g].Soma);
                    if (cos > melhorCos) { melhorCos = cos; melhor = g; }
                }
            }

            if (melhor >= 0 && melhorCos >= SimilaridadeMesmoAssunto)
            {
                montagem[melhor].Itens.Add(i);
                var soma = montagem[melhor].Soma;
                for (int k = 0; k < soma.Length && k < v.Length; k++) soma[k] += v[k];
            }
            else
            {
                montagem.Add((new List<int> { i }, v.Length > 0 ? (float[])v.Clone() : Array.Empty<float>()));
            }
        }

        foreach (var m in montagem)
        {
            var itens = m.Itens.Select(i => exemplos[i]).OrderByDescending(e => e.Data).ToList();
            grupos.Add(new IaLacunaGrupo
            {
                QtdTickets = itens.Select(e => e.CodTicket).Where(t => t > 0).Distinct().Count(),
                QtdPerguntas = itens.Count,
                SemBase = itens.Count(e => e.SemBase),
                Reescritas = itens.Count(e => e.Reescrita),
                Negativas = itens.Count(e => e.Negativa),
                Ultima = itens.Max(e => e.Data),
                Exemplos = itens.Take(8).ToList(),
                // título provisório: a pergunta mais curta do grupo costuma ser a mais "limpa"
                Titulo = Cortar(itens.Select(e => e.Pergunta).OrderBy(p => p.Length).First(), 120)
            });
        }

        grupos = grupos
            .OrderByDescending(g => Math.Max(g.QtdTickets, 1))
            .ThenByDescending(g => g.QtdPerguntas)
            .ThenByDescending(g => g.Ultima)
            .Take(MaxGrupos)
            .ToList();

        // 3) Nome do assunto de cada grupo (uma chamada só)
        try
        {
            var titulos = await _openAi.TitularGruposAsync(
                grupos.Select(g => (IReadOnlyList<string>)g.Exemplos.Select(e => e.Pergunta).ToList()).ToList());
            for (int g = 0; g < grupos.Count && g < titulos.Count; g++)
                if (!string.IsNullOrWhiteSpace(titulos[g])) grupos[g].Titulo = Cortar(titulos[g], 120);
        }
        catch (Exception ex)
        {
            LogHelper.Log("[IA CURADORIA] Títulos das lacunas: " + ex.Message);
        }

        _cacheLacunas[chave] = (DateTime.UtcNow, grupos);
        return grupos;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3) Documentações com 👎
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<IaDocNegativa>> ListarDocsComNegativoAsync(int codEmp, int dias)
    {
        dias = Math.Clamp(dias, 1, 365);
        var porDoc = new Dictionary<int, IaDocNegativa>();

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.AiSugestaoLog', 'U') IS NOT NULL
SELECT L.Fontes, L.DataHora
FROM dbo.AiSugestaoLog L
WHERE L.Origem = 'ticket'
  AND L.Feedback = -1
  AND L.DataHora >= DATEADD(DAY, -@dias, GETDATE())
  AND (@emp = 0 OR L.CodEmp = @emp)
  AND ISNULL(L.Fontes, '[]') <> '[]';", conn);
        cmd.Parameters.AddWithValue("@dias", dias);
        cmd.Parameters.AddWithValue("@emp", codEmp);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var json = rd.IsDBNull(0) ? "" : rd.GetString(0);
            var data = rd.GetDateTime(1);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var f in doc.RootElement.EnumerateArray())
                {
                    if (!f.TryGetProperty("Tipo", out var tp) || tp.GetString() != "doc") continue;
                    var cod = f.TryGetProperty("CodDoc", out var cd) && cd.ValueKind == JsonValueKind.Number ? cd.GetInt32() : 0;
                    if (cod <= 0) continue;
                    var titulo = f.TryGetProperty("Titulo", out var ti) ? ti.GetString() ?? "" : "";

                    if (!porDoc.TryGetValue(cod, out var d))
                        porDoc[cod] = d = new IaDocNegativa { CodDoc = cod, Titulo = titulo, Ultima = data };
                    d.QtdNegativos++;
                    if (data > d.Ultima) d.Ultima = data;
                    if (string.IsNullOrWhiteSpace(d.Titulo)) d.Titulo = titulo;
                }
            }
            catch
            {
                // JSON antigo/inválido: ignora a linha
            }
        }

        return porDoc.Values
            .OrderByDescending(d => d.QtdNegativos)
            .ThenByDescending(d => d.Ultima)
            .ToList();
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4) Casos repetidos e conflitantes
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Junta os casos que a IA usa (os que estão no índice) quando tratam do mesmo problema.
    ///  - conflito: o mesmo problema com soluções diferentes → revisar e ficar com a certa;
    ///  - documentar: 3 ou mais casos com a mesma solução → vale virar documentação oficial;
    ///  - repetido: 2 casos iguais (pode descartar um).
    /// Usa os vetores que já estão no índice; só as soluções dos casos agrupados ganham embedding novo.
    /// </summary>
    public async Task<List<IaGrupoCasos>> AgruparCasosAsync(int codEmp, bool forcar = false)
    {
        if (!forcar && _cacheGrupos.TryGetValue(codEmp, out var c) && DateTime.UtcNow - c.Em < TimeSpan.FromMinutes(60))
            return c.Grupos;

        var casos = _index.InsightsCarregados
            .Where(x => x.Vec.Length > 0 && (codEmp <= 0 || x.CodEmp <= 0 || x.CodEmp == codEmp))
            .OrderByDescending(x => x.CriadoEm)
            .Take(MaxCasosAgrupar)
            .ToList();

        // Vetores normalizados: o cosseno vira produto escalar
        var vetores = casos.Select(x => Normalizar(x.Vec)).ToArray();
        int n = casos.Count;

        // Pares do mesmo problema (em paralelo: são n²/2 comparações)
        var pares = new ConcurrentBag<(int A, int B)>();
        Parallel.For(0, n, i =>
        {
            var vi = vetores[i];
            for (int j = i + 1; j < n; j++)
            {
                var vj = vetores[j];
                if (vi.Length != vj.Length) continue;
                double dot = 0;
                for (int k = 0; k < vi.Length; k++) dot += vi[k] * vj[k];
                if (dot >= MesmoProblema) pares.Add((i, j));
            }
        });

        // União dos pares em grupos
        var pai = Enumerable.Range(0, n).ToArray();
        int Raiz(int x) { while (pai[x] != x) { pai[x] = pai[pai[x]]; x = pai[x]; } return x; }
        foreach (var (a, b) in pares)
        {
            int ra = Raiz(a), rb = Raiz(b);
            if (ra != rb) pai[ra] = rb;
        }
        var grupos = Enumerable.Range(0, n)
            .GroupBy(Raiz)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Select(i => casos[i]).ToList())
            .Where(g => g.Select(x => x.CodInsight).Distinct().Count() >= 2)
            .ToList();

        // Soluções dos casos agrupados: um embedding por solução (em lote)
        var membros = grupos.SelectMany(g => g).ToList();
        var vetSolucao = new Dictionary<int, float[]>();
        if (membros.Count > 0)
        {
            try
            {
                var vs = await _openAi.GetEmbeddingsAsync(membros.Select(m => m.Solucao ?? "").ToList());
                for (int i = 0; i < membros.Count && i < vs.Count; i++)
                    vetSolucao[membros[i].CodInsight] = Normalizar(vs[i]);
            }
            catch (Exception ex)
            {
                LogHelper.Log("[IA CURADORIA] Casos repetidos sem comparar soluções: " + ex.Message);
            }
        }

        var resultado = new List<IaGrupoCasos>();
        foreach (var g in grupos)
        {
            float menor = 1f;
            for (int i = 0; i < g.Count; i++)
                for (int j = i + 1; j < g.Count; j++)
                {
                    if (!vetSolucao.TryGetValue(g[i].CodInsight, out var a) || !vetSolucao.TryGetValue(g[j].CodInsight, out var b) || a.Length != b.Length)
                        continue;
                    double dot = 0;
                    for (int k = 0; k < a.Length; k++) dot += a[k] * b[k];
                    menor = Math.Min(menor, (float)dot);
                }

            var tipo = menor < SolucaoDiferente ? "conflito" : g.Count >= 3 ? "documentar" : "repetido";
            resultado.Add(new IaGrupoCasos
            {
                Tipo = tipo,
                MenorParecencaSolucao = menor,
                Titulo = Cortar(g.Select(x => x.Resumo ?? "").Where(r => r.Length > 0).OrderBy(r => r.Length).FirstOrDefault() ?? "", 140),
                Casos = g.OrderByDescending(x => x.CriadoEm).Select(x => new IaCasoParecido
                {
                    CodInsight = x.CodInsight,
                    CodTicket = x.TicketId,
                    Resumo = x.Resumo ?? "",
                    Solucao = x.Solucao ?? "",
                    CriadoEm = x.CriadoEm,
                    Versao = x.Versao ?? ""
                }).ToList()
            });
        }

        resultado = resultado
            .OrderBy(g => g.Tipo == "conflito" ? 0 : g.Tipo == "documentar" ? 1 : 2)
            .ThenByDescending(g => g.Casos.Count)
            .ToList();

        _cacheGrupos[codEmp] = (DateTime.UtcNow, resultado);
        return resultado;
    }

    /// <summary>Tira um caso do grupo guardado (depois de descartar), sem refazer o agrupamento.</summary>
    public static void RemoverDoAgrupamento(int codEmp, int codInsight)
    {
        if (!_cacheGrupos.TryGetValue(codEmp, out var c)) return;
        foreach (var g in c.Grupos) g.Casos.RemoveAll(x => x.CodInsight == codInsight);
        c.Grupos.RemoveAll(g => g.Casos.Count < 2);
    }

    /// <summary>Rascunho de documentação a partir dos casos de um grupo (para revisar e colar no editor).</summary>
    public Task<string> RascunharDocumentacaoAsync(IaGrupoCasos grupo)
        => _openAi.RascunharDocumentacaoAsync(grupo.Casos.Select(x => (x.Resumo, x.Solucao)).ToList());

    private static float[] Normalizar(float[] v)
    {
        double n = 0;
        foreach (var x in v) n += x * x;
        n = Math.Sqrt(n);
        if (n <= 0) return v;
        var r = new float[v.Length];
        for (int i = 0; i < v.Length; i++) r[i] = (float)(v[i] / n);
        return r;
    }

    // ═════════════════════════════════════════════════════════════════════

    private static float Cosseno(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9));
    }

    private static string Limpar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private static string NormalizarTags(string? tags)
        => string.Join(";", (tags ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant().Replace(" ", "_"))
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(12));

    private static string Cortar(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}
