using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace HelpDesktop
{
    /// <summary>
    /// Faz o HelpDesktop abrir primeiro ao entrar no Windows, sem precisar configurar nada à mão.
    /// Cria (ou corrige) a tarefa agendada "HelpDesktop Startup": ao fazer logon, sem espera,
    /// também na bateria, prioridade normal, sem limite de tempo e sem abrir uma segunda cópia.
    /// Tarefa "ao fazer logon" roda antes da pasta Inicializar e da lista de inicialização do Windows.
    /// </summary>
    internal static class InicializacaoWindows
    {
        private const string NomeTarefa = "HelpDesktop Startup";

        public static void Garantir()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                    return;

                // Rodando pelo Visual Studio (bin\Debug, bin\Release): não aponta a tarefa para a pasta de desenvolvimento
                var caminho = exe.ToLowerInvariant();
                if (caminho.Contains(@"\bin\debug\") || caminho.Contains(@"\bin\release\"))
                    return;

                RegistrarLink(exe);

                var usuario = WindowsIdentity.GetCurrent().Name;
                var xml = MontarXml(exe, Path.GetDirectoryName(exe) ?? "", usuario);

                var arquivo = Path.Combine(Path.GetTempPath(), $"helpdesktop-tarefa-{Guid.NewGuid():N}.xml");
                File.WriteAllText(arquivo, xml, Encoding.Unicode);   // o Agendador exige UTF-16

                try
                {
                    var info = new ProcessStartInfo("schtasks.exe",
                        $"/Create /TN \"{NomeTarefa}\" /XML \"{arquivo}\" /F")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var processo = Process.Start(info);
                    processo?.WaitForExit(15000);
                    // Sem permissão para trocar uma tarefa criada por outro usuário: mantém a que já existe
                }
                finally
                {
                    try { File.Delete(arquivo); } catch { }
                }
            }
            catch
            {
                // Nunca impede o HelpDesktop de abrir
            }
        }

        /// <summary>
        /// Link evoluthelp://ticket/123 abre o ticket no HelpDesktop (de e-mail, ERP, chat...).
        /// Fica no registro do usuário: não precisa de administrador.
        /// </summary>
        private static void RegistrarLink(string exe)
        {
            try
            {
                using var chave = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\evoluthelp");
                chave.SetValue("", "URL:EvolutTech Help");
                chave.SetValue("URL Protocol", "");
                using (var icone = chave.CreateSubKey("DefaultIcon"))
                    icone.SetValue("", $"\"{exe}\",0");
                using (var comando = chave.CreateSubKey(@"shell\open\command"))
                    comando.SetValue("", $"\"{exe}\" \"%1\"");
            }
            catch { }
        }

        private static string MontarXml(string exe, string pasta, string usuario)
        {
            string E(string s) => SecurityElement.Escape(s) ?? "";
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Abre o HelpDesktop assim que o usuário entra no Windows.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{E(usuario)}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{E(usuario)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{E(exe)}</Command>
      <WorkingDirectory>{E(pasta)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }
    }
}
