using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class OfflineTicketReassignService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private bool _colunaMobileExiste = true;

        private const string SqlOriginal = @"
                        UPDATE TicketChamadoC
                        SET Usuario = 'NOVO', Novo = 'S'
                        WHERE ISNULL(CodSituacao, 1) = 1
                          AND ISNULL(ChatInterno, 'N') <> 'S'
                          AND UPPER(LTRIM(RTRIM(ISNULL(Usuario, '')))) NOT IN
                              ('', 'NOVO', 'WHATSAPP', 'CLIENTE', 'BOT', 'SISTEMA')
                          AND EXISTS (
                              SELECT 1 FROM Usuario U
                              WHERE UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(TicketChamadoC.Usuario)))
                                AND (
                                    -- 1) Responsável Offline e o cliente respondeu depois da última atividade dele
                                    (
                                        ISNULL(U.Online, 'N') = 'N'
                                        -- Transferido para ele enquanto estava offline: fica com ele até ele entrar
                                        AND NOT (
                                            TicketChamadoC.DataHoraTransferencia IS NOT NULL
                                            AND TicketChamadoC.DataHoraTransferencia > ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                        )
                                        AND EXISTS (
                                            SELECT 1 FROM TicketChamadoD D
                                            WHERE D.CodTicketChamadoC = TicketChamadoC.Codigo
                                              AND ISNULL(D.EnvioCliente, 'N') = 'S'
                                              AND D.DataHora > ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                        )
                                    )
                                    OR
                                    -- 2) Responsável num status marcado para redistribuir (ex.: Treinamento)
                                    --    e o cliente respondeu depois que ele entrou nesse status
                                    (
                                        ISNULL(U.Online, 'N') = 'S'
                                        AND U.DataHoraStatus IS NOT NULL
                                        AND EXISTS (
                                            SELECT 1 FROM UsuarioStatus S
                                            WHERE S.CodEmp = U.CodEmp
                                              AND S.Codigo = U.CodUsuarioStatus
                                              AND ISNULL(S.RedistribuiTickets, 'N') = 'S'
                                        )
                                        AND EXISTS (
                                            SELECT 1 FROM TicketChamadoD D
                                            WHERE D.CodTicketChamadoC = TicketChamadoC.Codigo
                                              AND ISNULL(D.EnvioCliente, 'N') = 'S'
                                              AND D.DataHora > U.DataHoraStatus
                                        )
                                    )
                                )
                          )";

        private const string SqlComMobile = @"
                        UPDATE TicketChamadoC
                        SET Usuario = 'NOVO', Novo = 'S'
                        WHERE ISNULL(CodSituacao, 1) = 1
                          AND ISNULL(ChatInterno, 'N') <> 'S'
                          AND UPPER(LTRIM(RTRIM(ISNULL(Usuario, '')))) NOT IN
                              ('', 'NOVO', 'WHATSAPP', 'CLIENTE', 'BOT', 'SISTEMA')
                          AND EXISTS (
                              SELECT 1 FROM Usuario U
                              WHERE UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(TicketChamadoC.Usuario)))
                                AND (
                                    -- 1) Responsável Offline e o cliente respondeu depois da última atividade dele
                                    (
                                        ISNULL(U.Online, 'N') = 'N'
                                        -- Transferido para ele enquanto estava offline: fica com ele até ele entrar
                                        AND NOT (
                                            TicketChamadoC.DataHoraTransferencia IS NOT NULL
                                            AND TicketChamadoC.DataHoraTransferencia > CASE
                                                  WHEN ISNULL(U.UltimaAtividadeMobile, '2000-01-01') > ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                                  THEN U.UltimaAtividadeMobile
                                                  ELSE ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                                END
                                        )
                                        -- HELP Mobile aberto (atividade nos últimos 90s): o ticket continua com ele
                                        AND (U.UltimaAtividadeMobile IS NULL
                                             OR DATEDIFF(SECOND, U.UltimaAtividadeMobile, GETDATE()) > 90)
                                        AND EXISTS (
                                            SELECT 1 FROM TicketChamadoD D
                                            WHERE D.CodTicketChamadoC = TicketChamadoC.Codigo
                                              AND ISNULL(D.EnvioCliente, 'N') = 'S'
                                              -- última atividade = a mais recente entre HELP desktop e HELP Mobile
                                              AND D.DataHora > CASE
                                                    WHEN ISNULL(U.UltimaAtividadeMobile, '2000-01-01') > ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                                    THEN U.UltimaAtividadeMobile
                                                    ELSE ISNULL(U.UltimaAtividadeOnline, '2000-01-01')
                                                  END
                                        )
                                    )
                                    OR
                                    -- 2) Responsável num status marcado para redistribuir (ex.: Treinamento)
                                    --    e o cliente respondeu depois que ele entrou nesse status
                                    (
                                        ISNULL(U.Online, 'N') = 'S'
                                        AND U.DataHoraStatus IS NOT NULL
                                        AND EXISTS (
                                            SELECT 1 FROM UsuarioStatus S
                                            WHERE S.CodEmp = U.CodEmp
                                              AND S.Codigo = U.CodUsuarioStatus
                                              AND ISNULL(S.RedistribuiTickets, 'N') = 'S'
                                        )
                                        AND EXISTS (
                                            SELECT 1 FROM TicketChamadoD D
                                            WHERE D.CodTicketChamadoC = TicketChamadoC.Codigo
                                              AND ISNULL(D.EnvioCliente, 'N') = 'S'
                                              AND D.DataHora > U.DataHoraStatus
                                        )
                                    )
                                )
                          )";

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

                    // SQL com a regra do HELP Mobile (quem está com o app aberto não perde o ticket).
                    // Se a coluna Usuario.UltimaAtividadeMobile ainda não existir no banco, usa o SQL antigo.
                    var sql = _colunaMobileExiste ? SqlComMobile : SqlOriginal;
                    using var cmd = new SqlCommand(sql, con);
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(stoppingToken);
                    }
                    catch (SqlException ex) when (ex.Number == 207 && _colunaMobileExiste)
                    {
                        // 207 = coluna inválida: script do HELP Mobile ainda não rodou → volta para a regra antiga
                        _colunaMobileExiste = false;
                    }
                }
                catch { /* nunca derruba o serviço */ }
            }
        }
    }
}