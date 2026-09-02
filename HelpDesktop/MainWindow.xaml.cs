using Microsoft.Web.WebView2.Core;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Text.Json;

namespace HelpDesktop
{
    public partial class MainWindow : Window
    {
        private const string CrmUrl = "https://help.evoluttech.com/";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Uri.TryCreate(CrmUrl, UriKind.Absolute, out var uri))
                {
                    MessageBox.Show("URL do CRM inválida: " + CrmUrl);
                    return;
                }

                await WebView.EnsureCoreWebView2Async();

                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                WebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                WebView.CoreWebView2.Navigate(uri.ToString());
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "WebView2 Runtime não está instalado neste computador. Instale o Microsoft Edge WebView2 Runtime."
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao abrir o CRM: " + ex.Message);
            }
        }

        private void CoreWebView2_PermissionRequested(
            object? sender,
            CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
            {
                e.State = CoreWebView2PermissionState.Allow;
                e.Handled = true;
            }
        }


        private void CoreWebView2_WebMessageReceived(
    object? sender,
    CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                var msg = JsonSerializer.Deserialize<WebViewMessage>(json);

                if (msg?.type == "abrir-acesso-remoto")
                {
                    AbrirAcessoRemoto(msg.codigoAcesso);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(msg?.Mensagem))
                {
                    PiscarIconeBarraTarefas();

                    MessageBox.Show(
                        msg.Mensagem,
                        "EvolutTech Help",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao processar mensagem: " + ex.Message);
            }
        }

        private class WebViewMessage
        {
            public string? type { get; set; }
            public string? codigoAcesso { get; set; }
            public string? Mensagem { get; set; }
        }

        private void AbrirAcessoRemoto(string? codigoAcesso)
        {
            if (string.IsNullOrWhiteSpace(codigoAcesso))
            {
                MessageBox.Show("Código de acesso não informado.");
                return;
            }

            var ip = "127.0.0.1";
            var port = 5100;
            var privateKey = "HDTk1Zns3OI1kyG4JlajvL3Nrl6ZduZWH0/XPMbd9JI=";
            var senhaPadrao = "evolut91401149";

            try
            {
                codigoAcesso = LimparEntradaAcessoRemoto(codigoAcesso);
                senhaPadrao = LimparEntradaAcessoRemoto(senhaPadrao);

                var result = KHelpDeskIntegraNative.Connect(
                    ip,
                    port,
                    privateKey,
                    codigoAcesso,
                    senhaPadrao
                );

                if (result.Success)
                    return;

                var erro = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.Data
                    : result.ErrorMessage;

                MessageBox.Show("Falha ao iniciar acesso remoto: " + erro);
            }
            catch (DllNotFoundException)
            {
                MessageBox.Show("DLL KHelpDeskIntegraV3.dll não encontrada junto do executável.");
            }
            catch (BadImageFormatException)
            {
                MessageBox.Show("Arquitetura incompatível entre o HelpDesktop e a DLL. Verifique x86/x64.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao iniciar acesso remoto: " + ex.Message);
            }
        }

        private static string LimparEntradaAcessoRemoto(string? valor)
        {
            return (valor ?? "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("\u200B", "")
                .Replace("\uFEFF", "")
                .Trim();
        }

        private void PiscarIconeBarraTarefas()
        {
            var handle = new WindowInteropHelper(this).Handle;

            if (handle == IntPtr.Zero)
                return;

            var info = new FLASHWINFO
            {
                cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof(FLASHWINFO))),
                hwnd = handle,
                dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                uCount = 5,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);
        }

        private const uint FLASHW_STOP = 0;
        private const uint FLASHW_CAPTION = 1;
        private const uint FLASHW_TRAY = 2;
        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMER = 4;
        private const uint FLASHW_TIMERNOFG = 12;

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
    }
}