using Microsoft.Data.SqlClient;
using System.Net;
using System.Text.Json;
using WebPush;

namespace EvolutCRM.Mobile
{
    /// <summary>
    /// Notificação no celular (Web Push), mesmo com o app fechado.
    ///
    /// O Baileys grava as mensagens direto no TicketChamadoD, então — igual ao WhatsAppAnaliseWorker —
    /// a única forma confiável de saber que chegou mensagem é olhar o banco. Uma consulta leve a cada
    /// 3 segundos para TODOS os celulares (não uma por aparelho).
    ///
    /// Quem recebe:
    ///   • ticket com responsável  → só os celulares do responsável
    ///   • ticket na fila (NOVO)   → todos os celulares da empresa
    /// </summary>
    public class MobilePushWorker : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MobilePushWorker> _log;
        private readonly WebPushClient _client = new();
        private int _ultimoCodigo;

        public MobilePushWorker(IConfiguration config, ILogger<MobilePushWorker> log)
        {
            _config = config;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var publica = _config["Mobile:VapidPublicKey"];
            var privada = _config["Mobile:VapidPrivateKey"];
            var assunto = _config["Mobile:VapidSubject"] ?? "https://help.evoluttech.com";

            if (string.IsNullOrWhiteSpace(publica) || string.IsNullOrWhiteSpace(privada))
            {
                _log.LogWarning("[MobilePush] Chaves VAPID não configuradas (Mobile:VapidPublicKey / VapidPrivateKey). Push desligado.");
                return;
            }

            var vapid = new VapidDetails(assunto, publica, privada);
            var conn = _config.GetConnectionString("Connection")!;

            // Começa do "agora": não notifica mensagens antigas quando o HELP reinicia
            while (!stoppingToken.IsCancellationRequested && _ultimoCodigo == 0)
            {
                try { _ultimoCodigo = await MaxCodigoAsync(conn, stoppingToken); if (_ultimoCodigo == 0) _ultimoCodigo = -1; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "[MobilePush] Falha ao iniciar; tentando de novo em 10s.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await VerificarAsync(conn, vapid, stoppingToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[MobilePush] Erro na verificação.");
                }
            }
        }

        private static async Task<int> MaxCodigoAsync(string conn, CancellationToken ct)
        {
            using var con = new SqlConnection(conn);
            await con.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT ISNULL(MAX(Codigo), 0) FROM TicketChamadoD", con);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        private record MensagemNova(int Codigo, int CodTicket, int CodEmp, string Responsavel, string Nome, string Texto, string Tipo);
        private record Inscricao(int Codigo, string Endpoint, string P256dh, string Auth);

        private async Task VerificarAsync(string conn, VapidDetails vapid, CancellationToken ct)
        {
            // Pega o teto ANTES de ler: o que entrar depois fica para a próxima rodada (não se perde nada)
            var max = await MaxCodigoAsync(conn, ct);
            if (max <= _ultimoCodigo) return;

            var novas = new List<MensagemNova>();

            using (var con = new SqlConnection(conn))
            {
                await con.OpenAsync(ct);

                using var cmd = new SqlCommand($@"
SELECT TOP 200
    D.Codigo,
    D.CodTicketChamadoC,
    D.CodEmp,
    LTRIM(RTRIM(ISNULL(T.Usuario, '')))                                       AS Responsavel,
    ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)), ''), ISNULL(C.Nome, ''))           AS Nome,
    ISNULL(T.TelefoneWhatsApp, '')                                            AS Telefone,
    LEFT(ISNULL(D.Anotacao, ''), 300)                                         AS Texto,
    CASE WHEN D.Audio IS NOT NULL THEN 'audio'
         WHEN D.Video IS NOT NULL THEN 'video'
         WHEN D.Imagem IS NOT NULL OR ISNULL(D.CaminhoImagem, '') <> '' THEN 'imagem'
         ELSE 'texto' END                                                     AS Tipo
FROM TicketChamadoD D
JOIN TicketChamadoC T ON T.Codigo = D.CodTicketChamadoC AND T.CodEmp = D.CodEmp
LEFT JOIN Cliente C   ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
WHERE D.Codigo > @Ultimo
  AND D.Codigo <= @Max
  AND ISNULL(D.EnvioCliente, 'N') = 'S'
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND ISNULL(D.StatusWhatsApp, '') <> 'ia_processing'
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
  AND ISNULL(T.CodSetor, 1) = 1   -- só Suporte (igual à lista do app)
ORDER BY D.Codigo", con);
                cmd.Parameters.AddWithValue("@Ultimo", _ultimoCodigo);
                cmd.Parameters.AddWithValue("@Max", max);

                using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var nome = rd["Nome"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(nome))
                        nome = MobileRepository.FormatarTelefone(rd["Telefone"]?.ToString() ?? "");

                    novas.Add(new MensagemNova(
                        rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2),
                        rd["Responsavel"]?.ToString() ?? "",
                        nome,
                        rd["Texto"]?.ToString() ?? "",
                        rd["Tipo"]?.ToString() ?? "texto"));
                }
            }

            // Avança a marca até o teto (pula as mensagens nossas). Se bateu o limite de 200, continua de onde parou.
            _ultimoCodigo = novas.Count >= 200 ? novas.Max(n => n.Codigo) : max;

            if (novas.Count == 0) return;

            // Agrupa por ticket: várias mensagens seguidas viram uma notificação só
            foreach (var grupo in novas.GroupBy(n => (n.CodEmp, n.CodTicket)))
            {
                var ultima = grupo.Last();
                var qtd = grupo.Count();

                var corpo = ultima.Tipo switch
                {
                    "audio" => "🎤 Áudio",
                    "video" => "🎥 Vídeo",
                    "imagem" => string.IsNullOrWhiteSpace(ultima.Texto) ? "📷 Imagem" : "📷 " + ultima.Texto,
                    _ => ultima.Texto
                };
                if (qtd > 1) corpo = $"({qtd} mensagens) {corpo}";
                if (corpo.Length > 180) corpo = corpo[..180] + "…";

                var payload = JsonSerializer.Serialize(new
                {
                    titulo = ultima.Nome,
                    corpo,
                    ticket = ultima.CodTicket,
                    url = $"ticket/{ultima.CodTicket}",
                    tag = $"ticket-{ultima.CodTicket}"
                });

                var inscricoes = await InscricoesAsync(conn, ultima.CodEmp, ultima.Responsavel, ct);
                foreach (var insc in inscricoes)
                    await EnviarAsync(conn, insc, payload, vapid, ct);
            }
        }

        private static async Task<List<Inscricao>> InscricoesAsync(string conn, int codEmp, string responsavel, CancellationToken ct)
        {
            var lista = new List<Inscricao>();
            using var con = new SqlConnection(conn);
            await con.OpenAsync(ct);

            // Igual ao HELP desktop: ticket sem responsável (fila) → todos da empresa; senão só o responsável
            using var cmd = new SqlCommand($@"
SELECT P.Codigo, P.Endpoint, P.P256dh, P.Auth
FROM MobilePushInscricao P
JOIN Usuario U
  ON U.CodEmp = P.CodEmp
 AND UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(P.Usuario)))
 AND ISNULL(U.Inativo, 'N') = 'N'
 AND ISNULL(U.Help, 'N') = 'S'
WHERE P.CodEmp = @CodEmp
  AND (
        UPPER(@Responsavel) IN ({MobileRepository.FilaSql})
        OR UPPER(LTRIM(RTRIM(P.Usuario))) = UPPER(@Responsavel)
      )", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Responsavel", (responsavel ?? "").Trim());

            using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                lista.Add(new Inscricao(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3)));

            return lista;
        }

        private async Task EnviarAsync(string conn, Inscricao insc, string payload, VapidDetails vapid, CancellationToken ct)
        {
            try
            {
                var sub = new PushSubscription(insc.Endpoint, insc.P256dh, insc.Auth);
                await _client.SendNotificationAsync(sub, payload, vapid);
                await ExecutarAsync(conn,
                    "UPDATE MobilePushInscricao SET DataHoraUltimoEnvio = GETDATE(), Falhas = 0 WHERE Codigo = @Cod",
                    insc.Codigo, ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // Celular desinstalou / revogou permissão: remove a inscrição
                await ExecutarAsync(conn, "DELETE FROM MobilePushInscricao WHERE Codigo = @Cod", insc.Codigo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("[MobilePush] Falha ao enviar para inscrição {Cod}: {Erro}", insc.Codigo, ex.Message);
                await ExecutarAsync(conn,
                    "UPDATE MobilePushInscricao SET Falhas = ISNULL(Falhas, 0) + 1 WHERE Codigo = @Cod; " +
                    "DELETE FROM MobilePushInscricao WHERE Codigo = @Cod AND Falhas >= 20",
                    insc.Codigo, ct);
            }
        }

        private static async Task ExecutarAsync(string conn, string sql, int codigo, CancellationToken ct)
        {
            using var con = new SqlConnection(conn);
            await con.OpenAsync(ct);
            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codigo);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
