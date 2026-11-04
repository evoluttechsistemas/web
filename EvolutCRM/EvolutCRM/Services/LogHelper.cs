namespace EvolutCRM.Services;

public static class LogHelper
{
    private static readonly string Pasta = @"C:\FTP\Site\Help\logs";

    public static void Log(string texto)
    {
        try
        {
            if (!Directory.Exists(Pasta))
                Directory.CreateDirectory(Pasta);

            var arquivo = Path.Combine(Pasta, $"ia-{DateTime.Now:yyyy-MM-dd}.log");

            File.AppendAllText(
                arquivo,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {texto}{Environment.NewLine}"
            );
        }
        catch
        {
            // nunca deixar erro de log quebrar a aplicação
        }
    }
}