using CommunityToolkit.WinUI.Notifications;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Net.NetworkInformation;
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

        // Motor compartilhado com as janelas separadas (mesmo login)
        internal static CoreWebView2Environment? Ambiente;

        private bool _paginaCarregada;
        private string? _rotaPendente;          // link evoluthelp:// recebido antes do HELP carregar
        private int _ultimoContador = -1;

        // Atalhos globais
        private const int AtalhoTrazer = 0xB001;   // Ctrl+Alt+H
        private const int AtalhoPrint = 0xB002;    // Ctrl+Alt+P

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _estadoAntesDeMinimizar = WindowState; };

            // Clique no aviso do Windows: traz o HELP para frente e abre o ticket do aviso.
            // Botão "Responder": envia o texto digitado no próprio aviso, sem abrir a janela.
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                ToastArguments args;
                try { args = ToastArguments.Parse(toastArgs.Argument); } catch { return; }
                var resposta = toastArgs.UserInput.TryGetValue("resposta", out var r) ? r as string : null;
                Dispatcher.Invoke(() =>
                {
                    if (args.TryGetValue("acao", out var acao) && acao == "responder")
                        ResponderPeloAviso(args, resposta);
                    else
                        AoClicarNoAviso(args);
                });
            };

            // Voltou da suspensão ou a internet voltou: o HELP tenta reconectar na hora
            SystemEvents.PowerModeChanged += AoMudarEnergia;
            NetworkChange.NetworkAvailabilityChanged += AoMudarRede;

            if (App.LinkInicial is { } link)
                _rotaPendente = RotaDoLink(link);
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
                // autoplay: o bipe do aviso toca mesmo que ninguém tenha clicado no HELP desde que ele abriu.
                var opcoes = new CoreWebView2EnvironmentOptions(
                    "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --autoplay-policy=no-user-gesture-required");
                var ambiente = await CoreWebView2Environment.CreateAsync(null, null, opcoes);
                await WebView.EnsureCoreWebView2Async(ambiente);
                Ambiente = ambiente;

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

                // Página recarregando: zera o contador (o HELP manda o número de novo ao carregar)
                WebView.CoreWebView2.NavigationStarting += (_, _) => { _paginaCarregada = false; AtualizarSelo(0); };
                WebView.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    if (!args.IsSuccess) return;
                    _paginaCarregada = true;

                    // Link evoluthelp:// ou clique em aviso que chegou antes do HELP carregar:
                    // abre assim que a pessoa entrar no HELP (espera o login, até 10 minutos)
                    if (_rotaPendente is { } rota)
                    {
                        _rotaPendente = null;
                        await NavegacaoHelp.IrQuandoProntoAsync(WebView.CoreWebView2, rota, 600);
                    }
                };

                // Sempre abre pela tela de login (o ticket direto pelo endereço dá erro no HELP)
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

                // Aviso de ticket: dá para responder ao cliente direto pelo balão
                if (CodTicketDaTag(aviso.Tag) is int codTicket)
                {
                    builder.AddArgument("ticket", codTicket)
                        .AddInputTextBox("resposta", "Responder ao cliente...")
                        .AddButton(new ToastButton()
                            .SetContent("Responder")
                            .AddArgument("acao", "responder")
                            .SetTextBoxId("resposta"));
                }
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
            catch (Exception ex)
            {
                // Falhou o aviso próprio: registra e deixa o WebView2 mostrar o dele
                App.RegistrarErro(ex);
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

            if (args.TryGetValue("tag", out var tag) && CodTicketDaTag(tag) is int codTicket)
                Navegar($"/ticket/{codTicket}");
        }

        private static int? CodTicketDaTag(string? tag) =>
            tag != null && tag.StartsWith("ticket-") && int.TryParse(tag["ticket-".Length..], out var cod) && cod > 0
                ? cod : null;

        // ── Responder pelo aviso ──
        private void ResponderPeloAviso(ToastArguments args, string? resposta)
        {
            if (!args.TryGetValue("ticket", out var textoCod) || !int.TryParse(textoCod, out var codTicket))
                return;

            resposta = (resposta ?? "").Trim();
            if (resposta.Length == 0)
            {
                // Clicou em Responder sem escrever: abre o ticket
                TrazerParaFrente();
                Navegar($"/ticket/{codTicket}");
                return;
            }

            if (WebView.CoreWebView2 == null || !_paginaCarregada)
            {
                AvisoSimples("Resposta não enviada", "O HELP ainda está carregando. Abra o ticket para responder.", codTicket);
                return;
            }

            WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "responder",
                codigo = codTicket,
                texto = resposta
            }));
        }

        private void AvisoSimples(string titulo, string texto, int? codTicket = null)
        {
            try
            {
                var builder = new ToastContentBuilder().AddText(titulo).AddText(texto)
                    .AddAudio(new ToastAudio { Silent = true });
                if (codTicket is int cod)
                    builder.AddArgument("tag", $"ticket-{cod}");
                builder.Show(t => { t.Group = GrupoAvisos; });
            }
            catch (Exception ex) { App.RegistrarErro(ex); }
        }

        // ── Navegação dentro do HELP ──
        private void Navegar(string rota)
        {
            // HELP ainda carregando: vai para a tela assim que terminar
            if (WebView.CoreWebView2 == null || !_paginaCarregada) { _rotaPendente = rota; return; }
            _ = NavegacaoHelp.IrQuandoProntoAsync(WebView.CoreWebView2, rota);
        }

        /// <summary>Link evoluthelp://ticket/123 (de e-mail, ERP, chat...).</summary>
        internal void AbrirLink(string link)
        {
            var rota = RotaDoLink(link);
            TrazerParaFrente();
            if (rota != null) Navegar(rota);
        }

        internal static string? RotaDoLink(string link)
        {
            if (!Uri.TryCreate((link ?? "").Trim(), UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals("evoluthelp", StringComparison.OrdinalIgnoreCase))
                return null;
            return RotaPermitida(uri.Host + "/" + uri.AbsolutePath);
        }

        // Só telas conhecidas, com número válido (nada de endereço arbitrário vindo de fora)
        private static string? RotaPermitida(string? caminho)
        {
            var partes = (caminho ?? "").Split('?')[0].ToLowerInvariant()
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 2 && (partes[0] == "ticket" || partes[0] == "card") &&
                int.TryParse(partes[1], out var n) && n > 0)
                return $"/{partes[0]}/{n}";
            if (partes.Length == 1 && (partes[0] == "tickets" || partes[0] == "chat-interno" || partes[0] == "menu"))
                return "/" + partes[0];
            return null;
        }

        // ── Contador de não lidos no ícone da barra de tarefas ──
        private void AtualizarSelo(int total)
        {
            if (total < 0) total = 0;
            if (total == _ultimoContador) return;
            _ultimoContador = total;
            TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            TaskbarItemInfo.Overlay = total > 0 ? SeloBarraTarefas.Criar(total) : null;
            TaskbarItemInfo.Description = total > 0 ? $"EvolutTech Help · {total} aguardando" : "EvolutTech Help";
        }

        // ── Print de área (Ctrl+Alt+P) ──
        private void FazerPrint()
        {
            var imagem = CapturaTela.Capturar();
            if (imagem == null) return;

            try { Clipboard.SetImage(imagem); } catch { }

            if (WebView.CoreWebView2 == null || !_paginaCarregada)
            {
                AvisoSimples("Print copiado", "Cole com Ctrl+V onde quiser.");
                return;
            }

            WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "print",
                imagem = CapturaTela.ParaDataUrl(imagem)
            }));
        }

        // ── Atalhos globais ──
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(ProcessarMensagemWindows);
            RegisterHotKey(handle, AtalhoTrazer, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x48); // H
            RegisterHotKey(handle, AtalhoPrint, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x50);  // P
        }

        private IntPtr ProcessarMensagemWindows(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (id == AtalhoTrazer) { TrazerParaFrente(); handled = true; }
                else if (id == AtalhoPrint) { handled = true; Dispatcher.BeginInvoke(new Action(FazerPrint)); }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, AtalhoTrazer);
            UnregisterHotKey(handle, AtalhoPrint);
            SystemEvents.PowerModeChanged -= AoMudarEnergia;
            NetworkChange.NetworkAvailabilityChanged -= AoMudarRede;
            base.OnClosed(e);
        }

        // ── Reconexão rápida ──
        private void AoMudarEnergia(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) AcordarHelp();
        }

        private void AoMudarRede(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable) AcordarHelp();
        }

        private void AcordarHelp()
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(3000);   // dá tempo da rede subir
                try
                {
                    if (WebView.CoreWebView2 != null)
                        await WebView.CoreWebView2.ExecuteScriptAsync("window.helpAcordar && window.helpAcordar()");
                }
                catch { }
            });
        }

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        internal void TrazerParaFrente()
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

                // Mensagens novas só valem vindas do próprio HELP
                var doHelp = (e.Source ?? "").StartsWith(CrmOrigem, StringComparison.OrdinalIgnoreCase);

                if (doHelp && msg?.type == "contador")
                {
                    AtualizarSelo(msg.total ?? 0);
                    return;
                }

                if (doHelp && msg?.type == "resposta-aviso")
                {
                    if (msg.ok == true)
                        AvisoSimples("✓ Resposta enviada", "Ticket #" + msg.codigo);
                    else
                        AvisoSimples("Resposta não enviada", string.IsNullOrWhiteSpace(msg.texto)
                            ? "Abra o ticket para responder." : msg.texto!, msg.codigo);
                    return;
                }

                if (doHelp && msg?.type == "print-resultado")
                {
                    if (msg.ok == true) TrazerParaFrente();
                    else AvisoSimples("Print copiado", "Nenhum ticket aberto: cole com Ctrl+V onde quiser.");
                    return;
                }

                if (doHelp && msg?.type == "janela")
                {
                    if (RotaPermitida(msg.url) is { } rota)
                        new JanelaAvulsa(CrmUrl + "menu", rota).Show();
                    return;
                }

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
            public int? total { get; set; }
            public bool? ok { get; set; }
            public int? codigo { get; set; }
            public string? texto { get; set; }
            public string? url { get; set; }
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