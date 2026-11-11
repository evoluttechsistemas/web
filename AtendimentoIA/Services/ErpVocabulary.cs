using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

/// <summary>
/// Vocabulário do ERP para a busca: raiz das palavras ("emitir" acha "emissão") e sinônimos
/// ("NF-e" acha "nota fiscal"). Os grupos padrão podem ser completados no painel (Vocabulário),
/// gravados em App_Data/synonyms.json — uma linha por grupo, termos separados por vírgula.
/// </summary>
public static class ErpVocabulary
{
    public const string File = "synonyms.json";

    // Grupos de termos que o cliente e a documentação usam para a mesma coisa.
    public static readonly string[] DefaultGroups =
    [
        "nfe, nota fiscal, nota eletronica, danfe",
        "nfce, cupom fiscal, cupom, nota de consumidor",
        "sat, mfe, cfe",
        "boleto, titulo, cobranca, contas a receber",
        "contas a pagar, pagamento, duplicata",
        "pdv, frente de caixa, caixa, checkout",
        "sangria, retirada",
        "suprimento, reforco, troco inicial",
        "fechamento de caixa, fechar caixa, encerrar caixa",
        "abertura de caixa, abrir caixa",
        "cancelamento, cancelar, estorno, estornar",
        "inutilizacao, inutilizar",
        "carta de correcao, cce",
        "certificado, certificado digital, a1, a3",
        "rejeicao, rejeitada, rejeitado, recusada",
        "sefaz, receita, contingencia",
        "produto, mercadoria, item",
        "estoque, inventario, saldo",
        "fornecedor, compra, entrada de nota, nota de entrada, xml de entrada",
        "cliente, consumidor, comprador",
        "vendedor, comissao",
        "preco, valor, tabela de preco",
        "desconto, abatimento",
        "acrescimo, juros, multa",
        "balanca, etiqueta, codigo de balanca",
        "impressora, impressao, imprimir, cupom nao imprime",
        "tef, cartao, maquininha, pinpad",
        "pix, qrcode, qr code",
        "backup, copia de seguranca",
        "senha, login, acesso, usuario",
        "relatorio, consulta, listagem",
        "cfop, natureza de operacao",
        "ncm, cest, tributacao, imposto, icms, pis, cofins",
        "devolucao, troca, devolver",
        "orcamento, pedido, pre venda, prevenda",
        "emitir, emissao, emitida, emitido, transmitir",
        "cadastrar, cadastro, incluir, inclusao, cadastramento",
        "alterar, alteracao, editar, mudar",
        "excluir, exclusao, apagar, deletar",
        "travado, travou, lento, congelou, nao responde",
        "erro, falha, problema, mensagem de erro"
    ];

    public static List<string[]> Parse(IEnumerable<string>? lines) =>
        (lines ?? []).Select(l => l.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(KnowledgeSearch.Normalize).Select(t => Regex.Replace(t, @"\s+", " ").Trim())
                .Where(t => t.Length >= 2).Distinct().ToArray())
            .Where(g => g.Length >= 2).ToList();

    public static async Task<List<string[]>> LoadAsync(PanelStore store)
    {
        List<string>? custom = null;
        try { custom = await store.ReadAsync<List<string>>(File); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return Parse(DefaultGroups.Concat(custom ?? []));
    }

    // Sufixos comuns do português: "emissao"→"emis", "cancelamento"→"cancel", "cadastrar"→"cadastr".
    private static readonly string[] Suffixes =
        ["amentos", "imentos", "amento", "imento", "acoes", "icoes", "coes", "cao", "sao", "soes", "ando", "endo", "indo",
         "ados", "adas", "idos", "idas", "ado", "ada", "ido", "ida", "aram", "eram", "iram", "ar", "er", "ir", "ou", "eu", "iu"];

    /// <summary>Raiz conservadora (palavras com 6+ letras, raiz com 4+): "emitir"→"emit", "cadastrar"→"cadastr".</summary>
    public static string Root(string term)
    {
        if (term.Length < 6 || term.Any(char.IsDigit)) return term;
        foreach (var suffix in Suffixes)
            if (term.EndsWith(suffix, StringComparison.Ordinal) && term.Length - suffix.Length >= 4)
                return term[..^suffix.Length];
        return term;
    }

    /// <summary>
    /// Grupos de busca: cada termo da pergunta vira um grupo com o próprio termo, a raiz e os sinônimos.
    /// Um grupo "bate" quando qualquer alternativa aparece no texto.
    /// </summary>
    public static List<string[]> Expand(IReadOnlyList<string> terms, IReadOnlyList<string[]> groups, int maxAlternatives = 5)
    {
        var result = new List<string[]>();
        foreach (var term in terms)
        {
            var alternatives = new List<string> { term };
            var root = Root(term);
            if (root != term) alternatives.Add(root);
            foreach (var group in groups)
                if (group.Any(g => g == term || (!g.Contains(' ') && (Root(g) == root && root.Length >= 5))))
                    alternatives.AddRange(group);
            result.Add(alternatives.Distinct().Take(maxAlternatives).ToArray());
        }
        return result;
    }

    /// <summary>Termos multi-palavra da pergunta que são sinônimos conhecidos ("nota fiscal", "fechar caixa").</summary>
    public static IEnumerable<string> PhrasesIn(string question, IReadOnlyList<string[]> groups)
    {
        var normalized = " " + Regex.Replace(KnowledgeSearch.Normalize(question), @"[^a-z0-9]+", " ") + " ";
        return groups.SelectMany(g => g).Where(t => t.Contains(' ') && normalized.Contains(" " + t + " ", StringComparison.Ordinal)).Distinct();
    }
}
