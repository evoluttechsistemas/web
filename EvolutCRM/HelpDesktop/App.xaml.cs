using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace HelpDesktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Instância única: se o HelpDesktop já está aberto (ex.: inicialização configurada em mais de um
        // lugar, atalho clicado de novo ou link evoluthelp://), a cópia nova repassa o pedido para a
        // janela que já existe e fecha. Antes abriam 2 ou 3 janelas, cada uma com seus avisos e sons.
        private const string NomeMutex = @"Local\EvolutTech.HelpDesktop.InstanciaUnica";
        private static string NomeCanal => "EvolutTech.HelpDesktop." + Environment.UserName;

        private static Mutex? _mutex;

        /// <summary>Link evoluthelp:// com que o programa foi aberto (a janela abre direto nele).</summary>
        internal static string? LinkInicial { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            var link = e.Args.FirstOrDefault(a => a.StartsWith("evoluthelp:", StringComparison.OrdinalIgnoreCase));

            _mutex = new Mutex(true, NomeMutex, out var primeiraInstancia);

            if (!primeiraInstancia)
            {
                EnviarParaJanelaAberta(link ?? "mostrar");
                Environment.Exit(0);
                return;
            }

            LinkInicial = link;
            EscutarOutrasCopias();

            // Um erro numa função (print, aviso, atalho) não pode fechar o HelpDesktop inteiro:
            // registra e segue rodando, para os avisos de atendimento continuarem chegando.
            DispatcherUnhandledException += (_, args) =>
            {
                RegistrarErro(args.Exception);
                args.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                RegistrarErro(args.Exception);
                args.SetObserved();
            };

            // Garante a abertura automática ao ligar e o link evoluthelp:// (em segundo plano, não atrasa a janela)
            _ = Task.Run(InicializacaoWindows.Garantir);

            base.OnStartup(e);
        }

        private static void EnviarParaJanelaAberta(string pedido)
        {
            try
            {
                using var canal = new NamedPipeClientStream(".", NomeCanal, PipeDirection.Out);
                canal.Connect(3000);
                using var escrita = new StreamWriter(canal);
                escrita.WriteLine(pedido);
                escrita.Flush();
            }
            catch { }
        }

        private void EscutarOutrasCopias()
        {
            var escuta = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var canal = new NamedPipeServerStream(NomeCanal, PipeDirection.In, 1);
                        canal.WaitForConnection();
                        using var leitura = new StreamReader(canal);
                        var pedido = (leitura.ReadLine() ?? "").Trim();
                        if (pedido.Length > 500) pedido = pedido[..500];

                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is not HelpDesktop.MainWindow janela) return;
                            if (pedido.StartsWith("evoluthelp:", StringComparison.OrdinalIgnoreCase))
                                janela.AbrirLink(pedido);
                            else
                                janela.TrazerParaFrente();
                        });
                    }
                    catch
                    {
                        Thread.Sleep(500);
                    }
                }
            })
            { IsBackground = true, Name = "InstanciaUnica" };
            escuta.Start();
        }

        // %LocalAppData%\HelpDesktop\erros.log (fica pequeno: recomeça ao passar de 1 MB)
        internal static void RegistrarErro(Exception ex)
        {
            try
            {
                var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelpDesktop");
                Directory.CreateDirectory(pasta);
                var arquivo = Path.Combine(pasta, "erros.log");
                if (File.Exists(arquivo) && new FileInfo(arquivo).Length > 1_000_000)
                    File.Delete(arquivo);
                File.AppendAllText(arquivo, $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }

}
