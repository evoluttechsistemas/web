using HelpMobile.Models;
using Microsoft.JSInterop;

namespace HelpMobile.Services
{
    /// <summary>Ativa/desativa as notificações do celular e mantém a inscrição registrada no HELP.</summary>
    public class PushService
    {
        private readonly IJSRuntime _js;
        private readonly ApiClient _api;

        public PushService(IJSRuntime js, ApiClient api)
        {
            _js = js;
            _api = api;
        }

        /// <summary>'ativo' | 'desativado' | 'negado' | 'indisponivel' | 'instalar-ios'</summary>
        public async Task<string> SituacaoAsync()
        {
            try { return await _js.InvokeAsync<string>("helpMobile.situacaoPush"); }
            catch { return "indisponivel"; }
        }

        /// <summary>Precisa ser chamado a partir de um toque do usuário (exigência do iPhone).</summary>
        public async Task<string> AtivarAsync()
        {
            var chave = await _api.ChavePushAsync();
            if (string.IsNullOrWhiteSpace(chave)) return "indisponivel";

            var r = await _js.InvokeAsync<ResultadoJs>("helpMobile.ativarPush", chave);
            if (!string.IsNullOrWhiteSpace(r.Erro)) return r.Erro!;

            await RegistrarAsync(r);
            return "ativo";
        }

        /// <summary>Ao abrir o app: garante que a inscrição atual está salva no HELP (ela pode mudar sozinha).</summary>
        public async Task SincronizarAsync()
        {
            try
            {
                var r = await _js.InvokeAsync<ResultadoJs?>("helpMobile.inscricaoAtual");
                if (r != null && !string.IsNullOrWhiteSpace(r.Endpoint))
                    await RegistrarAsync(r);
            }
            catch { /* push é secundário */ }
        }

        public async Task DesativarAsync()
        {
            try
            {
                var endpoint = await _js.InvokeAsync<string?>("helpMobile.desativarPush");
                if (!string.IsNullOrWhiteSpace(endpoint))
                    await _api.CancelarPushAsync(endpoint);
            }
            catch { }
        }

        private async Task RegistrarAsync(ResultadoJs r)
        {
            var dispositivo = await _js.InvokeAsync<string>("helpMobile.descricaoDispositivo");
            await _api.InscreverPushAsync(new MobilePushInscricaoRequest
            {
                Endpoint = r.Endpoint ?? "",
                P256dh = r.P256dh ?? "",
                Auth = r.Auth ?? "",
                Dispositivo = dispositivo
            });
        }

        private class ResultadoJs
        {
            public string? Endpoint { get; set; }
            public string? P256dh { get; set; }
            public string? Auth { get; set; }
            public string? Erro { get; set; }
        }
    }
}
