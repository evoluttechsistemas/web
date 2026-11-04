using Microsoft.JSInterop;
using System.Text.Json;
using EvolutDelivery.Models;

namespace EvolutDelivery.Services
{
    public class StorageService
    {
        private readonly IJSRuntime js;

        public StorageService(IJSRuntime js)
        {
            this.js = js;
        }

        public async Task SaveAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            await js.InvokeVoidAsync("localStorage.setItem", key, json);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var json = await js.InvokeAsync<string>("localStorage.getItem", key);
            return string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json);
        }

        public async Task RemoveAsync(string key)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }

        // ===== CARRINHO (um por loja) =====
        // No navegador ficam SÓ código, quantidade e observação.
        // Nome, preço e fracionamento são sempre buscados no banco (CarrinhoService).

        private static string ChaveCarrinho(string slug) => $"carrinho_{slug}";

        private sealed class ItemCarrinhoSalvo
        {
            public int Codigo { get; set; }
            public decimal Quantidade { get; set; }
            public string? Observacao { get; set; }
        }

        public async Task<List<ItemCarrinho>> ObterCarrinhoAsync(string slug)
        {
            try
            {
                // Lê também carrinhos antigos (que tinham Nome/Preco): os campos extras são ignorados
                var salvos = await GetAsync<List<ItemCarrinhoSalvo>>(ChaveCarrinho(slug));

                return (salvos ?? new())
                    .Where(i => i != null)
                    .Select(i => new ItemCarrinho
                    {
                        Codigo = i.Codigo,
                        Quantidade = i.Quantidade,
                        Observacao = i.Observacao ?? "",
                        PermiteDecimal = "N",
                        ImagemBase64 = ""
                    })
                    .ToList();
            }
            catch (JsonException)
            {
                // JSON corrompido/alterado no navegador
                return new List<ItemCarrinho>();
            }
        }

        public async Task SalvarCarrinhoAsync(string slug, IEnumerable<ItemCarrinho> itens)
        {
            var salvos = (itens ?? Enumerable.Empty<ItemCarrinho>())
                .Select(i => new ItemCarrinhoSalvo
                {
                    Codigo = i.Codigo,
                    Quantidade = i.Quantidade,
                    Observacao = i.Observacao ?? ""
                })
                .ToList();

            await SaveAsync(ChaveCarrinho(slug), salvos);
        }

        public async Task LimparCarrinhoAsync(string slug)
        {
            await RemoveAsync(ChaveCarrinho(slug));
        }
    }
}
