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
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__helpJanelaAvulsa = true;");
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    var t = _web.CoreWebView2.DocumentTitle;
                    Title = string.IsNullOrWhiteSpace(t) ? "EvolutTech Help" : t;
                };
                // Abre pela página inicial e vai para o ticket quando o HELP estiver logado
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

        protected override void OnClosed(EventArgs e)
        {
            try { _web.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}
