using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace HelpDesktop
{
    /// <summary>
    /// Abrir uma tela do HELP (ex.: /ticket/123) a partir do HelpDesktop.
    /// O HELP não monta a tela do ticket se o endereço for carregado do zero (o login, que fica no
    /// navegador, ainda não foi lido). Por isso: carrega a página inicial, espera o HELP conectar e
    /// entrar com o login, e só então navega por dentro — igual a quando a pessoa clica.
    /// </summary>
    internal static class NavegacaoHelp
    {
        public static async Task IrQuandoProntoAsync(CoreWebView2? web, string rota, int esperaMaxSegundos = 25)
        {
            if (web == null || string.IsNullOrWhiteSpace(rota)) return;
            var seguro = JsonSerializer.Serialize(rota);
            var limite = DateTime.UtcNow.AddSeconds(esperaMaxSegundos);

            while (DateTime.UtcNow < limite)
            {
                try
                {
                    var pronto = await web.ExecuteScriptAsync(
                        "!!(window.Blazor && window.Blazor.navigateTo && window.helpConectado === true)");
                    if (pronto == "true")
                    {
                        await Task.Delay(700);   // o HELP termina de ler o login logo após conectar
                        await web.ExecuteScriptAsync($"window.Blazor.navigateTo({seguro})");
                        return;
                    }
                }
                catch { }
                await Task.Delay(300);
            }
        }
    }
}
