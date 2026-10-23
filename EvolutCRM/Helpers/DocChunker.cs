using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EvolutCRM.Helpers;

/// <summary>
/// Corta uma documentação em pedaços para a busca da IA, respeitando a estrutura:
/// subtítulos (h1–h4), passos numerados em negrito ("1- Abra o Comercial…") e o título de cada bloco.
/// Cada pedaço começa com o caminho: "Cadastro de Produtos › 3- Aba Tributação".
/// </summary>
public static class DocChunker
{
    public sealed record Bloco(string Titulo, string ConteudoHtml);
    public sealed record Pedaco(string Caminho, string Texto);

    private const int Alvo = 1200;     // tamanho bom de pedaço
    private const int Minimo = 300;    // seção menor que isso junta com a próxima

    private static readonly HashSet<string> TitulosGenericos = new(StringComparer.OrdinalIgnoreCase)
    { "", "conteúdo", "conteudo", "texto", "bloco", "descrição", "descricao" };

    private static readonly Regex RxPasso = new(@"^\s*(passo\s*)?\d{1,2}\s*[-–—.):ºª°]\s*\S", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Pedaco> Cortar(string tituloDoc, string? descricao, IEnumerable<Bloco> blocos)
    {
        // 1) Seções: (subtítulo, linhas)
        var secoes = new List<(string Titulo, List<string> Linhas)>();
        if (!string.IsNullOrWhiteSpace(descricao))
            secoes.Add(("", new List<string> { Limpar(descricao) }));

        foreach (var b in blocos)
        {
            var tituloBloco = Limpar(b.Titulo);
            var atual = (Titulo: TitulosGenericos.Contains(tituloBloco) ? "" : tituloBloco, Linhas: new List<string>());
            secoes.Add(atual);

            foreach (var (texto, ehTitulo) in Linhas(b.ConteudoHtml))
            {
                if (ehTitulo)
                {
                    // subtítulo / passo: começa uma seção nova (o título do bloco vira o "pai" no caminho)
                    var prefixo = atual.Titulo.Length > 0 && !RxPasso.IsMatch(atual.Titulo) && tituloBloco.Length > 0 && !TitulosGenericos.Contains(tituloBloco)
                        ? tituloBloco + " › " : "";
                    atual = (prefixo + texto, new List<string>());
                    secoes.Add(atual);
                }
                else
                {
                    atual.Linhas.Add(texto);
                }
            }
        }

        // tira seções vazias que não têm título
        secoes = secoes.Where(s => s.Linhas.Count > 0 || s.Titulo.Length > 0).ToList();

        // 2) Junta seções pequenas e quebra as grandes
        var pedacos = new List<Pedaco>();
        string? caminhoAtual = null;
        var buffer = new StringBuilder();

        void Fechar()
        {
            var t = buffer.ToString().Trim();
            if (t.Length > 0)
                pedacos.Add(new Pedaco(MontarCaminho(tituloDoc, caminhoAtual), t));
            buffer.Clear();
            caminhoAtual = null;
        }

        foreach (var (titulo, linhas) in secoes)
        {
            var corpo = string.Join("\n", linhas).Trim();
            var textoSecao = titulo.Length > 0 ? (titulo + (corpo.Length > 0 ? "\n" + corpo : "")) : corpo;
            if (textoSecao.Length == 0) continue;

            // seção maior que o alvo: fecha o que tem e quebra ela em partes por frase
            if (textoSecao.Length > Alvo)
            {
                Fechar();
                var parte = new StringBuilder();
                string ultimaFrase = "";
                foreach (var frase in Frases(textoSecao))
                {
                    if (parte.Length + frase.Length > Alvo && parte.Length > 0)
                    {
                        pedacos.Add(new Pedaco(MontarCaminho(tituloDoc, titulo), parte.ToString().Trim()));
                        parte.Clear();
                        if (ultimaFrase.Length < 300) parte.Append(ultimaFrase).Append('\n'); // sobreposição de 1 frase
                    }
                    parte.Append(frase).Append('\n');
                    ultimaFrase = frase;
                }
                if (parte.ToString().Trim().Length > 0)
                    pedacos.Add(new Pedaco(MontarCaminho(tituloDoc, titulo), parte.ToString().Trim()));
                continue;
            }

            // cabe junto com o que já está no buffer?
            if (buffer.Length > 0 && buffer.Length + textoSecao.Length + 2 > Alvo)
                Fechar();

            if (buffer.Length == 0) caminhoAtual = titulo.Length > 0 ? titulo : null;
            else if (caminhoAtual == null && titulo.Length > 0) caminhoAtual = titulo;

            if (buffer.Length > 0) buffer.Append("\n\n");
            buffer.Append(textoSecao);

            // seção já tem tamanho bom sozinha: fecha
            if (buffer.Length >= Alvo - Minimo) Fechar();
        }
        Fechar();

        return pedacos;
    }

    private static string MontarCaminho(string tituloDoc, string? secao)
    {
        var doc = Limpar(tituloDoc);
        if (string.IsNullOrWhiteSpace(secao)) return doc;
        var s = secao.Length > 140 ? secao[..140] + "…" : secao;
        return $"{doc} › {s}";
    }

    /// <summary>HTML → linhas de texto, marcando quais são subtítulo/passo.</summary>
    private static IEnumerable<(string Texto, bool EhTitulo)> Linhas(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) yield break;
        var h = html;

        h = Regex.Replace(h, @"<!--.*?-->", "", RegexOptions.Singleline);
        h = Regex.Replace(h, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // subtítulos
        h = Regex.Replace(h, @"<h[1-4][^>]*>(.*?)</h[1-4]>", m => "\n\u0001" + m.Groups[1].Value + "\n",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // parágrafo que é só negrito (ex.: "<p><span><strong>1- Abra o Comercial…</strong></span></p>")
        h = Regex.Replace(h,
            @"<p[^>]*>\s*(?:<(?!/?(?:strong|b)\b)[^>]+>\s*)*<(strong|b)\b[^>]*>(.*?)</\1>\s*(?:</?(?!p\b)[^>]+>\s*)*</p>",
            m => "\n\u0002" + m.Groups[2].Value + "\n",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // quebras de linha
        h = Regex.Replace(h, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"</(p|div|li|tr|h[1-6]|ul|ol|table)>", "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<img[^>]*\balt\s*=\s*[""']([^""']+)[""'][^>]*>", " [imagem: $1] ", RegexOptions.IgnoreCase);

        // resto das tags
        h = Regex.Replace(h, @"<[^>]+>", " ");
        h = WebUtility.HtmlDecode(h);

        foreach (var bruta in h.Split('\n'))
        {
            var ehH = bruta.Contains('\u0001');
            var ehNegrito = bruta.Contains('\u0002');
            var linha = Limpar(bruta.Replace("\u0001", "").Replace("\u0002", ""));
            if (linha.Length == 0) continue;

            var ehTitulo = (ehH && linha.Length <= 160)
                        || (ehNegrito && linha.Length <= 200)
                        || (RxPasso.IsMatch(linha) && linha.Length <= 160);
            yield return (linha, ehTitulo);
        }
    }

    private static IEnumerable<string> Frases(string texto)
    {
        foreach (var linha in texto.Split('\n'))
        {
            var l = linha.Trim();
            if (l.Length == 0) continue;
            if (l.Length <= 400) { yield return l; continue; }
            foreach (var f in Regex.Split(l, @"(?<=[.!?;:])\s+"))
                if (f.Trim().Length > 0) yield return f.Trim();
        }
    }

    private static string Limpar(string? s) =>
        Regex.Replace(WebUtility.HtmlDecode(s ?? ""), @"[ \t ]+", " ").Trim();
}
