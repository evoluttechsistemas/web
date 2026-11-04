using System.Text;
using System.Text.Json;

namespace EvolutCRM.Services;

/// <summary>
/// Fica no meio de toda chamada HTTP do HELP. Quando a resposta vem da OpenAI (api.openai.com),
/// lê os tokens usados ("usage") e registra o custo estimado no IaUsoService. Não muda a resposta:
/// quem chamou recebe exatamente o mesmo conteúdo. Respostas em streaming (Assistente) são lidas
/// enquanto passam, e o gasto é registrado no fim.
/// </summary>
public sealed class UsoOpenAiHandler : DelegatingHandler
{
    private readonly IaUsoService _uso;

    public UsoOpenAiHandler(IaUsoService uso) => _uso = uso;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var resp = await base.SendAsync(request, ct);

        try
        {
            if (!string.Equals(request.RequestUri?.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
                || !resp.IsSuccessStatusCode || resp.Content == null)
                return resp;

            var endpoint = request.RequestUri!.AbsolutePath;
            var tipo = resp.Content.Headers.ContentType?.MediaType ?? "";

            if (tipo.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var original = resp.Content;
                var stream = await original.ReadAsStreamAsync(ct);
                var copia = new StreamCopiado(stream, bytes => _ = Task.Run(() => RegistrarStream(endpoint, bytes)));
                var nova = new StreamContent(copia);
                CopiarCabecalhos(original, nova);
                resp.Content = nova;
                return resp;
            }

            if (!tipo.Contains("json", StringComparison.OrdinalIgnoreCase))
                return resp;

            var antigo = resp.Content;
            var corpo = await antigo.ReadAsByteArrayAsync(ct);
            var conteudo = new ByteArrayContent(corpo);
            CopiarCabecalhos(antigo, conteudo);
            resp.Content = conteudo;

            _ = Task.Run(() => RegistrarJson(endpoint, corpo));
        }
        catch (Exception ex)
        {
            LogHelper.Log("[IA USO] " + ex.Message);   // nunca atrapalha a chamada
        }

        return resp;
    }

    private static void CopiarCabecalhos(HttpContent de, HttpContent para)
    {
        foreach (var h in de.Headers)
        {
            if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            para.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
    }

    private async Task RegistrarJson(string endpoint, byte[] corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            await RegistrarDe(endpoint, doc.RootElement);
        }
        catch { /* resposta sem usage (ex.: lista de modelos) */ }
    }

    private async Task RegistrarStream(string endpoint, byte[] corpo)
    {
        try
        {
            // Responses API em streaming: o evento "response.completed" traz o uso total
            var texto = Encoding.UTF8.GetString(corpo);
            foreach (var linha in texto.Split('\n'))
            {
                var l = linha.Trim();
                if (!l.StartsWith("data:")) continue;
                var json = l[5..].Trim();
                if (json.Length == 0 || json == "[DONE]") continue;
                if (!json.Contains("\"usage\"")) continue;

                using var doc = JsonDocument.Parse(json);
                var raiz = doc.RootElement;
                if (raiz.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.Object)
                    raiz = r;
                if (raiz.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    await RegistrarDe(endpoint, raiz);
                    return;
                }
            }
        }
        catch { }
    }

    private Task RegistrarDe(string endpoint, JsonElement raiz)
    {
        if (!raiz.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return Task.CompletedTask;

        var modelo = raiz.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
        if (modelo.Length == 0 && endpoint.Contains("transcriptions")) modelo = "whisper";

        long Num(JsonElement e, string nome) =>
            e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

        // Chat Completions / Embeddings: prompt_tokens, completion_tokens; Responses: input_tokens, output_tokens
        long entrada = Num(u, "prompt_tokens") + Num(u, "input_tokens");
        long saida = Num(u, "completion_tokens") + Num(u, "output_tokens");
        long cache = 0;
        if (u.TryGetProperty("prompt_tokens_details", out var pd) && pd.ValueKind == JsonValueKind.Object) cache += Num(pd, "cached_tokens");
        if (u.TryGetProperty("input_tokens_details", out var id) && id.ValueKind == JsonValueKind.Object) cache += Num(id, "cached_tokens");

        // Transcrição cobrada por tempo
        decimal segundos = 0;
        if (u.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number) segundos = s.GetDecimal();

        // Busca na web (Responses API)
        int buscas = 0;
        if (raiz.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.Array)
            foreach (var item in o.EnumerateArray())
                if (item.TryGetProperty("type", out var t) && t.GetString() == "web_search_call") buscas++;

        if (entrada == 0 && saida == 0 && segundos == 0 && buscas == 0)
            return Task.CompletedTask;

        return _uso.RegistrarAsync(modelo, endpoint, entrada, cache, saida, buscas, segundos);
    }

    /// <summary>Stream que entrega os bytes normalmente e guarda uma cópia; no fim chama o callback.</summary>
    private sealed class StreamCopiado : Stream
    {
        private readonly Stream _interno;
        private readonly Action<byte[]> _fim;
        private readonly MemoryStream _copia = new();
        private bool _terminou;
        private const int Maximo = 8 * 1024 * 1024;

        public StreamCopiado(Stream interno, Action<byte[]> fim) { _interno = interno; _fim = fim; }

        private void Guardar(ReadOnlySpan<byte> dados)
        {
            if (dados.Length == 0) { Terminar(); return; }
            if (_copia.Length < Maximo) _copia.Write(dados);
        }

        private void Terminar()
        {
            if (_terminou) return;
            _terminou = true;
            try { _fim(_copia.ToArray()); } catch { }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _interno.Read(buffer, offset, count);
            Guardar(buffer.AsSpan(offset, n));
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await _interno.ReadAsync(buffer, ct);
            Guardar(buffer.Span[..n]);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var n = await _interno.ReadAsync(buffer.AsMemory(offset, count), ct);
            Guardar(buffer.AsSpan(offset, n));
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Terminar(); _interno.Dispose(); _copia.Dispose(); }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
