using Microsoft.Toolkit.Uwp.Notifications;
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
        private const string CrmOrigem = "https://help.evoluttech.com";
        private const string GrupoAvisos = "help";

        // Avisos mostrados no Windows, para repassar o clique à página (abre o ticket certo)
        private readonly Dictionary<string, CoreWebView2Notification> _avisos = new();
        private readonly Queue<string> _ordemAvisos = new();
        private WindowState _estadoAntesDeMinimizar = WindowState.Maximized;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _estadoAntesDeMinimizar = WindowState; };

            // Clique no aviso do Windows: traz o HELP para frente e abre o ticket do aviso
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                ToastArguments args;
                try { args = ToastArguments.Parse(toastArgs.Argument); } catch { return; }
                Dispatcher.Invoke(() => AoClicarNoAviso(args));
            };
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

                // A página continua rodando com a janela minimizada ou atrás de outro programa.
                // Sem isso o motor do Edge "desacelera" o HELP em segundo plano e os avisos atrasam ou não saem.
                var opcoes = new CoreWebView2EnvironmentOptions(
                    "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
                var ambiente = await CoreWebView2Environment.CreateAsync(null, null, opcoes);
                await WebView.EnsureCoreWebView2Async(ambiente);

                // Notificações já liberadas desde o início (antes dependia de um pedido que às vezes não acontecia)
                try
                {
                    await WebView.CoreWebView2.Profile.SetPermissionStateAsync(
                        CoreWebView2PermissionKind.Notifications, CrmOrigem, CoreWebView2PermissionState.Allow);
                }
                catch { }

                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                WebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                WebView.CoreWebView2.NotificationReceived += CoreWebView2_NotificationReceived;

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


        // ── Avisos do HELP como notificação do Windows ──
        // O WebView2 não garante o balão do Windows sozinho (app instalado fora da loja). O HelpDesktop
        // assume o aviso e mostra ele mesmo, com o nome e o ícone do aplicativo.
        private void CoreWebView2_NotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
        {
            var aviso = e.Notification;
            var id = Guid.NewGuid().ToString("N");
            var tagWindows = TagWindows(aviso.Tag, id);

            try
            {
                e.Handled = true;

                var builder = new ToastContentBuilder()
                    .AddArgument("id", id)
                    .AddArgument("tag", aviso.Tag ?? "")
                    .AddText(string.IsNullOrWhiteSpace(aviso.Title) ? "EvolutTech Help" : aviso.Title)
                    .AddText(aviso.Body ?? "");
                if (aviso.IsSilent)
                    builder.AddAudio(new ToastAudio { Silent = true });

                builder.Show(toast =>
                {
                    toast.Tag = tagWindows;   // mesmo ticket: o aviso novo substitui o anterior
                    toast.Group = GrupoAvisos;
                });

                aviso.ReportShown();
                GuardarAviso(id, aviso);

                aviso.CloseRequested += (_, _) =>
                {
                    try { ToastNotificationManagerCompat.History.Remove(tagWindows, GrupoAvisos); } catch { }
                    _avisos.Remove(id);
                };

                if (!IsActive)
                    PiscarIconeBarraTarefas();
            }
            catch
            {
                // Falhou o aviso próprio: deixa o WebView2 mostrar o dele
                e.Handled = false;
            }
        }

        private void GuardarAviso(string id, CoreWebView2Notification aviso)
        {
            _avisos[id] = aviso;
            _ordemAvisos.Enqueue(id);
            while (_ordemAvisos.Count > 100)
                _avisos.Remove(_ordemAvisos.Dequeue());
        }

        // Tag do Windows aceita até 64 caracteres
        private static string TagWindows(string? tag, string id)
        {
            if (string.IsNullOrWhiteSpace(tag)) return id[..32];
            if (tag.Length <= 60) return tag;
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tag));
            return Convert.ToHexString(hash)[..32];
        }

        private void AoClicarNoAviso(ToastArguments args)
        {
            TrazerParaFrente();

            if (args.TryGetValue("id", out var id) && _avisos.TryGetValue(id, out var aviso))
            {
                _avisos.Remove(id);
                try
                {
                    aviso.ReportClicked();   // a página trata o clique: abre a tela do aviso
                    return;
                }
                catch { }   // página recarregou depois do aviso: abre pelo número do ticket
            }

            if (args.TryGetValue("tag", out var tag) && tag.StartsWith("ticket-") &&
                int.TryParse(tag["ticket-".Length..], out var codTicket))
            {
                _ = WebView.CoreWebView2?.ExecuteScriptAsync(
                    $"window.Blazor && window.Blazor.navigateTo('/ticket/{codTicket}')");
            }
        }

        private void TrazerParaFrente()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = _estadoAntesDeMinimizar;
            Show();
            Activate();
            Topmost = true;   // garante que vem para frente de outros programas
            Topmost = false;
            Focus();
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