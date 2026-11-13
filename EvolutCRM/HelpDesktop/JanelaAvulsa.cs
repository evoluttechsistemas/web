using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace HelpDesktop
{
    /// <summary>
    /// Ticket em janela própria (para acompanhar dois atendimentos lado a lado).
    /// Usa o mesmo motor e o mesmo login do HelpDesktop; não dispara avisos nem sons
    /// (quem avisa é a janela principal, para não duplicar).
    /// </summary>
    internal sealed class JanelaAvulsa : Window
    {
        private readonly WebView2 _web = new();
        private readonly string _paginaInicial;
        private readonly string _rota;
        private bool _abriuTela;

        public JanelaAvulsa(string paginaInicial, string rota)
        {
            _paginaInicial = paginaInicial;
            _rota = rota;
            Title = "EvolutTech Help";
            Width = 1100;
            Height = 800;
            MinWidth = 700;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Application.Current.MainWindow?.Icon != null)
                Icon = Application.Current.MainWindow.Icon;
            Content = _web;
            Loaded += async (_, _) => await IniciarAsync();
        }

        private async Task IniciarAsync()
        {
            try
            {
                await _web.EnsureCoreWebView2Async(MainWindow.Ambiente);
                // Marca a página e esconde menu, abas e notificações: fica só o ticket (igual à documentação).
                // O script roda antes de a página existir, então espera o <html> aparecer para marcar.
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ScriptJanelaAvulsa);
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    var t = _web.CoreWebView2.DocumentTitle;
                    Title = string.IsNullOrWhiteSpace(t) ? "EvolutTech Help" : t;
                };
                // Abre pelo menu (que lê o login já salvo, sem pedir de novo) e vai para o ticket
                _web.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    if (!args.IsSuccess || _abriuTela) return;
                    _abriuTela = true;
                    await NavegacaoHelp.IrQuandoProntoAsync(_web.CoreWebView2, _rota);
                };
                _web.CoreWebView2.Navigate(_paginaInicial);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível abrir a janela separada: " + ex.Message, "EvolutTech Help");
                Close();
            }
        }

        private const string ScriptJanelaAvulsa = @"
window.__helpJanelaAvulsa = true;
(function () {
    var css =
        'html.help-janela-avulsa .layout{display:block!important;height:100vh}' +
        'html.help-janela-avulsa .layout>nav,html.help-janela-avulsa .notif-panel,html.help-janela-avulsa .aba-topo,' +
        'html.help-janela-avulsa .ham-btn,html.help-janela-avulsa .mob-overlay{display:none!important}' +
        'html.help-janela-avulsa .content{width:100%!important;height:100vh!important}';
    function aplicar() {
        var raiz = document.documentElement;
        if (!raiz) return false;
        raiz.classList.add('help-janela-avulsa');
        if (!document.getElementById('help-janela-avulsa-css') && (document.head || raiz)) {
            var st = document.createElement('style');
            st.id = 'help-janela-avulsa-css';
            st.textContent = css;
            (document.head || raiz).appendChild(st);
        }
        return true;
    }
    if (!aplicar()) {
        var obs = new MutationObserver(function () { if (aplicar()) obs.disconnect(); });
        obs.observe(document, { childList: true, subtree: true });
    }
    document.addEventListener('DOMContentLoaded', aplicar);
})();";

        protected override void OnClosed(EventArgs e)
        {
            try { _web.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}