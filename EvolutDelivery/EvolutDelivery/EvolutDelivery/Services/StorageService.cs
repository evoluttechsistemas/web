using Microsoft.JSInterop;
using System.Text.Json;
using EvolutDelivery.Models;

namespace EvolutDelivery.Services
{
    public class StorageService
    {
        private readonly IJSRuntime js;
        private const string CARRINHO_KEY = "carrinho";

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

        // ===== MÉTODOS ESPECÍFICOS DO CARRINHO =====
        
        public async Task<List<ItemCarrinho>> ObterCarrinhoAsync()
        {
            var carrinho = await GetAsync<List<ItemCarrinho>>(CARRINHO_KEY);
            return carrinho ?? new List<ItemCarrinho>();
        }

        public async Task SalvarCarrinhoAsync(List<ItemCarrinho> itens)
        {
            await SaveAsync(CARRINHO_KEY, itens);
        }

        public async Task LimparCarrinhoAsync()
        {
            await RemoveAsync(CARRINHO_KEY);
        }
    }
}
