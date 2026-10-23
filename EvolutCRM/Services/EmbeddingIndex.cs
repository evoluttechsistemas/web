using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

public sealed class EmbeddingIndex
{
    private readonly string _cs;
    private volatile List<InsightVec> _insights = new();
    private volatile List<DocVec> _docs = new();
    public DateTime CarregadoEm { get; private set; }

    public EmbeddingIndex(IConfiguration config) => _cs = config.GetConnectionString("Connection")!;

    public sealed record InsightVec(int TicketId, int TopicoIndex, string? Resumo,
        string? Tags, string? Solucao, DateTime CriadoEm, float[] Vec, int CodEmp = 0);
    public sealed record DocVec(int CodDoc, string Slug, string Titulo, string Texto, float[] Vec, int CodEmp = 0);

    // Mesmo critério em todo lugar: só casos que ajudam a responder
    private const string FiltroInsights = @"
          I.Embedding IS NOT NULL
          AND ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
          -- só casos que ajudam a responder: com solução e (confirmada ou confiança alta)
          AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
          AND (I.SolucaoConfirmada = 1 OR ISNULL(I.Confianca, 0) >= 70)";

    private readonly object _trava = new();

    /// <summary>Recarrega o índice inteiro do banco (início do sistema, reindexar e rotina da madrugada).</summary>
    public async Task ReloadAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var ins = await CarregarInsightsAsync(conn, null);
        var docs = await CarregarDocsAsync(conn, null);

        lock (_trava)
        {
            _insights = ins;
            _docs = docs;
            CarregadoEm = DateTime.UtcNow;
        }
        Console.WriteLine($"[EmbeddingIndex] Carregado do banco: {ins.Count} insights, {docs.Count} docs");
    }

    /// <summary>Troca no índice só os insights deste ticket (depois de gerar o insight).</summary>
    public async Task AtualizarInsightsDoTicketAsync(int codTicket)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var novos = await CarregarInsightsAsync(conn, codTicket);

        lock (_trava)
        {
            var lista = _insights.Where(x => x.TicketId != codTicket).ToList();
            lista.AddRange(novos);
            _insights = lista;
        }
        Console.WriteLine($"[EmbeddingIndex] Ticket #{codTicket}: {novos.Count} insight(s) no índice");
    }

    /// <summary>Troca no índice só os pedaços desta documentação (depois de salvar ou excluir).</summary>
    public async Task AtualizarDocAsync(int codDoc)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var novos = await CarregarDocsAsync(conn, codDoc);

        lock (_trava)
        {
            var lista = _docs.Where(x => x.CodDoc != codDoc).ToList();
            lista.AddRange(novos);
            _docs = lista;
        }
        Console.WriteLine($"[EmbeddingIndex] Documentação #{codDoc}: {novos.Count} pedaço(s) no índice");
    }

    private static async Task<List<InsightVec>> CarregarInsightsAsync(SqlConnection conn, int? codTicket)
    {
        var ins = new List<InsightVec>();
        await using var cmd = new SqlCommand($@"
        SELECT I.CodTicketChamadoC, I.TopicoIndex, I.Resumo, I.Tags, I.Solucao, I.CriadoEm, I.Embedding,
               ISNULL(T.CodEmp, 0) AS CodEmp
        FROM dbo.AiTicketInsights I
        LEFT JOIN TicketChamadoC T ON T.Codigo = I.CodTicketChamadoC
        WHERE {FiltroInsights}
          AND (@Ticket IS NULL OR I.CodTicketChamadoC = @Ticket);", conn);
        cmd.Parameters.Add("@Ticket", System.Data.SqlDbType.Int).Value = (object?)codTicket ?? DBNull.Value;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var vec = JsonSerializer.Deserialize<float[]>(rd["Embedding"]!.ToString()!)!;
            ins.Add(new InsightVec(
                rd.GetInt32(0), rd["TopicoIndex"] is int t ? t : 1,
                rd["Resumo"] as string, rd["Tags"] as string, rd["Solucao"] as string,
                rd["CriadoEm"] is DateTime dt ? dt : DateTime.MinValue, vec,
                Convert.ToInt32(rd["CodEmp"])));
        }
        return ins;
    }

    private static async Task<List<DocVec>> CarregarDocsAsync(SqlConnection conn, int? codDoc)
    {
        var docs = new List<DocVec>();
        await using var cmd = new SqlCommand(@"
        SELECT CodDocumentacaoTutorial, Slug, Titulo, Texto, Embedding, ISNULL(CodEmp, 0) AS CodEmp
        FROM dbo.DocChunk
        WHERE Embedding IS NOT NULL
          AND (@Doc IS NULL OR CodDocumentacaoTutorial = @Doc);", conn);
        cmd.Parameters.Add("@Doc", System.Data.SqlDbType.Int).Value = (object?)codDoc ?? DBNull.Value;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var vec = JsonSerializer.Deserialize<float[]>(rd["Embedding"]!.ToString()!)!;
            docs.Add(new DocVec(rd.GetInt32(0), rd["Slug"]?.ToString() ?? "",
                rd["Titulo"]?.ToString() ?? "", rd["Texto"]?.ToString() ?? "", vec,
                Convert.ToInt32(rd["CodEmp"])));
        }
        return docs;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Busca híbrida: cosseno (0.7) + palavras em comum pesadas por raridade (0.3) + termos exatos
    //  - sem acento, sem palavras vazias ("para", "como", "sistema"...), plural simples cortado
    //  - palavra rara (ex.: "sangria", "cfop") vale mais que palavra comum (ex.: "cliente")
    //  - sinônimos contam metade
    //  - no máximo N pedaços por documentação / N casos por ticket, para variar o contexto

    // codEmp > 0: só itens da empresa do ticket (itens sem empresa conhecida continuam valendo)
    public List<(InsightVec Item, float Score)> SearchInsights(float[] q, string query, int top, int codEmp = 0,
        string? sinonimos = null, int maxPorTicket = 2)
    {
        var lista = _insights;
        var idf = Idf();
        var toks = Tokens(query);
        var sin = Tokens(sinonimos ?? "");
        sin.ExceptWith(toks);

        return Diversificar(
            lista.Where(x => codEmp <= 0 || x.CodEmp <= 0 || x.CodEmp == codEmp)
                 .Select(x => (x, Score: Hybrid(q, x.Vec, TokensDoItem(x), $"{x.Resumo} {x.Tags} {x.Solucao}", toks, sin, idf, query)))
                 .OrderByDescending(r => r.Score),
            r => r.x.TicketId, maxPorTicket, top);
    }

    public List<(DocVec Item, float Score)> SearchDocs(float[] q, string query, int top, int codEmp = 0,
        string? sinonimos = null, int maxPorDoc = 2)
    {
        var lista = _docs;
        var idf = Idf();
        var toks = Tokens(query);
        var sin = Tokens(sinonimos ?? "");
        sin.ExceptWith(toks);

        return Diversificar(
            lista.Where(x => codEmp <= 0 || x.CodEmp <= 0 || x.CodEmp == codEmp)
                 .Select(x => (x, Score: Hybrid(q, x.Vec, TokensDoItem(x), $"{x.Titulo} {x.Texto}", toks, sin, idf, query)))
                 .OrderByDescending(r => r.Score),
            r => r.x.CodDoc, maxPorDoc, top);
    }

    private static List<(T Item, float Score)> Diversificar<T>(IEnumerable<(T x, float Score)> ordenados,
        Func<(T x, float Score), int> chave, int maxPorChave, int top)
    {
        var res = new List<(T Item, float Score)>();
        var cont = new Dictionary<int, int>();
        foreach (var r in ordenados)
        {
            var k = chave(r);
            cont.TryGetValue(k, out var n);
            if (maxPorChave > 0 && n >= maxPorChave) continue;
            cont[k] = n + 1;
            res.Add((r.x, r.Score));
            if (res.Count >= top) break;
        }
        return res;
    }

    private static float Hybrid(float[] q, float[] d, HashSet<string> dToks, string textoOriginal,
        HashSet<string> qToks, HashSet<string> sinToks, Dictionary<string, float> idf, string queryRaw)
    {
        float cos = Cosine(q, d);

        // Palavras da pergunta encontradas no item, pesadas pela raridade
        float total = 0, achou = 0;
        foreach (var t in qToks)
        {
            var w = PesoIdf(idf, t);
            total += w;
            if (dToks.Contains(t)) achou += w;
        }
        float overlap = total <= 0 ? 0 : achou / total;

        // Sinônimos: ajudam, mas valem menos que a palavra que o cliente usou
        if (sinToks.Count > 0)
        {
            float st = 0, sa = 0;
            foreach (var t in sinToks)
            {
                var w = PesoIdf(idf, t);
                st += w;
                if (dToks.Contains(t)) sa += w;
            }
            if (st > 0) overlap = Math.Min(1f, overlap + 0.5f * (sa / st) * (1 - overlap));
        }

        float boost = 0;
        foreach (var tk in TokensEspeciais(queryRaw))
            if (Normalizar(textoOriginal).Contains(Normalizar(tk))) boost += 0.08f;

        return 0.7f * cos + 0.3f * overlap + Math.Min(boost, 0.2f);
    }

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9));
    }

    // ── Palavras ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> PalavrasVazias = new(StringComparer.Ordinal)
    {
        "que","para","pra","pro","com","sem","como","uma","umas","uns","dos","das","nos","nas","num","numa",
        "por","pelo","pela","pelos","pelas","mais","menos","muito","muita","isso","isto","esse","essa","este",
        "esta","aqui","ali","ele","ela","eles","elas","voce","voces","meu","minha","seu","sua","nao","sim",
        "tem","ter","tenho","tinha","estou","estava","sao","ser","foi","era","vai","vou","fazer","faz",
        "consigo","consegue","conseguir","quando","onde","qual","quais","porque","pois","entao","tambem",
        "ainda","depois","antes","agora","hoje","ontem","bom","boa","dia","tarde","noite","obrigado","obrigada",
        "favor","ola","oi","tudo","bem","gostaria","preciso","precisa","queria","quero","sistema","evolut",
        "alguem","ajuda","ajudar","duvida","problema","aparece","aparecendo","fica","ficou","dar","deu",
        "faco","fiz","feito","algum","alguma","nenhum","coisa","jeito","forma","certinho","esta",
        "ate","sobre","entre","cada","outro","outra","mesmo","mesma","pode","podem","poderia","the","and"
    };

    /// <summary>Minúsculo e sem acento.</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>Corte simples de plural/variação: "notas"→"nota", "emissoes"→"emissao", "cancelamentos"→"cancelamento".</summary>
    private static string Raiz(string w)
    {
        if (w.Length <= 4 || Regex.IsMatch(w, @"\d")) return w;
        if (w.EndsWith("coes")) return w[..^4] + "cao";
        if (w.EndsWith("oes")) return w[..^3] + "ao";
        if (w.EndsWith("aes")) return w[..^3] + "ao";
        if (w.EndsWith("ais")) return w[..^3] + "al";
        if (w.EndsWith("eis")) return w[..^3] + "el";
        if (w.EndsWith("res") || w.EndsWith("zes")) return w[..^2];
        if (w.EndsWith("s") && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is")) return w[..^1];
        return w;
    }

    public static HashSet<string> Tokens(string? s)
    {
        var res = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Normalizar(s), @"[a-z0-9]+"))
        {
            var w = m.Value;
            if (w.Length < 3 && !Regex.IsMatch(w, @"^(f\d+|\d{2,})$")) continue;
            if (PalavrasVazias.Contains(w)) continue;
            res.Add(Raiz(w));
        }
        return res;
    }

    // Tokens de cada item, calculados uma vez só
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, HashSet<string>> _tokensItem = new();

    private HashSet<string> TokensDoItem(InsightVec x) =>
        _tokensItem.GetValue(x, _ => Tokens($"{x.Resumo} {x.Tags} {x.Solucao}"));

    private HashSet<string> TokensDoItem(DocVec x) =>
        _tokensItem.GetValue(x, _ => Tokens($"{x.Titulo} {x.Texto}"));

    // Raridade de cada palavra em toda a base (recalcula quando o índice muda)
    private Dictionary<string, float>? _idf;
    private object? _idfDe1, _idfDe2;

    private Dictionary<string, float> Idf()
    {
        var ins = _insights;
        var docs = _docs;
        var atual = _idf;
        if (atual != null && ReferenceEquals(_idfDe1, ins) && ReferenceEquals(_idfDe2, docs)) return atual;

        lock (_trava)
        {
            if (_idf != null && ReferenceEquals(_idfDe1, ins) && ReferenceEquals(_idfDe2, docs)) return _idf;

            var df = new Dictionary<string, int>(StringComparer.Ordinal);
            int n = 0;
            foreach (var x in ins) { n++; foreach (var t in TokensDoItem(x)) df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1; }
            foreach (var x in docs) { n++; foreach (var t in TokensDoItem(x)) df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1; }

            var idf = new Dictionary<string, float>(df.Count, StringComparer.Ordinal);
            foreach (var kv in df)
                idf[kv.Key] = (float)Math.Log(1 + (n + 1.0) / (kv.Value + 0.5));

            _idf = idf;
            _idfDe1 = ins;
            _idfDe2 = docs;
            return idf;
        }
    }

    // Palavra que não aparece na base: peso médio (pode ser erro de digitação ou termo novo)
    private static float PesoIdf(Dictionary<string, float> idf, string t) => idf.TryGetValue(t, out var w) ? w : 1.5f;

    // termos exatos que NÃO podem ser perdidos: teclas, F-keys, códigos fiscais
    private static IEnumerable<string> TokensEspeciais(string s)
    {
        foreach (Match m in Regex.Matches(s ?? "", @"\b(ctrl\s*\+?\s*\w|f\d{1,2}|cfop\s*\d{3,4}|cst\s*\d+|csosn\s*\d+|ncm\s*\d+|rejei[cç][aã]o\s*\d+)\b",
                 RegexOptions.IgnoreCase))
            yield return m.Value.Trim();
    }
}
