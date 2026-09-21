using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EvolutCRM.Services
{
    public class DeployNotificacaoService : BackgroundService
    {
        private readonly string _conn;
        private readonly DeployNotificacaoState _state;

        public DeployNotificacaoService(IConfiguration cfg, DeployNotificacaoState state)
        {
            _conn = cfg.GetConnectionString("Connection")!;
            _state = state;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await VerificarNotificacaoAsync();
                }
                catch
                {
                    // Silencia erros — não pode derrubar o background service
                }
            }
        }

        private async Task VerificarNotificacaoAsync()
        {
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(@"
    SELECT TOP 1 Id, Tipo, Mensagem
    FROM DeployNotificacao
    WHERE Lido = 0
      AND DataHora >= DATEADD(MINUTE, -10, GETDATE())
    ORDER BY DataHora DESC", conn);

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                if (_state.Ativo && _state.Tipo != "aviso30seg")
                    _state.Limpar();
                return;
            }

            var id = rd.GetInt32(0);
            var tipo = rd.GetString(1);
            var mensagem = rd.GetString(2);
            rd.Close();

            await using var cmdUpdate = new SqlCommand(
                "UPDATE DeployNotificacao SET Lido = 1 WHERE Id = @Id", conn);
            cmdUpdate.Parameters.AddWithValue("@Id", id);
            await cmdUpdate.ExecuteNonQueryAsync();

            _state.Notificar(tipo, mensagem);
        }
    }
}