using System.Globalization;
using EvolutDelivery.Models;

namespace EvolutDelivery.Services
{
    /// <summary>
    /// Monta o carrinho com dados oficiais do banco.
    /// Do navegador só aproveita código, quantidade e observação.
    /// Confere preço, fracionamento e ESTOQUE (ProdutoAFV.EstoqueAtual).
    /// Usado pelo Carrinho e pelo Checkout.
    /// </summary>
    public class CarrinhoService
    {
        public const int MaxObservacaoItem = 200;
        public const decimal QuantidadeMaximaItem = 999m;

        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly EmpresaService _empresaService;
        private readonly ProdutoService _produtoService;

        public CarrinhoService(EmpresaService empresaService, ProdutoService produtoService)
        {
            _empresaService = empresaService;
            _produtoService = produtoService;
        }

        public static decimal PrecoAtual(Produto p) =>
            p.PrecoPromocao > 0 && p.PrecoPromocao < p.PrecoVenda ? p.PrecoPromocao : p.PrecoVenda;

        public static decimal CalcularSubtotal(IEnumerable<ItemCarrinho> itens) =>
            Math.Round(itens.Sum(i => i.Preco * i.Quantidade), 2, MidpointRounding.AwayFromZero);

        public static bool PermiteDecimal(string? permiteDecimal) =>
            string.Equals(permiteDecimal, "S", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Quanto do estoque pode ser vendido: produto inteiro usa só a parte inteira
        /// (estoque 2,5 de um produto "UN" = 2). null = sem controle de estoque.
        /// </summary>
        public static decimal? EstoqueVendavel(decimal? estoque, string? permiteDecimal)
        {
            if (estoque == null)
                return null;

            var valor = PermiteDecimal(permiteDecimal)
                ? Math.Round(estoque.Value, 3, MidpointRounding.ToZero)
                : Math.Floor(estoque.Value);

            return valor > 0 ? valor : 0;
        }

        public static string FormatarQuantidade(decimal quantidade) =>
            quantidade == Math.Truncate(quantidade)
                ? quantidade.ToString("0", PtBr)
                : quantidade.ToString("0.###", PtBr);

        public async Task<(List<ItemCarrinho> Itens, List<string> Avisos)> ValidarAsync(string slug, List<ItemCarrinho> itensNavegador)
        {
            var avisos = new List<string>();
            var itens = new List<ItemCarrinho>();

            var empresa = await _empresaService.BuscarPorSlugAsync(slug);
            if (empresa == null || string.IsNullOrWhiteSpace(empresa.CNPJ))
                throw new Exception("Empresa não encontrada.");

            var itensComCodigo = (itensNavegador ?? new())
                .Where(i => i != null && i.Codigo > 0)
                .ToList();

            if (itensComCodigo.Count != (itensNavegador?.Count ?? 0))
                avisos.Add("Itens inválidos foram removidos do carrinho.");

            if (!itensComCodigo.Any())
                return (itens, avisos);

            var produtosBanco = await _produtoService.BuscarPorCodigosAsync(itensComCodigo.Select(i => i.Codigo), empresa.CNPJ);

            bool removeuIndisponivel = false;
            bool ajustouQuantidade = false;
            bool atualizouPreco = false;

            // Estoque que ainda sobra para cada produto (o mesmo produto pode estar em
            // mais de uma linha, por exemplo com observações diferentes)
            var estoqueRestante = new Dictionary<int, decimal?>();
            var semEstoque = new List<string>();
            var estoqueLimitado = new List<string>();

            foreach (var item in itensComCodigo)
            {
                if (!produtosBanco.TryGetValue(item.Codigo, out var prod))
                {
                    removeuIndisponivel = true;
                    continue;
                }

                decimal preco = PrecoAtual(prod);
                if (preco <= 0)
                {
                    removeuIndisponivel = true;
                    continue;
                }

                bool permiteDecimal = PermiteDecimal(prod.PermiteDecimal);

                decimal qtd = permiteDecimal
                    ? Math.Round(item.Quantidade, 3, MidpointRounding.AwayFromZero)
                    : Math.Truncate(item.Quantidade);

                if (qtd <= 0)
                {
                    ajustouQuantidade = true;
                    continue;
                }

                if (qtd > QuantidadeMaximaItem)
                {
                    qtd = QuantidadeMaximaItem;
                    ajustouQuantidade = true;
                }

                if (qtd != item.Quantidade)
                    ajustouQuantidade = true;

                // ===== ESTOQUE =====
                var estoqueTotal = EstoqueVendavel(prod.EstoqueAtual, prod.PermiteDecimal);
                if (!estoqueRestante.ContainsKey(prod.Codigo))
                    estoqueRestante[prod.Codigo] = estoqueTotal;

                var restante = estoqueRestante[prod.Codigo];
                if (restante != null)
                {
                    if (restante <= 0)
                    {
                        if (!semEstoque.Contains(prod.Nome)) semEstoque.Add(prod.Nome);
                        continue;
                    }

                    if (qtd > restante)
                    {
                        qtd = restante.Value;
                        var msg = $"{prod.Nome}: temos apenas {FormatarQuantidade(estoqueTotal!.Value)} em estoque";
                        if (!estoqueLimitado.Contains(msg)) estoqueLimitado.Add(msg);
                    }

                    estoqueRestante[prod.Codigo] = restante - qtd;
                }

                // Carrinhos antigos ainda podem trazer preço salvo: avisa se mudou
                if (item.Preco > 0 && preco != item.Preco)
                    atualizouPreco = true;

                var obs = (item.Observacao ?? "").Trim();
                if (obs.Length > MaxObservacaoItem)
                    obs = obs[..MaxObservacaoItem];

                itens.Add(new ItemCarrinho
                {
                    Codigo = prod.Codigo,
                    Nome = prod.Nome,
                    Quantidade = qtd,
                    Preco = preco,
                    PermiteDecimal = prod.PermiteDecimal ?? "N",
                    ImagemBase64 = "",
                    Observacao = obs,
                    EstoqueDisponivel = estoqueTotal
                });
            }

            foreach (var nome in semEstoque)
                avisos.Add($"{nome} não está mais disponível no estoque e foi removido do carrinho.");
            foreach (var msg in estoqueLimitado)
                avisos.Add($"{msg}. A quantidade foi ajustada.");

            if (removeuIndisponivel) avisos.Add("Alguns produtos não estão mais disponíveis e foram removidos.");
            if (ajustouQuantidade) avisos.Add("Algumas quantidades foram ajustadas.");
            if (atualizouPreco) avisos.Add("Os preços do carrinho foram atualizados.");

            return (itens, avisos);
        }
    }
}
