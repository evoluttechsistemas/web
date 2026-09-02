using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EvolutCRM.Services
{
    public class BackupMonitorBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<BackupMonitorBackgroundService> _logger;
        private readonly Dictionary<string, DateTime> _ultimaNotificacao = new();

        public BackupMonitorBackgroundService(
            IServiceProvider services,
            ILogger<BackupMonitorBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);

            await VerificarAsync();

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                    await VerificarAsync();
            }
            catch (OperationCanceledException) { }
        }

        private async Task VerificarAsync()
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var backupSvc = scope.ServiceProvider.GetRequiredService<MonitorBackupService>();

                var criticos = await backupSvc.ObterClientesParaAlertaAsync();

                var semBackup = await backupSvc.ObterClientesSemBackupAsync();
                criticos.AddRange(semBackup.Select(x => new MonitorBackupModel
                {
                    CodCliente = x.CodCliente,
                    NomeCliente = x.NomeCliente,
                    Cnpj = x.Cnpj,
                    Status = StatusBackup.SemBackup,
                    DataHoraUltimaSincronizacao = null
                }));

                foreach (var cliente in criticos)
                {
                    var chave = $"{cliente.CodCliente}_{cliente.Cnpj}";
                    var agora = DateTime.Now;

                    if (_ultimaNotificacao.TryGetValue(chave, out var ultima)
                        && (agora - ultima).TotalHours < 24)
                        continue;

                    var dias = cliente.DataHoraUltimaSincronizacao.HasValue
                        ? (int)(agora - cliente.DataHoraUltimaSincronizacao.Value).TotalDays
                        : -1;

                    var mensagem = dias >= 0
                        ? $"{dias} dia(s) sem backup. Último: {cliente.DataHoraUltimaSincronizacao:dd/MM/yyyy HH:mm} ({cliente.Computador})"
                        : "Nenhum backup registrado para este cliente.";

                    _ultimaNotificacao[chave] = agora;

                    _logger.LogWarning(
                        "Alerta backup: {Cliente} — {Mensagem}",
                        cliente.NomeCliente,
                        mensagem);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no BackupMonitorBackgroundService");
            }
        }
    }
}