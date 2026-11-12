using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HelpDesktop
{
    /// <summary>
    /// Print de uma área da tela (Ctrl+Alt+P): congela a tela, a pessoa arrasta o retângulo
    /// e a imagem vai para a área de transferência e para o ticket aberto no HELP.
    /// </summary>
    internal static class CapturaTela
    {
        public static bool EmAndamento { get; private set; }

        public static BitmapSource? Capturar()
        {
            if (EmAndamento) return null;
            EmAndamento = true;
            try
            {
                var tela = TelaInteira();
                if (tela == null) return null;

                var selecao = new SelecaoWindow(tela);
                selecao.ShowDialog();
                return selecao.Resultado;
            }
            finally
            {
                EmAndamento = false;
            }
        }

        public static string ParaDataUrl(BitmapSource imagem)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(imagem));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }

        // Todos os monitores, em pixels reais
        private static BitmapSource? TelaInteira()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) return null;

            IntPtr telaDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(telaDc);
            IntPtr bmp = CreateCompatibleBitmap(telaDc, w, h);
            IntPtr antigo = SelectObject(memDc, bmp);
            try
            {
                BitBlt(memDc, 0, 0, w, h, telaDc, x, y, SRCCOPY | CAPTUREBLT);
                SelectObject(memDc, antigo);
                var fonte = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                fonte.Freeze();
                return fonte;
            }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, telaDc);
            }
        }

        private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        private const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    }

    /// <summary>Tela congelada para escolher a área do print.</summary>
    internal sealed class SelecaoWindow : Window
    {
        private readonly BitmapSource _tela;
        private readonly System.Windows.Shapes.Path _escurecido = new() { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
        private readonly Rectangle _borda = new() { Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)), StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        private Point _inicio;
        private bool _arrastando;
        private bool _fechando;

        // Fechar uma vez só: fechar a janela dispara "perdeu o foco", que tentava fechar de novo
        // enquanto ela já estava fechando — isso derrubava o HelpDesktop.
        private void Fechar()
        {
            if (_fechando) return;
            _fechando = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _fechando = true;
            base.OnClosing(e);
        }

        public BitmapSource? Resultado { get; private set; }

        public SelecaoWindow(BitmapSource tela)
        {
            _tela = tela;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var dica = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Arraste para selecionar a área do print  ·  Esc cancela",
                    Foreground = Brushes.White,
                    FontSize = 14
                }
            };

            var desenho = new Canvas { IsHitTestVisible = false };
            desenho.Children.Add(_borda);

            var grade = new Grid();
            grade.Children.Add(new Image { Source = tela, Stretch = Stretch.Fill });
            grade.Children.Add(_escurecido);
            grade.Children.Add(desenho);
            grade.Children.Add(dica);
            Content = grade;

            Loaded += (_, _) => { AtualizarEscurecido(Rect.Empty); Activate(); Focus(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Fechar(); };
            MouseRightButtonUp += (_, _) => Fechar();
            Deactivated += (_, _) => { if (!_arrastando && !_fechando && IsVisible) Fechar(); };
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_fechando) return;
            _inicio = e.GetPosition(this);
            _arrastando = true;
            CaptureMouse();
            _borda.Visibility = Visibility.Visible;
            Desenhar(new Rect(_inicio, _inicio));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_arrastando) Desenhar(new Rect(_inicio, e.GetPosition(this)));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_arrastando) return;
            _arrastando = false;
            ReleaseMouseCapture();

            var area = new Rect(_inicio, e.GetPosition(this));
            if (area.Width < 4 || area.Height < 4) { Fechar(); return; }

            // Tela em pixels reais x janela em unidades do WPF (escala do Windows)
            double fx = _tela.PixelWidth / ActualWidth, fy = _tela.PixelHeight / ActualHeight;
            int x = (int)Math.Max(0, Math.Round(area.X * fx));
            int y = (int)Math.Max(0, Math.Round(area.Y * fy));
            int w = (int)Math.Min(_tela.PixelWidth - x, Math.Round(area.Width * fx));
            int h = (int)Math.Min(_tela.PixelHeight - y, Math.Round(area.Height * fy));

            if (w > 0 && h > 0)
            {
                var recorte = new CroppedBitmap(_tela, new Int32Rect(x, y, w, h));
                recorte.Freeze();
                Resultado = recorte;
            }
            Fechar();
        }

        private void Desenhar(Rect area)
        {
            Canvas.SetLeft(_borda, area.X);
            Canvas.SetTop(_borda, area.Y);
            _borda.Width = area.Width;
            _borda.Height = area.Height;
            AtualizarEscurecido(area);
        }

        private void AtualizarEscurecido(Rect area)
        {
            var tudo = new RectangleGeometry(new Rect(0, 0, Math.Max(ActualWidth, Width), Math.Max(ActualHeight, Height)));
            _escurecido.Data = area.IsEmpty
                ? tudo
                : new CombinedGeometry(GeometryCombineMode.Exclude, tudo, new RectangleGeometry(area));
        }
    }
}
