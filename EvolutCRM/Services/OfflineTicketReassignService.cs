using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class OfflineTicketReassignService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public OfflineTicketReassignService(IServiceScopeFactory scopeFactory)
            => _scopeFactory = scopeFactory;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var conn = config.GetConnectionString("Connection");

                    using var con = new SqlConnection(conn);
                    await con.OpenAsync(stoppingToken);

                    using var cmd = new SqlCommand(@"
                        UPDATE TicketChamadoC
                        SET Usuario = 'NOVO', Novo = 'S'
                        WHERE ISNULL(CodSituacao, 1) = 1
                          AND ISNULL(ChatInterno, 'N') <> 'S'
                          AND UPPER(LTRIM(RTRIM(ISNULL(Usuario, '')))) NOT IN
                              ('', 'NOVO', 'WHATSAPP', 'CLIENTE', 'BOT', 'SISTEMA')
                          AND EXISTS (
                              SELECT 1 FROM Usuario U
                              WHERE UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(TicketChamadoC.Usuario)))
                                AND ISNULL(U.Online, 'N') = 'N'
                                AND EXISTS (
                                    SELECT 1 FROM TicketChamadoD D
                                    WHERE D.CodTicketChamadoC = TicketChamadoC.Codigo
                                      AND ISNULL(D.EnvioCliente, 'N') = 'S'
                                      AND D.DataHora > ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                )
                          )", con);

                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }
                catch { /* nunca derruba o serviço */ }
            }
        }
    }
}