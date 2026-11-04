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

    public async Task<List<TicketInsight>> GenerateInsightsAsync(string ticketBaseText, List<string>? imagens = null)
    {
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";
        imagens ??= new List<string>();

        var system = @"
Você é um especialista em análise de tickets de suporte do sistema Evolut (ERP/CRM: PDV, fiscal, financeiro, estoque, etiquetas, balança, impressoras).

Sua função:
- identificar problemas técnicos reais;
- separar assuntos diferentes;
- identificar causa provável;
- identificar a solução aplicada;
- gerar conhecimento reutilizável para suporte futuro.

COMO LER O TICKET:
- Cliente = quem pediu ajuda. Atendente = suporte EvolutTech. Nota interna = anotação do suporte que o cliente não vê
  (muitas vezes é ali que está o que foi feito de verdade).
- [imagem N anexada] = print enviado no ticket; a imagem N vem junto. Use o que aparece nela
  (mensagem de erro, código de rejeição, tela, campo) no resumo, na causa e na solução.
- [áudio] = transcrição de um áudio.
- [texto sugerido pela IA] = mensagem do atendente que foi escrita pela IA do HELP e só revisada.
  Ela NÃO é prova de solução: só use o que ela diz como solução se o cliente confirmar DEPOIS que funcionou.
  Nunca marque solucaoConfirmada = true por causa dela.

IMPORTANTE:
- Não invente solução.
- Só considere solução quando ela realmente estiver no histórico (mensagem do atendente, nota interna ou print).
- Se o atendente apenas sugeriu algo e não há confirmação de que resolveu, preencha a solução mas marque solucaoConfirmada = false.
- Tags devem ser curtas, técnicas e úteis para busca futura.
- Evite tags genéricas.
- Priorize módulo, erro, ação, funcionalidade, rotina, tela e integração.
- Responda SOMENTE com JSON válido, sem texto extra.
";
        var user = $@"
Analise o ticket abaixo e identifique os assuntos/problemas tratados.
Gere 1 item por assunto, dentro da lista ""itens"".

Regras:
- Não misture assuntos diferentes no mesmo item.
- Identifique a causa provável quando estiver clara.
- solucaoConfirmada = true SOMENTE quando o próprio cliente confirmou na conversa que resolveu
  (ex.: ""deu certo"", ""funcionou"", ""agora foi"") depois da solução. Atendente dizer que resolveu,
  ticket finalizado ou cliente que sumiu NÃO é confirmação: nesses casos solucaoConfirmada = false
  (o caso vai para a revisão de um atendente antes de a IA usar).
- Use confianca de 0 a 100 conforme a segurança da análise.
- Tags devem ser minúsculas, sem acentos, sem espaços e úteis para busca futura.
- Nunca use tags genéricas como erro, sistema, problema, ajuda.
- topicoIndex começa em 1 (1,2,3...).
- Se não houver solução no histórico, deixe ""solucao"": """" e ""solucaoConfirmada"": false.

categoria: escolha EXATAMENTE uma desta lista (não invente outra):
pdv, fiscal_nfe, fiscal_nfce, financeiro_pagar, financeiro_receber, estoque,
cadastro_produto, cadastro_cliente_fornecedor, balanca, impressao, etiqueta,
relatorio, acesso_remoto, hardware, configuracao, integracao, outro

gravidade: escolha uma: baixa, media, alta
confianca: número de 0 a 100 (quão seguro você está da análise)

REGRA DA SOLUÇÃO (importante):
- Só preencha ""solucao"" se houver o PASSO concreto no histórico (caminho de tela, tecla, código, ação, configuração alterada).
- Se o problema foi resolvido por acesso remoto SEM o passo escrito, deixe ""solucao"": """".
- ""solucao"" nunca deve ser ""resolvido"", ""deu certo"" ou ""ajuste feito"".

Formato exato:

{{
  ""itens"": [
    {{
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
      ""solucaoConfirmada"": false,
      ""confianca"": 0
    }}
  ]
}}

TICKET:
{ticketBaseText}
";

        // Texto + prints do ticket
        object conteudoUsuario = user;
        if (imagens.Count > 0)
        {
            var partes = new List<object> { new { type = "text", text = user } };
            foreach (var img in imagens)
                partes.Add(new { type = "image_url", image_url = new { url = img, detail = "high" } });
            conteudoUsuario = partes;
        }

        var body = new
        {
            model = model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = conteudoUsuario }
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

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Formato novo: { "itens": [ ... ] }. Aceita também a lista pura (formato antigo).
            List<TicketInsight>? insights;
            using (var j = JsonDocument.Parse(content.Trim()))
            {
                var raiz = j.RootElement;
                if (raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("itens", out var itens))
                    insights = JsonSerializer.Deserialize<List<TicketInsight>>(itens.GetRawText(), opts);
                else if (raiz.ValueKind == JsonValueKind.Array)
                    insights = JsonSerializer.Deserialize<List<TicketInsight>>(raiz.GetRawText(), opts);
                else
                    insights = new List<TicketInsight> { JsonSerializer.Deserialize<TicketInsight>(raiz.GetRawText(), opts)! };
            }

            // Lista vazia é resposta válida: a conversa não tem assunto de suporte para registrar
            // (só saudação, cliente sumiu, etc.). Quem chama decide o que fazer.
            if (insights == null || insights.Count == 0)
                return new List<TicketInsight>();

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

    /// <summary>Embeddings de vários textos numa chamada só (a API aceita uma lista). Mesma ordem da entrada.</summary>
    public async Task<List<float[]>> GetEmbeddingsAsync(IReadOnlyList<string> textos)
    {
        var resultado = new List<float[]>();
        for (int ini = 0; ini < textos.Count; ini += 200)
        {
            var lote = textos.Skip(ini).Take(200)
                .Select(t => { t = (t ?? "").Trim(); if (t.Length == 0) t = "-"; return t.Length > 8000 ? t[..8000] : t; })
                .ToList();

            var body = new { model = "text-embedding-3-small", input = lote };
            var http = _httpFactory.CreateClient("openai");
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"OpenAI embeddings erro: {(int)resp.StatusCode} - {raw}");

            using var doc = JsonDocument.Parse(raw);
            var porIndice = new float[lote.Count][];
            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var idx = item.GetProperty("index").GetInt32();
                var arr = item.GetProperty("embedding");
                var vec = new float[arr.GetArrayLength()];
                int i = 0;
                foreach (var v in arr.EnumerateArray()) vec[i++] = v.GetSingle();
                if (idx >= 0 && idx < porIndice.Length) porIndice[idx] = vec;
            }
            resultado.AddRange(porIndice.Select(v => v ?? Array.Empty<float>()));
        }
        return resultado;
    }

    /// <summary>
    /// Dá um título curto de assunto para cada grupo de perguntas parecidas (tela Lacunas da IA).
    /// Devolve a mesma quantidade de grupos; título vazio quando a IA não respondeu aquele.
    /// </summary>
    public async Task<List<string>> TitularGruposAsync(IReadOnlyList<IReadOnlyList<string>> grupos)
    {
        var titulos = Enumerable.Repeat("", grupos.Count).ToList();
        if (grupos.Count == 0) return titulos;

        var lista = new StringBuilder();
        for (int g = 0; g < grupos.Count; g++)
        {
            lista.AppendLine($"[G{g + 1}]");
            foreach (var p in grupos[g].Take(5))
                lista.AppendLine("- " + (p.Length > 220 ? p[..220] : p).Replace("\n", " "));
            lista.AppendLine();
        }

        var body = new
        {
            model = _config["OpenAI:Model"] ?? "gpt-4.1-mini",
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = @"Você organiza dúvidas de clientes do suporte do sistema Evolut (ERP para varejo: PDV, fiscal NF-e/NFC-e, financeiro, estoque, etiquetas, balança, impressoras).
Cada grupo [G1], [G2]... tem perguntas parecidas que a base de conhecimento NÃO conseguiu responder.
Para cada grupo, escreva o ASSUNTO em até 8 palavras, como título de uma documentação que resolveria essas dúvidas
(ex.: ""Cancelar NFC-e fora do prazo"", ""Configurar balança Toledo no PDV"").
Português, sem ponto final, sem nome de cliente.
Responda só JSON: { ""titulos"": { ""G1"": ""..."", ""G2"": ""..."" } }" },
                new { role = "user", content = lista.ToString() }
            }
        };

        try
        {
            var http = _httpFactory.CreateClient("openai");
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return titulos;

            using var doc = JsonDocument.Parse(raw);
            var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            using var j = JsonDocument.Parse(conteudo);
            var raiz = j.RootElement.TryGetProperty("titulos", out var pt) ? pt : j.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return titulos;

            foreach (var p in raiz.EnumerateObject())
            {
                var chave = p.Name.Trim('[', ']', ' ').TrimStart('G', 'g');
                if (int.TryParse(chave, out var n) && n >= 1 && n <= grupos.Count && p.Value.ValueKind == JsonValueKind.String)
                    titulos[n - 1] = (p.Value.GetString() ?? "").Trim();
            }
        }
        catch
        {
            // sem título: a tela usa a pergunta mais representativa do grupo
        }
        return titulos;
    }

    /// <summary>
    /// Escreve um rascunho de documentação (título + passo a passo) juntando casos resolvidos do mesmo problema.
    /// Só usa o que está nos casos; quando as soluções divergem, aponta a divergência em vez de escolher.
    /// </summary>
    public async Task<string> RascunharDocumentacaoAsync(IReadOnlyList<(string Resumo, string Solucao)> casos)
    {
        var lista = new StringBuilder();
        for (int i = 0; i < casos.Count && i < 12; i++)
        {
            lista.AppendLine($"[Caso {i + 1}]");
            lista.AppendLine("Problema: " + casos[i].Resumo);
            lista.AppendLine("Solução: " + casos[i].Solucao);
            lista.AppendLine();
        }

        var body = new
        {
            model = _config["OpenAI:ModeloResposta"] ?? "gpt-4.1",
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = @"Você escreve documentação de suporte do sistema Evolut (ERP para varejo) para clientes e atendentes.
Junte os casos resolvidos abaixo, que tratam do mesmo problema, em UMA documentação curta:
- Primeira linha: título claro (o problema ou a tarefa).
- Depois: ""Quando acontece"" (1-2 linhas) e ""Como resolver"" em passos numerados.
- Use SOMENTE caminhos de tela, teclas, códigos e configurações que aparecem nos casos. Não invente.
- Se os casos trazem soluções diferentes para o mesmo problema, não escolha: escreva no fim
  ""ATENÇÃO: os casos divergem"" e liste as alternativas, para o time conferir.
- Português do Brasil, sem markdown pesado (sem tabelas), sem citar números de caso ou de ticket." },
                new { role = "user", content = lista.ToString() }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        return (doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "").Trim();
    }

    public sealed record DocumentacaoGerada(bool MesmoAssunto, string Titulo, string Resumo, string Html);

    /// <summary>
    /// Documentação interna detalhada de um caso de atendimento (modelo intermediário, gpt-4.1).
    /// Com <paramref name="htmlExistente"/>: primeiro diz se o caso é do MESMO assunto da documentação; se for,
    /// devolve a documentação inteira atualizada com o caso novo junto (sem repetir o que já está lá).
    /// </summary>
    public async Task<DocumentacaoGerada> DocumentarCasoAsync(string conversa, string caso, string? tituloExistente, string? htmlExistente)
    {
        var juntando = !string.IsNullOrWhiteSpace(htmlExistente);
        conversa = (conversa ?? "").Trim();
        if (conversa.Length > 12000) conversa = conversa[^12000..];

        var system = @"Você escreve a DOCUMENTAÇÃO INTERNA da equipe de suporte da EvolutTech, sobre o sistema Evolut
(ERP para varejo: PDV, vendas, estoque, financeiro, notas fiscais etc.). Quem lê é o atendente, para resolver
casos parecidos no futuro, e a IA do suporte, para responder clientes.

Escreva uma documentação BEM DETALHADA, em HTML simples, com estas seções (use só as que tiverem conteúdo):
<h2>Problema</h2> o que o cliente queria ou o erro que aparecia, com as palavras que o cliente costuma usar;
<h2>Quando acontece</h2> em que tela, situação ou configuração;
<h2>Causa</h2> o motivo, se a conversa deixa claro;
<h2>Como resolver</h2> passo a passo numerado (<ol><li>), com caminhos de tela, menus, teclas de atalho,
campos e valores exatamente como aparecem na conversa;
<h2>Como confirmar que resolveu</h2> o que o cliente deve ver depois;
<h2>Observações</h2> cuidados, variações, o que NÃO resolveu e por quê, quando chamar o desenvolvimento.

Regras:
- Use SOMENTE o que está na conversa e no resumo do caso. Não invente telas, teclas, menus ou configurações.
- Se algo não ficou claro na conversa, diga que não ficou claro em vez de supor.
- Não cite nomes de clientes, empresas, telefones, CPF/CNPJ, números de ticket nem datas do atendimento.
- Mensagens marcadas [texto sugerido pela IA] são respostas sugeridas pela IA: não são prova de que funcionou.
- HTML permitido: h2, h3, p, ol, ul, li, strong, em, code, blockquote, br. Sem estilos, sem tabelas, sem markdown.
- Português do Brasil, linguagem clara e direta.";

        if (juntando)
            system += @"

Você também recebe uma DOCUMENTAÇÃO QUE JÁ EXISTE. Primeiro decida se o caso novo é do MESMO assunto dela
(mesmo problema ou mesma tarefa no sistema; detalhes diferentes do mesmo problema contam como mesmo assunto).
- Se NÃO for o mesmo assunto: responda mesmoAssunto = false e deixe os outros campos vazios.
- Se for: devolva a documentação INTEIRA atualizada, juntando o caso novo. Mantenha o que já existe e está certo,
  acrescente passos, variações, causas e observações novas, sem repetir. Se o caso novo traz uma solução diferente
  da documentada, mantenha as duas e explique em que situação cada uma vale; se não der para saber, escreva
  ""ATENÇÃO: há soluções diferentes para este problema"" e liste as alternativas para a equipe conferir.";

        system += @"

Responda SOMENTE com JSON:
{""mesmoAssunto"": true/false, ""titulo"": ""título claro do problema ou tarefa (até 120 caracteres)"",
 ""resumo"": ""uma ou duas frases dizendo do que trata (até 250 caracteres)"", ""html"": ""a documentação em HTML""}";

        var user = new StringBuilder();
        if (juntando)
        {
            user.AppendLine("=== DOCUMENTAÇÃO QUE JÁ EXISTE ===");
            user.AppendLine("Título: " + tituloExistente);
            user.AppendLine(htmlExistente!.Length > 15000 ? htmlExistente[..15000] : htmlExistente);
            user.AppendLine();
        }
        user.AppendLine("=== RESUMO DO CASO NOVO ===");
        user.AppendLine(caso);
        user.AppendLine();
        user.AppendLine("=== CONVERSA DO ATENDIMENTO ===");
        user.AppendLine(conversa);

        var body = new
        {
            model = _config["OpenAI:ModeloResposta"] ?? "gpt-4.1",
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user.ToString() }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        using var json = JsonDocument.Parse(CleanJson(conteudo));
        var r = json.RootElement;

        string Texto(string nome) =>
            r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        var mesmo = !juntando || (r.TryGetProperty("mesmoAssunto", out var m) && m.ValueKind == JsonValueKind.True);
        var html = Texto("html");
        if (mesmo && html.Length == 0)
            throw new Exception("a IA não devolveu o conteúdo da documentação");

        return new DocumentacaoGerada(mesmo, Texto("titulo"), Texto("resumo"), html);
    }

    /// <summary>
    /// Resumo curto da conversa para o atendente que está recebendo o ticket (modelo barato, OpenAI:Model).
    /// Devolve 3 linhas: Problema / Já feito / Próximo passo. Vazio se não conseguir.
    /// </summary>
    public async Task<string> ResumirParaTransferenciaAsync(string conversa)
    {
        conversa = (conversa ?? "").Trim();
        if (conversa.Length == 0) return "";
        if (conversa.Length > 10000) conversa = conversa[^10000..];

        var system = @"Você ajuda a equipe de suporte da EvolutTech (sistema Evolut, ERP para varejo).
Um ticket está sendo transferido para outro atendente. Escreva um resumo MUITO curto para quem vai assumir,
em exatamente 3 linhas, neste formato:
Problema: <o que o cliente precisa, em uma frase>
Já feito: <o que já foi tentado ou respondido; ""nada ainda"" se ninguém respondeu>
Próximo passo: <o que falta fazer ou o que o cliente está esperando>

Regras: use só o que está na conversa; não invente; cada linha com no máximo 160 caracteres;
sem saudações, sem markdown, sem nomes de telefone/CPF. Português do Brasil.";

        var body = new
        {
            model = _config["OpenAI:Model"] ?? "gpt-4.1-mini",
            temperature = 0.2,
            max_tokens = 220,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = conversa }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        var texto = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        texto = texto.Replace("**", "").Trim();
        return texto.Length > 700 ? texto[..700] : texto;
    }

    /// <summary>
    /// Duas documentações automáticas parecidas: diz se são do MESMO assunto e, se forem, devolve uma só,
    /// juntando o conteúdo das duas sem repetir (gpt-4.1).
    /// </summary>
    public async Task<DocumentacaoGerada> JuntarDocumentacoesAsync(string tituloA, string htmlA, string tituloB, string htmlB)
    {
        static string Cortar(string? h) => (h ?? "").Length > 15000 ? h![..15000] : (h ?? "");

        var system = @"Você cuida da DOCUMENTAÇÃO INTERNA da equipe de suporte da EvolutTech, sobre o sistema Evolut
(ERP para varejo: PDV, vendas, estoque, financeiro, notas fiscais etc.).
Você recebe DUAS documentações que podem tratar do mesmo assunto.

Primeiro decida se são do MESMO assunto: o mesmo problema ou a mesma tarefa no sistema, mesmo que escritas de
jeito diferente ou com detalhes diferentes (ex.: ""Alteração do servidor do Evolut para um novo computador"" e
""Alteração do servidor principal do sistema Evolut"" são o mesmo assunto). Assuntos apenas relacionados
(ex.: instalar o servidor x fazer backup do servidor) NÃO são o mesmo assunto.
- Se NÃO forem: responda mesmoAssunto = false e deixe os outros campos vazios.
- Se forem: devolva UMA documentação inteira juntando as duas. Mantenha tudo que estiver certo nas duas,
  sem repetir. Se trazem soluções diferentes, mantenha as duas e explique em que situação cada uma vale; se não
  der para saber, escreva ""ATENÇÃO: há soluções diferentes para este problema"" e liste as alternativas.
  Use as seções <h2>Problema</h2>, <h2>Quando acontece</h2>, <h2>Causa</h2>, <h2>Como resolver</h2>,
  <h2>Como confirmar que resolveu</h2>, <h2>Observações</h2> (só as que tiverem conteúdo).

Regras:
- Use SOMENTE o que está nas duas documentações. Não invente telas, teclas, menus ou configurações.
- HTML permitido: h2, h3, p, ol, ul, li, strong, em, code, blockquote, br. Sem estilos, sem tabelas, sem markdown.
- Português do Brasil, linguagem clara e direta.

Responda SOMENTE com JSON:
{""mesmoAssunto"": true/false, ""titulo"": ""título claro do problema ou tarefa (até 120 caracteres)"",
 ""resumo"": ""uma ou duas frases dizendo do que trata (até 250 caracteres)"", ""html"": ""a documentação em HTML""}";

        var user = new StringBuilder();
        user.AppendLine("=== DOCUMENTAÇÃO 1 ===");
        user.AppendLine("Título: " + tituloA);
        user.AppendLine(Cortar(htmlA));
        user.AppendLine();
        user.AppendLine("=== DOCUMENTAÇÃO 2 ===");
        user.AppendLine("Título: " + tituloB);
        user.AppendLine(Cortar(htmlB));

        var body = new
        {
            model = _config["OpenAI:ModeloResposta"] ?? "gpt-4.1",
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user.ToString() }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI erro: {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        using var json = JsonDocument.Parse(CleanJson(conteudo));
        var r = json.RootElement;

        string Texto(string nome) =>
            r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        var mesmo = r.TryGetProperty("mesmoAssunto", out var m) && m.ValueKind == JsonValueKind.True;
        var html = Texto("html");
        if (mesmo && html.Length == 0)
            throw new Exception("a IA não devolveu o conteúdo da documentação juntada");

        return new DocumentacaoGerada(mesmo, Texto("titulo"), Texto("resumo"), html);
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