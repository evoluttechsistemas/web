using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelpDesktop
{
    /// <summary>Bolinha vermelha com o número de não lidos sobre o ícone da barra de tarefas.</summary>
    internal static class SeloBarraTarefas
    {
        public static ImageSource Criar(int total)
        {
            const int tamanho = 32;
            var texto = total > 99 ? "99+" : total.ToString(CultureInfo.InvariantCulture);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 38, 38)), new Pen(Brushes.White, 2),
                    new Point(tamanho / 2.0, tamanho / 2.0), tamanho / 2.0 - 1, tamanho / 2.0 - 1);

                var fonte = new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    texto.Length >= 3 ? 12 : texto.Length == 2 ? 15 : 18, Brushes.White, 1.0);

                dc.DrawText(fonte, new Point((tamanho - fonte.Width) / 2, (tamanho - fonte.Height) / 2));
            }

            var imagem = new RenderTargetBitmap(tamanho, tamanho, 96, 96, PixelFormats.Pbgra32);
            imagem.Render(visual);
            imagem.Freeze();
            return imagem;
        }
    }
}
