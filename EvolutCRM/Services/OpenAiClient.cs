using EvolutCRM.Models;
using System.Text;
using System.Text.Json;

namespace EvolutCRM.Services;

public sealed class OpenAiClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public OpenAiClient(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task<List<TicketInsight>> GenerateInsightsAsync(string ticketBaseText)
    {
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

        var system = @"
Você é um especialista em análise de tickets ERP/CRM.

Sua função:
- identificar problemas técnicos reais;
- separar assuntos diferentes;
- identificar causa provável;
- identificar solução aplicada;
- gerar conhecimento reutilizável para suporte futuro.

IMPORTANTE:
- Não invente solução.
- Só considere solução quando ela realmente estiver confirmada no histórico.
- Se o atendente apenas sugeriu algo, mas o cliente não confirmou, deixe solucao vazio.
- Tags devem ser curtas, técnicas e úteis para busca futura.
- Evite tags genéricas.
- Priorize módulo, erro, ação, funcionalidade, rotina, tela e integração.
- Responda SOMENTE com JSON válido, sem texto extra.
";
        var user = $@"
Analise o ticket abaixo e identifique os assuntos/problemas tratados.
Retorne um JSON como LISTA (array) e gere 1 item por assunto.

Regras:
- Não misture assuntos diferentes no mesmo item.
- Identifique a causa provável quando estiver clara.
- Marque solucaoConfirmada como true somente quando houver confirmação real no histórico.
- Use confianca de 0 a 100 conforme a segurança da análise.
- Tags devem ser minúsculas, sem acentos, sem espaços e úteis para busca futura.
- Nunca use tags genéricas como erro, sistema, problema, ajuda.
- topicoIndex começa em 1 (1,2,3...).
- Se não houver solução confirmada, deixe ""solucao"": """".
- Responda SOMENTE com JSON, sem ```.

categoria: escolha EXATAMENTE uma desta lista (não invente outra):
pdv, fiscal_nfe, fiscal_nfce, financeiro_pagar, financeiro_receber, estoque,
cadastro_produto, cadastro_cliente_fornecedor, balanca, impressao, etiqueta,
relatorio, acesso_remoto, hardware, configuracao, integracao, outro

gravidade: escolha uma: baixa, media, alta
confianca: número de 0 a 100 (quão seguro você está da análise)

REGRA DA SOLUÇÃO (importante):
- Só preencha ""solucao"" se houver o PASSO concreto no histórico (caminho de tela, tecla, código, ação).
- Se o problema foi resolvido por acesso remoto SEM o passo escrito, deixe ""solucao"": """".
- ""solucao"" nunca deve ser ""resolvido"", ""deu certo"" ou ""ajuste feito"".

Formato exato:

[
  {{{{
    ""topicoIndex"": 1,
    ""topicoTitulo"": """",
    ""perguntaCliente"": """",
    ""resumo"": """",
    ""categoria"": """",
    ""intencao"": """",
    ""gravidade"": """",
    ""tags"": ["""",""""],
    ""causaProvavel"": """",
    ""solucao"": """",
    ""confianca"": 0
  }}}}
]

TICKET:
{ticketBaseText}
";

        var body = new
        {
            model = model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        var http = _httpFactory.CreateClient("openai");

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode} - {raw}");

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new Exception("OpenAI retornou content vazio.");

        content = CleanJson(content);

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // ✅ Agora desserializa LISTA
            var insights = JsonSerializer.Deserialize<List<TicketInsight>>(content, opts);

            if (insights == null || insights.Count == 0)
                throw new Exception("Deserialize retornou lista vazia.");

            // garante topicoIndex
            for (int i = 0; i < insights.Count; i++)
            {
                if (insights[i].topicoIndex <= 0)
                    insights[i].topicoIndex = i + 1;
            }

            return insights;
        }
        catch (Exception ex)
        {
            throw new Exception("Falha ao desserializar JSON do modelo. Conteúdo recebido:\n" + content, ex);
        }
    }

    public async Task<string> GenerateWhatsAppReplyAsync(
    string historicoTicket,
    string ultimaMensagemCliente,
    string periodo)
    {
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

        var system = $@"
Você é um atendente humano da EvolutTech no WhatsApp.

Regras:
- Responda de forma curta, natural e educada.
- Nunca diga que é IA, robô ou assistente virtual.
- Não use markdown.
- Esta é a primeira mensagem do cliente neste atendimento.
- O período atual é: {periodo}.
- Se o período for MANHA, comece com Bom dia.
- Se o período for TARDE, comece com Boa tarde.
- Apenas cumprimente de forma natural.
- Não diga que recebeu a mensagem.
- Não diga ""em breve"", ""já vamos atender"" ou algo parecido.
- Se souber o nome do cliente, use o nome.
- Exemplo: ""Boa tarde, Gabriel! Tudo bem?""
- Exemplo: ""Bom dia! Tudo bem?""
- Não invente solução técnica se o cliente ainda não explicou o problema.
";

        var user = $@"
HISTÓRICO DO TICKET:
{historicoTicket}

ÚLTIMA MENSAGEM DO CLIENTE:
{ultimaMensagemCliente}

Gere somente a resposta que será enviada ao cliente.
";

        var body = new
        {
            model = model,
            temperature = 0.4,
            messages = new object[]
            {
            new { role = "system", content = system },
            new { role = "user", content = user }
            }
        };

        var http = _httpFactory.CreateClient("openai");

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode} - {raw}");

        using var doc = JsonDocument.Parse(raw);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content?.Trim() ?? "";
    }

    public async Task<string> GenerateWhatsAppGreetingOnlyAsync(string periodo)
    {
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

        var cumprimento = periodo == "MANHA"
            ? "Bom dia"
            : "Boa tarde";

        var system = $@"
Você é um atendente humano da EvolutTech no WhatsApp.

Sua função é SOMENTE cumprimentar o cliente.

IMPORTANTE:
O cliente já informou o que precisa.
Você NÃO deve perguntar:
- como pode ajudar
- em que pode ajudar
- o que aconteceu
- qual problema
- qualquer pergunta parecida

Você deve apenas responder com uma saudação curta e natural.

Regras obrigatórias:
- Use obrigatoriamente: {cumprimento}
- Responda apenas cumprimentando.
- Não responda dúvidas do cliente.
- Não fale sobre suporte, erro, sistema, vaga ou atendimento.
- Não diga que recebeu a mensagem.
- Não diga que vai verificar.
- Não use markdown.
- Use somente 1 frase curta.

Exemplos válidos:
- {cumprimento}! Tudo bem?
- {cumprimento}, tudo bem?
- {cumprimento}! Tudo certo?
- {cumprimento}! Como vai?
";

        var body = new
        {
            model = model,
            temperature = 0.2,
            messages = new object[]
            {
            new
            {
                role = "system",
                content = system
            },
            new
            {
                role = "user",
                content = "Gere somente uma saudação inicial."
            }
            }
        };

        var http = _httpFactory.CreateClient("openai");

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/chat/completions"
        );

        req.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json"
        );

        using var resp = await http.SendAsync(req);

        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode} - {raw}");

        using var doc = JsonDocument.Parse(raw);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var resposta = content?.Trim() ?? "";

        // Segurança extra
        if (string.IsNullOrWhiteSpace(resposta) ||
            resposta.Contains("como posso ajudar", StringComparison.OrdinalIgnoreCase) ||
            resposta.Contains("em que posso ajudar", StringComparison.OrdinalIgnoreCase) ||
            resposta.Contains("posso ajudar", StringComparison.OrdinalIgnoreCase))
        {
            resposta = $"{cumprimento}! Tudo bem?";
        }

        return resposta;
    }

    private static string CleanJson(string s)
    {
        s = s.Trim();

        if (s.StartsWith("```"))
        {
            s = s.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("```", "")
                 .Trim();
        }

        // ✅ Agora tenta cortar ARRAY primeiro
        var firstArr = s.IndexOf('[');
        var lastArr = s.LastIndexOf(']');
        if (firstArr >= 0 && lastArr > firstArr)
            return s.Substring(firstArr, lastArr - firstArr + 1).Trim();

        // fallback: objeto único (caso o modelo erre e devolva {})
        var firstObj = s.IndexOf('{');
        var lastObj = s.LastIndexOf('}');
        if (firstObj >= 0 && lastObj > firstObj)
            return s.Substring(firstObj, lastObj - firstObj + 1).Trim();

        return s.Trim();
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return Array.Empty<float>();
        if (text.Length > 8000) text = text.Substring(0, 8000); // corte de segurança

        var body = new { model = "text-embedding-3-small", input = text };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI embeddings erro: {(int)resp.StatusCode} - {raw}");

        using var doc = JsonDocument.Parse(raw);
        var arr = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var vec = new float[arr.GetArrayLength()];
        int i = 0;
        foreach (var v in arr.EnumerateArray()) vec[i++] = v.GetSingle();
        return vec;
    }

    public async Task<(string assunto, string sentimento, string emoji)> AnalisarContextoClienteAsync(
    string mensagens)
    {
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

        var system = @"
Você é um assistente de análise de atendimento.
Analise as mensagens do cliente e retorne SOMENTE um JSON com 3 campos:
- assunto: resumo do problema em até 80 caracteres, direto ao ponto
- sentimento: uma das opções: positivo, neutro, negativo, frustrado, urgente
- emoji: um emoji correspondente ao sentimento (😊 positivo, 😐 neutro, 😠 negativo, 😤 frustrado, 🚨 urgente)

Responda SOMENTE com JSON válido, sem texto extra, sem markdown.
Exemplo: {""assunto"":""Erro ao emitir NF-e"",""sentimento"":""frustrado"",""emoji"":""😤""}
";

        var body = new
        {
            model = model,
            temperature = 0.2,
            messages = new object[]
            {
            new { role = "system", content = system },
            new { role = "user", content = mensagens }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return ("[WhatsApp]", "neutro", "😐");

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim() ?? "";

        content = CleanJson(content);

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resultado = JsonSerializer.Deserialize<JsonElement>(content, opts);
            var assunto = resultado.GetProperty("assunto").GetString() ?? "[WhatsApp]";
            var sentimento = resultado.GetProperty("sentimento").GetString() ?? "neutro";
            var emoji = resultado.GetProperty("emoji").GetString() ?? "😐";
            return (assunto, sentimento, emoji);
        }
        catch
        {
            return ("[WhatsApp]", "neutro", "😐");
        }
    }
}