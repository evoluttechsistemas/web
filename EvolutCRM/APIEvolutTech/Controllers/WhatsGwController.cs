using EvolutCRM.Helpers;
using EvolutCRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.APIEvolutTech.Controllers
{
    [ApiController]
    [Route("api/whatsgw")]
    public class WhatsGwController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly HealthMonitorService _monitor;

        public WhatsGwController(IConfiguration config, HealthMonitorService monitor)
        {
            _config = config;
            _monitor = monitor;
        }

        private SqlConnection GetConn()
        {
            return new SqlConnection(
                _config.GetConnectionString("Connection")
            );
        }

        private async Task<int> ObterCodEmpPorInstanciaAsync(int codInstancia)
        {
            if (codInstancia <= 0)
                return 2;

            try
            {
                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
SELECT TOP 1 CodEmp
FROM WhatsAppInstancia
WHERE Codigo = @Codigo", conn);

                cmd.Parameters.AddWithValue("@Codigo", codInstancia);

                var result = await cmd.ExecuteScalarAsync();
                var codEmp = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);

                return codEmp > 0 ? codEmp : 2;
            }
            catch
            {
                return 2;
            }
        }

        [HttpPost("nova-mensagem")]
        public async Task<IActionResult> NovaMensagemRecebida()
        {
            SqlTransaction transaction = null;
            string contactPhoneNumber = "";
            string contactName = "";
            string waid = "";
            string messageType = "";
            string chatType = "";
            int codInstancia = 0;
            bool ehComercial = false;

            try
            {
                var form = await Request.ReadFormAsync();

                string evento = form["event"].ToString().Trim();
                string apiKey = form["apikey"].ToString().Trim();
                string phoneNumber = form["phone_number"].ToString().Trim();
                contactPhoneNumber = form["contact_phone_number"].ToString().Trim();
                contactName = form["contact_name"].ToString().Trim();
                chatType = form["chat_type"].ToString().Trim();
                string messageId = form["message_id"].ToString().Trim();
                messageType = form["message_type"].ToString().Trim();
                string messageState = form["message_state"].ToString().Trim();
                bool ehEnviadoPeloCelular = messageState.Equals("sent_external", StringComparison.OrdinalIgnoreCase);
                string messageBody = form["message_body"].ToString();
                string messageCaption = form["message_caption"].ToString();
                string groupId = form["group_id"].ToString().Trim();
                waid = form["waid"].ToString().Trim();
                string contextWaid = form["context_waid"].ToString().Trim();
                string contextQuotedTexto = form["context_quoted_texto"].ToString().Trim();
                string messageBodyMimeType = form["message_body_mimetype"].ToString().Trim();
                string messageBodyFileName = form["message_body_filename"].ToString().Trim();
                string messageCustomId = form["message_custom_id"].ToString().Trim();
                string codInstanciaStr = form["cod_instancia"].ToString().Trim();
                int.TryParse(codInstanciaStr, out codInstancia);
                int codEmp = await ObterCodEmpPorInstanciaAsync(codInstancia);

                // ── LOG 1: entrada da requisição ────────────────────────────
                _monitor.Log(LogCategory.Baileys, LogSeverity.Info,
                    $"[WEBHOOK] Entrada — evento={evento} tel={contactPhoneNumber} nome='{contactName}'",
                    $"waid={waid} inst={codInstancia} codEmp={codEmp} type={messageType} state={messageState} " +
                    $"chat={chatType} mime={messageBodyMimeType} file={messageBodyFileName} " +
                    $"bodyLen={messageBody?.Length} caption='{messageCaption}'");

                ehComercial = await VerificarSeComercialAsync(codInstancia);

                int contextType = 0;
                int.TryParse(form["context_type"], out contextType);

                long receivedTimeUnix = 0;
                long.TryParse(form["received_time"], out receivedTimeUnix);

                DateTime? receivedTime = null;
                if (receivedTimeUnix > 0)
                {
                    receivedTime = DateTimeOffset
                        .FromUnixTimeSeconds(receivedTimeUnix)
                        .LocalDateTime;
                }

                bool ehImagem = messageType.Equals("image", StringComparison.OrdinalIgnoreCase)
                             || messageType.Equals("sticker", StringComparison.OrdinalIgnoreCase)
                             || messageBodyMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

                bool ehAudio = messageType.Equals("ptt", StringComparison.OrdinalIgnoreCase)
                             || messageType.Equals("audio", StringComparison.OrdinalIgnoreCase)
                             || messageBodyMimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

                bool ehVideo = messageType.Equals("video", StringComparison.OrdinalIgnoreCase)
                             || messageBodyMimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

                bool ehDocumento = messageType.Equals("document", StringComparison.OrdinalIgnoreCase)
                             || messageBodyMimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                             || messageBodyMimeType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                             || (!string.IsNullOrWhiteSpace(messageBodyFileName)
                                 && !ehImagem && !ehAudio && !ehVideo
                                 && messageBody?.Length > 100);

                bool ehMidia = ehImagem || ehAudio || ehVideo || ehDocumento;

                byte[]? imagemBytes = null;
                byte[]? audioBytes = null;
                byte[]? videoBytes = null;

                if (ehMidia && !string.IsNullOrWhiteSpace(messageBody))
                {
                    try
                    {
                        byte[] midiaBytes = Convert.FromBase64String(messageBody);
                        if (ehImagem) imagemBytes = midiaBytes;
                        if (ehAudio) audioBytes = midiaBytes;
                        if (ehVideo) videoBytes = midiaBytes;
                        if (ehDocumento) imagemBytes = midiaBytes;

                        _monitor.Log(LogCategory.Baileys, LogSeverity.Info,
                            $"[WEBHOOK] Mídia decodificada OK — {messageType} {midiaBytes.Length} bytes",
                            $"waid={waid} file={messageBodyFileName} mime={messageBodyMimeType}");
                    }
                    catch (Exception exBase64)
                    {
                        ehMidia = false; ehImagem = false; ehAudio = false;
                        ehVideo = false; ehDocumento = false;

                        _monitor.Log(LogCategory.Baileys, LogSeverity.Warning,
                            $"[WEBHOOK] Falha ao decodificar base64 da mídia — {messageType}",
                            $"waid={waid} mime={messageBodyMimeType} erro={exBase64.Message}");
                    }
                }

                string anotacaoFinal = ehMidia
                    ? (string.IsNullOrWhiteSpace(messageCaption) ? "" : messageCaption)
                    : messageBody;

                using var conn = GetConn();
                await conn.OpenAsync();
                transaction = conn.BeginTransaction();
                conn.InfoMessage += (s, e) => { };

                // ── EVENTO STATUS ────────────────────────────────────────────
                if (evento.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    await AtualizarStatusMensagemWhatsApp(
                        conn, transaction, waid, messageCustomId, messageId, messageState, codEmp);
                    transaction.Commit();
                    return Ok(new { status = "OK", message = "Status atualizado com sucesso." });
                }

                // ── Verifica duplicidade ─────────────────────────────────────
                using (var cmdExiste = new SqlCommand(@"
SELECT COUNT(1)
FROM WhatsAppMensagemRecebida
WHERE Waid = @Waid", conn, transaction))
                {
                    cmdExiste.Parameters.Add("@Waid", SqlDbType.VarChar).Value = waid ?? "";
                    int existe = Convert.ToInt32(await cmdExiste.ExecuteScalarAsync());

                    if (existe > 0)
                    {
                        transaction.Rollback();
                        _monitor.Log(LogCategory.Baileys, LogSeverity.Info,
                            $"[WEBHOOK] Msg duplicada ignorada — waid={waid}");
                        return Ok(new { status = "OK", message = "Mensagem já recebida anteriormente." });
                    }
                }

                // ── Grava log na tabela base ─────────────────────────────────
                string messageBodyLog = ehMidia
                    ? $"[{messageType.ToUpper()}] {messageBodyFileName} ({messageBodyMimeType})"
                    : messageBody;

                using (var cmd = new SqlCommand(@"
INSERT INTO WhatsAppMensagemRecebida
(
    Evento, ApiKey, PhoneNumber, ContactPhoneNumber, ContactName, ChatType,
    MessageId, MessageType, MessageState, MessageBody, GroupId, Waid,
    ContextType, ContextWaid, ReceivedTimeUnix, ReceivedTime, Processado,
    MessageBodyMimeType, MessageBodyFileName, MessageCustomId
)
VALUES
(
    @Evento, @ApiKey, @PhoneNumber, @ContactPhoneNumber, @ContactName, @ChatType,
    @MessageId, @MessageType, @MessageState, @MessageBody, @GroupId, @Waid,
    @ContextType, @ContextWaid, @ReceivedTimeUnix, @ReceivedTime, 'N',
    @MessageBodyMimeType, @MessageBodyFileName, @MessageCustomId
)", conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@Evento", evento ?? "");
                    cmd.Parameters.AddWithValue("@ApiKey", apiKey ?? "");
                    cmd.Parameters.AddWithValue("@PhoneNumber", ExtrairSoDigitos(phoneNumber, 20));
                    cmd.Parameters.AddWithValue("@ContactPhoneNumber", ExtrairSoDigitos(contactPhoneNumber, 20));
                    cmd.Parameters.AddWithValue("@ContactName", contactName ?? "");
                    cmd.Parameters.AddWithValue("@ChatType", chatType ?? "");
                    cmd.Parameters.AddWithValue("@MessageId", messageId ?? "");
                    cmd.Parameters.AddWithValue("@MessageType", messageType ?? "");
                    cmd.Parameters.AddWithValue("@MessageState", messageState ?? "");
                    cmd.Parameters.Add("@MessageBody", SqlDbType.NVarChar, -1).Value = messageBodyLog ?? "";
                    cmd.Parameters.AddWithValue("@GroupId", groupId ?? "");
                    cmd.Parameters.AddWithValue("@Waid", waid ?? "");
                    cmd.Parameters.AddWithValue("@ContextType", contextType);
                    cmd.Parameters.AddWithValue("@ContextWaid", contextWaid ?? "");
                    cmd.Parameters.AddWithValue("@ReceivedTimeUnix", receivedTimeUnix);
                    cmd.Parameters.Add("@ReceivedTime", SqlDbType.DateTime).Value =
                        receivedTime.HasValue ? (object)receivedTime.Value : DBNull.Value;
                    cmd.Parameters.AddWithValue("@MessageBodyMimeType", messageBodyMimeType ?? "");
                    cmd.Parameters.AddWithValue("@MessageBodyFileName", messageBodyFileName ?? "");
                    cmd.Parameters.AddWithValue("@MessageCustomId", messageCustomId ?? "");

                    await cmd.ExecuteNonQueryAsync();
                }

                // ── LOG 2: gravou na tabela base ─────────────────────────────
                _monitor.Log(LogCategory.Baileys, LogSeverity.Info,
                    $"[WEBHOOK] Gravado em WhatsAppMensagemRecebida — waid={waid}");

                // ── Distribui para Ticket / CRM ──────────────────────────────
                if (chatType.Equals("group", StringComparison.OrdinalIgnoreCase))
                {
                    _monitor.Log(LogCategory.Baileys, LogSeverity.Info,
                        $"[WEBHOOK] Mensagem de grupo ignorada — groupId={groupId}",
                        $"tel={contactPhoneNumber} waid={waid}");
                }
                else if (string.IsNullOrWhiteSpace(contactPhoneNumber))
                {
                    // ── LOG 3: telefone vazio — causa mais comum de ticket não ser criado ──
                    _monitor.Log(LogCategory.Baileys, LogSeverity.Warning,
                        $"[WEBHOOK] contactPhoneNumber vazio — ticket/card NÃO criado",
                        $"waid={waid} inst={codInstancia} nome='{contactName}' event={evento} " +
                        $"phoneNumber={phoneNumber} messageState={messageState}");
                }
                else
                {
                    if (ehComercial)
                    {
                        // ── LOG 4a: fluxo comercial ──────────────────────────
                        _monitor.Log(LogCategory.CRM, LogSeverity.Info,
                            $"[WEBHOOK] Fluxo COMERCIAL — tel={contactPhoneNumber}",
                            $"enviadoCelular={ehEnviadoPeloCelular} waid={waid} inst={codInstancia}");

                        int codCrmc = ehEnviadoPeloCelular
                            ? await BuscarCardComercialExistenteAsync(conn, transaction, contactPhoneNumber, codEmp)
                            : await ObterOuCriarCardComercialAsync(conn, transaction, contactPhoneNumber, contactName, codInstancia, codEmp);

                        if (codCrmc > 0)
                        {
                            _monitor.Log(LogCategory.CRM, LogSeverity.Info,
                                $"[WEBHOOK] Card CRM obtido/criado — codCrmc={codCrmc}",
                                $"tel={contactPhoneNumber} waid={waid}");

                            await InserirMensagemCRMAnotacaoAsync(
                                conn, transaction, codCrmc,
                                anotacaoFinal, waid, messageId, messageCustomId, receivedTime,
                                contactName, contactPhoneNumber,
                                imagemBytes, audioBytes, videoBytes,
                                messageBodyMimeType, messageBodyFileName, codInstancia, codEmp,
                                ehEnviadoPeloCelular);
                        }
                        else
                        {
                            // ── LOG 4b: card não encontrado/criado ───────────
                            _monitor.Log(LogCategory.CRM, LogSeverity.Warning,
                                $"[WEBHOOK] Card CRM NÃO encontrado/criado — mensagem descartada",
                                $"tel={contactPhoneNumber} enviadoCelular={ehEnviadoPeloCelular} " +
                                $"waid={waid} inst={codInstancia}");
                        }
                    }
                    else
                    {
                        // ── LOG 5a: fluxo ticket ─────────────────────────────
                        _monitor.Log(LogCategory.Tickets, LogSeverity.Info,
                            $"[WEBHOOK] Fluxo TICKET — tel={contactPhoneNumber}",
                            $"enviadoCelular={ehEnviadoPeloCelular} waid={waid} inst={codInstancia}");

                        int codTicketChamadoC = ehEnviadoPeloCelular
                            ? await BuscarTicketExistenteAsync(conn, transaction, contactPhoneNumber, codEmp)
                            : await ObterOuCriarTicketAsync(conn, transaction, contactPhoneNumber, contactName, chatType, codInstancia, codEmp);

                        if (codTicketChamadoC > 0)
                        {
                            _monitor.Log(LogCategory.Tickets, LogSeverity.Info,
                                $"[WEBHOOK] Ticket obtido/criado — codTicket={codTicketChamadoC}",
                                $"tel={contactPhoneNumber} waid={waid}");

                            await InserirMensagemTicketDAsync(
                                conn, transaction, codTicketChamadoC,
                                anotacaoFinal, waid, messageId, messageCustomId, receivedTime,
                                contactName, contactPhoneNumber,
                                imagemBytes, audioBytes, videoBytes,
                                messageBodyMimeType, messageBodyFileName, codInstancia, codEmp,
                                contextWaid, contextQuotedTexto, ehDocumento, ehEnviadoPeloCelular);

                            if (!ehEnviadoPeloCelular && !ehMidia)
                            {
                                bool registrou = await TentarRegistrarNotaPesquisaAsync(
                                    conn, transaction, codTicketChamadoC, anotacaoFinal, codEmp);

                                if (registrou)
                                {
                                    _monitor.Log(LogCategory.Tickets, LogSeverity.Info,
                                        $"[WEBHOOK] Pesquisa de satisfação registrada — ticket={codTicketChamadoC}",
                                        $"nota='{anotacaoFinal}' tel={contactPhoneNumber}");

                                    using var cmdFecha = new SqlCommand(@"
UPDATE TicketChamadoC
SET CodSituacao = 3, Novo = 'N'
WHERE Codigo = @Cod AND CodEmp = @CodEmp", conn, transaction);
                                    cmdFecha.Parameters.AddWithValue("@Cod", codTicketChamadoC);
                                    cmdFecha.Parameters.AddWithValue("@CodEmp", codEmp);
                                    await cmdFecha.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        else
                        {
                            // ── LOG 5b: ticket não encontrado/criado — o bug que você quer pegar ──
                            _monitor.Log(LogCategory.Tickets, LogSeverity.Warning,
                                $"[WEBHOOK] Ticket NÃO encontrado/criado — mensagem descartada",
                                $"tel={contactPhoneNumber} enviadoCelular={ehEnviadoPeloCelular} " +
                                $"waid={waid} inst={codInstancia} nome='{contactName}' " +
                                $"chatType={chatType} ehMidia={ehMidia} type={messageType}");
                        }
                    }
                }

                transaction.Commit();

                // ── LOG 6: commit OK ─────────────────────────────────────────
                var categoria = ehComercial ? LogCategory.CRM : LogCategory.Tickets;
                var tipoMsg = ehMidia ? $"mídia ({messageType})" : "texto";
                _monitor.Log(categoria, LogSeverity.Info,
                    $"[WEBHOOK] Commit OK — msg recebida de {contactPhoneNumber} — {tipoMsg}",
                    $"waid={waid} inst={codInstancia} nome='{contactName}'");

                return Ok(new { status = "OK", message = "Mensagem recebida com sucesso." });
            }
            catch (Exception ex)
            {
                try { transaction?.Rollback(); } catch { }

                _monitor.LogException(LogCategory.Sistema, ex,
                    $"[WEBHOOK] EXCEÇÃO — tel={contactPhoneNumber} nome='{contactName}' " +
                    $"waid={waid} inst={codInstancia} ehComercial={ehComercial} type={messageType}");

                return StatusCode(500, new { status = "ERRO", detalhe = ex.Message });
            }
        }

        [HttpPost("mensagem-lida")]
        public async Task<IActionResult> MensagemLida([FromBody] MensagemLidaDto dto)
        {
            try
            {
                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
            UPDATE TicketChamadoD
            SET StatusWhatsApp = 'read',
                DataHoraStatusWhatsApp = GETDATE()
            WHERE WaidWhatsApp = @Waid
              AND ISNULL(StatusWhatsApp,'') NOT IN ('read','deleted')", conn);

                cmd.Parameters.AddWithValue("@Waid", dto.Waid ?? "");
                int afetados = await cmd.ExecuteNonQueryAsync();                        // ← MUDOU: captura o retorno

                if (afetados > 0)                                                       // ← ADD
                    _monitor.Log(LogCategory.Tickets, LogSeverity.Info,                // ← ADD
                        $"Msg marcada como lida — waid={dto.Waid}");                   // ← ADD

                return Ok(new { status = "OK" });
            }
            catch (Exception ex)
            {
                _monitor.LogException(LogCategory.Tickets, ex,                         // ← ADD
                    $"MensagemLida waid={dto.Waid}");                                  // ← ADD
                return StatusCode(500, new { status = "ERRO", detalhe = ex.Message });
            }
        }

        public class MensagemLidaDto
        {
            public string Waid { get; set; } = "";
            public int CodInstancia { get; set; }
        }

        // =========================================================
        // BUSCA TICKET DE HOJE OU CRIA UM NOVO (sem alteração)
        // =========================================================
        private async Task<int> ObterOuCriarTicketAsync(
    SqlConnection conn,
    SqlTransaction transaction,
    string telefoneWhatsAppBruto,
    string contactName,
    string chatType,
    int codInstancia,
    int codEmp)
        {
            // Canônico p/ GRAVAR e todas as variantes p/ BUSCAR
            string telefoneWhatsApp = new string((telefoneWhatsAppBruto ?? "").Where(char.IsDigit).ToArray());
            var variantes = TelefoneBR.GerarVariantes(telefoneWhatsAppBruto);
            if (variantes.Count == 0) variantes = new List<string> { telefoneWhatsApp };

            var pTel = variantes.Select((_, i) => "@T" + i).ToList();
            string inTel = string.Join(",", pTel);

            // Limpa formatação da coluna p/ comparar só dígitos (cobre dados antigos)
            const string LIMPA_TEL =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(TelefoneWhatsApp,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

            int? codigoTicketExistente = null;
            int situacaoAtual = 0;

            using (var cmdBusca = new SqlCommand($@"
        SELECT TOP 1 Codigo, ISNULL(CodSituacao, 1) AS CodSituacao
        FROM TicketChamadoC WITH (UPDLOCK, ROWLOCK)
        WHERE CodEmp = @CodEmp
          AND {LIMPA_TEL} IN ({inTel})
          AND (
                ISNULL(CodSituacao, 1) <> 3                          -- aberto: reaproveita
                OR DataHoraUltimaGravacao >= DATEADD(HOUR, -24, GETDATE())  -- finalizado <24h: reabre
              )
        ORDER BY Codigo DESC", conn, transaction))
            {
                cmdBusca.CommandTimeout = 10;
                cmdBusca.Parameters.AddWithValue("@CodEmp", codEmp);
                for (int i = 0; i < variantes.Count; i++)
                    cmdBusca.Parameters.AddWithValue(pTel[i], variantes[i]);

                using var reader = await cmdBusca.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    codigoTicketExistente = reader.GetInt32(0);
                    situacaoAtual = reader.GetInt32(1);
                }
            }

            if (codigoTicketExistente.HasValue)
            {
                if (situacaoAtual == 3) // finalizado mas dentro de 24h → reabre
                {
                    using var cmdReabre = new SqlCommand(@"
                UPDATE TicketChamadoC
                SET CodSituacao = 1, Usuario = 'NOVO', Novo = 'S',
                    CodInstanciaWhatsApp = CASE
                        WHEN @CodInstancia > 0 THEN @CodInstancia
                        ELSE CodInstanciaWhatsApp
                    END
                WHERE Codigo = @Codigo AND CodEmp = @CodEmp", conn, transaction);
                    cmdReabre.Parameters.AddWithValue("@Codigo", codigoTicketExistente.Value);
                    cmdReabre.Parameters.AddWithValue("@CodEmp", codEmp);
                    cmdReabre.Parameters.AddWithValue("@CodInstancia", codInstancia);
                    await cmdReabre.ExecuteNonQueryAsync();
                }
                else
                {
                    // Ticket aberto: fixa a instância no cabeçalho se ainda não tiver
                    using var cmdFixaInst = new SqlCommand(@"
                UPDATE TicketChamadoC
                SET CodInstanciaWhatsApp = @CodInstancia
                WHERE Codigo = @Codigo
                  AND CodEmp = @CodEmp
                  AND CodInstanciaWhatsApp IS NULL
                  AND @CodInstancia > 0", conn, transaction);
                    cmdFixaInst.Parameters.AddWithValue("@Codigo", codigoTicketExistente.Value);
                    cmdFixaInst.Parameters.AddWithValue("@CodEmp", codEmp);
                    cmdFixaInst.Parameters.AddWithValue("@CodInstancia", codInstancia);
                    await cmdFixaInst.ExecuteNonQueryAsync();
                }

                return codigoTicketExistente.Value;
            }

            // ── Cliente: reconhece pelo número em TODAS as variantes (Celular OU Telefone) ──
            int codClienteEncontrado = 0;
            string nomeClienteEncontrado = "";

            var pCli = variantes.Select((_, i) => "@C" + i).ToList();
            string inCli = string.Join(",", pCli);

            const string LIMPA_FMT =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL({0},''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

            string limpaCelular = string.Format(LIMPA_FMT, "Celular");
            string limpaTelefone = string.Format(LIMPA_FMT, "Telefone");

            using (var cmdCliente = new SqlCommand($@"
        SELECT TOP 1 Codigo, ISNULL(Nome,'')
        FROM Cliente
        WHERE CodEmp = @CodEmp
          AND (
               {limpaCelular}  IN ({inCli})
            OR {limpaTelefone} IN ({inCli})
          )
        ORDER BY Codigo DESC", conn, transaction))
            {
                cmdCliente.Parameters.AddWithValue("@CodEmp", codEmp);
                for (int i = 0; i < variantes.Count; i++)
                    cmdCliente.Parameters.AddWithValue(pCli[i], variantes[i]);

                using var readerCliente = await cmdCliente.ExecuteReaderAsync();
                if (await readerCliente.ReadAsync())
                {
                    codClienteEncontrado = readerCliente.GetInt32(0);
                    nomeClienteEncontrado = readerCliente.GetString(1);
                }
            }

            string nomeParaAssunto = !string.IsNullOrWhiteSpace(nomeClienteEncontrado)
                ? nomeClienteEncontrado
                : (contactName ?? "");

            using (var cmdInsere = new SqlCommand(@"
        DECLARE @NovoTicket TABLE (Codigo INT);

        INSERT INTO TicketChamadoC
        (
            CodEmp, Status, CodSituacao, CodSetor, DataAbertura, DataHoraAbertura, Usuario,
            CodCliente, Assunto, UsuarioAbertura,
            TelefoneWhatsApp, ChatTipo, Novo, CodInstanciaWhatsApp
        )
        OUTPUT INSERTED.Codigo INTO @NovoTicket
        VALUES
        (
            @CodEmp, 1, 1, 1, CAST(GETDATE() AS DATE), GETDATE(), 'NOVO',
            @CodCliente, @Assunto, @UsuarioAbertura,
            @TelefoneWhatsApp, @ChatTipo, 'S', @CodInstancia
        );

        SELECT Codigo FROM @NovoTicket;", conn, transaction))
            {
                // Truncagem defensiva — espelha exatamente o schema de TicketChamadoC
                string assuntoSeguro = (nomeParaAssunto ?? "").Trim();
                string usuarioAberturaSeguro = assuntoSeguro;
                string telefoneSeguro = (telefoneWhatsApp ?? "").Trim();
                string chatTipoSeguro = (chatType ?? "user").Trim();

                if (assuntoSeguro.Length > 150) assuntoSeguro = assuntoSeguro[..150];
                if (usuarioAberturaSeguro.Length > 50) usuarioAberturaSeguro = usuarioAberturaSeguro[..50];
                if (telefoneSeguro.Length > 30) telefoneSeguro = telefoneSeguro[..30];
                if (chatTipoSeguro.Length > 20) chatTipoSeguro = chatTipoSeguro[..20];

                cmdInsere.Parameters.AddWithValue("@CodEmp", codEmp);
                cmdInsere.Parameters.Add("@CodCliente", SqlDbType.Int).Value =
                    codClienteEncontrado > 0 ? codClienteEncontrado : (object)DBNull.Value;
                cmdInsere.Parameters.AddWithValue("@Assunto", assuntoSeguro);
                cmdInsere.Parameters.AddWithValue("@UsuarioAbertura", usuarioAberturaSeguro);
                cmdInsere.Parameters.AddWithValue("@TelefoneWhatsApp", telefoneSeguro);
                cmdInsere.Parameters.AddWithValue("@ChatTipo", chatTipoSeguro);
                cmdInsere.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                    codInstancia > 0 ? codInstancia : (object)DBNull.Value;

                var novoCodigo = await cmdInsere.ExecuteScalarAsync();
                return Convert.ToInt32(novoCodigo);
            }
        }

        // =========================================================
        // INSERE MENSAGEM EM TicketChamadoD
        // Agora aceita bytes de imagem / áudio / vídeo e grava nas
        // colunas corretas para o HELP exibir automaticamente.
        // =========================================================
        private async Task InserirMensagemTicketDAsync(
    SqlConnection conn,
    SqlTransaction transaction,
    int codTicketChamadoC,
    string anotacao,
    string waid,
    string messageId,
    string messageCustomId,
    DateTime? dataHora,
    string contactName,
    string telefoneWhatsAppBruto,
    byte[]? imagemBytes = null,
    byte[]? audioBytes = null,
    byte[]? videoBytes = null,
    string? mimeType = null,
    string? fileName = null,
    int codInstancia = 0,
    int codEmp = 2,
    string contextWaid = "",
    string contextQuotedTexto = "",
    bool ehDocumento = false,
    bool ehEnviadoPeloCelular = false)
        {
            bool temImagem = imagemBytes != null && imagemBytes.Length > 0;
            bool temAudio = audioBytes != null && audioBytes.Length > 0;
            bool temVideo = videoBytes != null && videoBytes.Length > 0;

            // ── Resolve mensagem citada pelo WaId ────────────────────────
            int? codMensagemRespondida = null;
            string textoMensagemRespondida = "";
            string usuarioMensagemRespondida = "";
            string envioCliente = (ehEnviadoPeloCelular ||
    string.Equals(contactName?.Trim(), "WhatsApp", StringComparison.OrdinalIgnoreCase))
    ? "N" : "S";
            string usuarioMensagem = ehEnviadoPeloCelular
    ? "Por Whats"
    : (string.IsNullOrWhiteSpace(contactName) ? "CLIENTE" : contactName.Trim());

            if (usuarioMensagem.Length > 50)
                usuarioMensagem = usuarioMensagem.Substring(0, 50);

            // WhatsAppEnviado: já foi enviado, não precisa reenviar
            string whatsAppEnviado = ehEnviadoPeloCelular ? "S" : (string)null!; // NULL = aguarda envio
            string statusWhatsApp = ehEnviadoPeloCelular ? "sent_external" : "received";

            if (!string.IsNullOrWhiteSpace(contextWaid))
            {
                using var cmdQuoted = new SqlCommand(@"
            SELECT TOP 1 Codigo,
                   ISNULL(Anotacao, ''),
                   ISNULL(Usuario, '')
            FROM TicketChamadoD
            WHERE WaidWhatsApp = @ContextWaid
              AND CodTicketChamadoC = @CodTicket
              AND CodEmp = @CodEmp
            ORDER BY Codigo DESC", conn, transaction);

                cmdQuoted.Parameters.AddWithValue("@ContextWaid", contextWaid);
                cmdQuoted.Parameters.AddWithValue("@CodTicket", codTicketChamadoC);
                cmdQuoted.Parameters.AddWithValue("@CodEmp", codEmp);

                using var rdQ = await cmdQuoted.ExecuteReaderAsync();
                if (await rdQ.ReadAsync())
                {
                    codMensagemRespondida = rdQ.GetInt32(0);
                    textoMensagemRespondida = rdQ.IsDBNull(1) ? "" : rdQ.GetString(1);
                    usuarioMensagemRespondida = rdQ.IsDBNull(2) ? "" : rdQ.GetString(2);
                }
                else
                {
                    // Mensagem citada não encontrada no banco (antiga ou de outra sessão)
                    // Usa o texto que veio do Node como fallback
                    textoMensagemRespondida = contextQuotedTexto ?? "";
                }
            }

            // O número que veio do webhook JÁ é o JID real do WhatsApp.
            // Normalizar aqui gera um número inexistente e as respostas não chegam.
            string telefoneWhatsApp = new string((telefoneWhatsAppBruto ?? "").Where(char.IsDigit).ToArray());

            using var cmd = new SqlCommand(@"
INSERT INTO TicketChamadoD
(
    CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
    TelefoneWhatsApp,
    WaidWhatsApp, MessageIdWhatsApp, MessageCustomIdWhatsApp,
    WhatsAppEnviado, EnvioCliente, StatusWhatsApp, DataHoraStatusWhatsApp, LidoSuporte,
    Imagem,      NomeImagem,
    Audio,       AudioMimeType,  AudioFileName,
    Video,       VideoMimeType,  VideoFileName,
    CodInstancia,
    CodMensagemRespondida, TextoMensagemRespondida, UsuarioMensagemRespondida
)
VALUES
(
    @CodEmp, @CodTicketChamadoC, @Anotacao, @DataHora, @Usuario,
    @TelefoneWhatsApp,
    @WaidWhatsApp, @MessageIdWhatsApp, @MessageCustomIdWhatsApp,
    @WhatsAppEnviado, @EnvioCliente, @StatusWhatsApp, GETDATE(), @LidoSuporte,
    @Imagem,     @NomeImagem,
    @Audio,      @AudioMimeType, @AudioFileName,
    @Video,      @VideoMimeType, @VideoFileName,
    @CodInstancia,
    @CodMensagemRespondida, @TextoMensagemRespondida, @UsuarioMensagemRespondida
)", conn, transaction);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@CodTicketChamadoC", codTicketChamadoC);
            cmd.Parameters.Add("@Anotacao", SqlDbType.NVarChar, -1).Value = anotacao ?? "";
            cmd.Parameters.AddWithValue("@DataHora", dataHora ?? DateTime.Now);
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 50).Value = usuarioMensagem;
            cmd.Parameters.AddWithValue("@WaidWhatsApp", waid ?? "");
            cmd.Parameters.AddWithValue("@MessageIdWhatsApp", messageId ?? "");
            cmd.Parameters.AddWithValue("@MessageCustomIdWhatsApp", messageCustomId ?? "");
            cmd.Parameters.Add("@TelefoneWhatsApp", SqlDbType.VarChar, 30).Value = telefoneWhatsApp ?? "";
            cmd.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                codInstancia > 0 ? codInstancia : (object)DBNull.Value;
            cmd.Parameters.Add("@WhatsAppEnviado", SqlDbType.Char, 1).Value =
    ehEnviadoPeloCelular ? "S" : (object)DBNull.Value;
            cmd.Parameters.Add("@EnvioCliente", SqlDbType.Char, 1).Value = envioCliente;
            cmd.Parameters.Add("@StatusWhatsApp", SqlDbType.VarChar, 30).Value = statusWhatsApp;
            cmd.Parameters.Add("@LidoSuporte", SqlDbType.Char, 1).Value =
    ehEnviadoPeloCelular ? "S" : "N";

            // Imagem
            cmd.Parameters.Add("@Imagem", SqlDbType.VarBinary, -1).Value =
                temImagem ? (object)imagemBytes! : DBNull.Value;

            string nomeImagemFinal = !string.IsNullOrWhiteSpace(fileName)
                ? fileName
                : (ehDocumento ? "documento.pdf" : "imagem-whatsapp.jpg");

            cmd.Parameters.Add("@NomeImagem", SqlDbType.NVarChar, 255).Value =
                temImagem ? (object)nomeImagemFinal : DBNull.Value;

            // Áudio
            cmd.Parameters.Add("@Audio", SqlDbType.VarBinary, -1).Value =
                temAudio ? (object)audioBytes! : DBNull.Value;
            cmd.Parameters.Add("@AudioMimeType", SqlDbType.VarChar, 200).Value =
                temAudio ? "audio/ogg; codecs=opus" : (object)DBNull.Value;
            cmd.Parameters.Add("@AudioFileName", SqlDbType.VarChar, 255).Value =
                temAudio ? "file.ogg" : (object)DBNull.Value;

            // Vídeo
            cmd.Parameters.Add("@Video", SqlDbType.VarBinary, -1).Value =
                temVideo ? (object)videoBytes! : DBNull.Value;
            cmd.Parameters.Add("@VideoMimeType", SqlDbType.VarChar, 200).Value =
                temVideo ? (object)(mimeType ?? "video/mp4") : (object)DBNull.Value;
            cmd.Parameters.Add("@VideoFileName", SqlDbType.VarChar, 255).Value =
                temVideo ? (object)(fileName ?? "video.mp4") : (object)DBNull.Value;

            // Mensagem citada
            cmd.Parameters.Add("@CodMensagemRespondida", SqlDbType.Int).Value =
                codMensagemRespondida.HasValue ? codMensagemRespondida.Value : (object)DBNull.Value;
            cmd.Parameters.Add("@TextoMensagemRespondida", SqlDbType.NVarChar, 500).Value =
                textoMensagemRespondida.Length > 500
                    ? textoMensagemRespondida.Substring(0, 500)
                    : textoMensagemRespondida;
            cmd.Parameters.Add("@UsuarioMensagemRespondida", SqlDbType.VarChar, 100).Value =
                usuarioMensagemRespondida.Length > 100
                    ? usuarioMensagemRespondida.Substring(0, 100)
                    : usuarioMensagemRespondida;

            // ── Depois ──
            await cmd.ExecuteNonQueryAsync();

            // Resposta enviada pelo próprio atendente via celular:
            // baixa o alerta "Novo" do ticket, já que ele respondeu por fora.
            if (ehEnviadoPeloCelular)
            {
                using var cmdBaixaNovo = new SqlCommand(@"
                    UPDATE TicketChamadoC
                    SET Novo = 'N',
                        DataHoraUltimaGravacao = GETDATE()
                    WHERE Codigo = @Cod AND CodEmp = @CodEmp", conn, transaction);
                cmdBaixaNovo.Parameters.AddWithValue("@Cod", codTicketChamadoC);
                cmdBaixaNovo.Parameters.AddWithValue("@CodEmp", codEmp);
                await cmdBaixaNovo.ExecuteNonQueryAsync();

                // Marca as mensagens pendentes do cliente como lidas pelo suporte
                using var cmdLido = new SqlCommand(@"
                    UPDATE TicketChamadoD
                    SET LidoSuporte = 'S'
                    WHERE CodTicketChamadoC = @Cod
                      AND CodEmp = @CodEmp
                      AND ISNULL(EnvioCliente,'N') = 'S'
                      AND ISNULL(LidoSuporte,'N') <> 'S'", conn, transaction);
                cmdLido.Parameters.AddWithValue("@Cod", codTicketChamadoC);
                cmdLido.Parameters.AddWithValue("@CodEmp", codEmp);
                await cmdLido.ExecuteNonQueryAsync();
            }
        }

        // =========================================================
        // ATUALIZA STATUS DA MENSAGEM (sem alteração)
        // =========================================================
        private async Task AtualizarStatusMensagemWhatsApp(
            SqlConnection conn,
            SqlTransaction transaction,
            string waid,
            string messageCustomId,
            string messageId,
            string messageState,
            int codEmp)
        {
            using var cmd = new SqlCommand(@"
UPDATE TicketChamadoD
SET
    StatusWhatsApp = @StatusWhatsApp,
    WaidWhatsApp = CASE
        WHEN ISNULL(@WaidWhatsApp, '') <> '' THEN @WaidWhatsApp
        ELSE WaidWhatsApp
    END,
    MessageIdWhatsApp = CASE
        WHEN ISNULL(@MessageIdWhatsApp, '') <> '' THEN @MessageIdWhatsApp
        ELSE MessageIdWhatsApp
    END,
    DataHoraStatusWhatsApp = GETDATE()
WHERE CodEmp = @CodEmp
  AND (
    (NULLIF(@MessageCustomIdWhatsApp, '') IS NOT NULL AND MessageCustomIdWhatsApp = @MessageCustomIdWhatsApp)
    OR (NULLIF(@WaidWhatsApp, '')        IS NOT NULL AND WaidWhatsApp        = @WaidWhatsApp)
    OR (NULLIF(@MessageIdWhatsApp, '')   IS NOT NULL AND MessageIdWhatsApp   = @MessageIdWhatsApp)
  )
", conn, transaction);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@StatusWhatsApp", messageState ?? "");
            cmd.Parameters.AddWithValue("@WaidWhatsApp", waid ?? "");
            cmd.Parameters.AddWithValue("@MessageCustomIdWhatsApp", messageCustomId ?? "");
            cmd.Parameters.AddWithValue("@MessageIdWhatsApp", messageId ?? "");

            await cmd.ExecuteNonQueryAsync();
        }

        // =========================================================
        // HELPERS (sem alteração)
        // =========================================================
        private string ExtrairSoDigitos(string valor, int tamanhoMaximo)
        {
            string digitos = new string((valor ?? "").Where(char.IsDigit).ToArray());
            if (digitos.Length > tamanhoMaximo)
                digitos = digitos.Substring(0, tamanhoMaximo);
            return digitos;
        }


        // =========================================================
        // BUSCA CARD COMERCIAL DE HOJE OU CRIA UM NOVO
        // Mesma lógica do ticket: reaproveita dentro do dia,
        // reabre se estiver finalizado, mantém o funil anterior
        // =========================================================
        private async Task<int> ObterOuCriarCardComercialAsync(
    SqlConnection conn,
    SqlTransaction transaction,
    string telefoneWhatsAppBruto,
    string contactName,
    int codInstancia,
    int codEmp)
        {
            string telefone = TelefoneBR.Normalizar(telefoneWhatsAppBruto); // canônico p/ gravar
            var variantes = TelefoneBR.GerarVariantes(telefoneWhatsAppBruto);
            if (variantes.Count == 0) variantes = new List<string> { telefone };

            var pTel = variantes.Select((_, i) => "@T" + i).ToList();
            string inTel = string.Join(",", pTel);

            const string LIMPA_WA =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(TelefoneWhatsApp,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";
            const string LIMPA_CEL =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Celular,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

            int? codCardExistente = null;
            string? statusAtual = null;

            using (var cmdBusca = new SqlCommand($@"
        SELECT TOP 1 Codigo, Status, Funil
        FROM CRMC WITH (UPDLOCK, ROWLOCK)
        WHERE CodEmp = @CodEmp
          AND ({LIMPA_WA} IN ({inTel}) OR {LIMPA_CEL} IN ({inTel}))
          AND (
                UPPER(ISNULL(Status,'')) NOT IN ('FECHADO','CONCLUIDO','PERDIDO')
                OR DataHoraUltimaGravacao >= DATEADD(HOUR, -24, GETDATE())
              )
        ORDER BY Codigo DESC", conn, transaction))
            {
                cmdBusca.CommandTimeout = 10;
                cmdBusca.Parameters.AddWithValue("@CodEmp", codEmp);
                for (int i = 0; i < variantes.Count; i++)
                    cmdBusca.Parameters.AddWithValue(pTel[i], variantes[i]);

                using var rd = await cmdBusca.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    codCardExistente = rd.GetInt32(0);
                    statusAtual = rd.IsDBNull(1) ? null : rd.GetString(1);
                }
            }

            if (codCardExistente.HasValue)
            {
                if (string.Equals(statusAtual, "FECHADO", StringComparison.OrdinalIgnoreCase))
                {
                    using var cmdReabre = new SqlCommand(@"
                UPDATE CRMC
                SET Status = 'ABERTO', UsuarioCard = 'NOVO', Novo = 'S',
                    DataHoraUltimaGravacao = GETDATE(), UsuarioUltimaGravacao = 'WHATSAPP',
                    CodInstanciaWhatsApp = CASE
                        WHEN @CodInstancia > 0 THEN @CodInstancia
                        ELSE CodInstanciaWhatsApp
                    END
                WHERE Codigo = @Codigo AND CodEmp = @CodEmp", conn, transaction);
                    cmdReabre.Parameters.AddWithValue("@Codigo", codCardExistente.Value);
                    cmdReabre.Parameters.AddWithValue("@CodEmp", codEmp);
                    cmdReabre.Parameters.AddWithValue("@CodInstancia", codInstancia);
                    await cmdReabre.ExecuteNonQueryAsync();
                }
                else
                {
                    using var cmdUpd = new SqlCommand(@"
                UPDATE CRMC
                SET Novo = 'S', DataHoraUltimaGravacao = GETDATE(), UsuarioUltimaGravacao = 'WHATSAPP',
                    CodInstanciaWhatsApp = CASE
                        WHEN CodInstanciaWhatsApp IS NULL AND @CodInstancia > 0 THEN @CodInstancia
                        ELSE CodInstanciaWhatsApp
                    END
                WHERE Codigo = @Codigo AND CodEmp = @CodEmp", conn, transaction);
                    cmdUpd.Parameters.AddWithValue("@Codigo", codCardExistente.Value);
                    cmdUpd.Parameters.AddWithValue("@CodEmp", codEmp);
                    cmdUpd.Parameters.AddWithValue("@CodInstancia", codInstancia);
                    await cmdUpd.ExecuteNonQueryAsync();
                }

                return codCardExistente.Value;
            }

            // Mantém o funil mais recente deste número (buscando por variantes)
            string funilParaUsar = "EP";
            using (var cmdFunil = new SqlCommand($@"
        SELECT TOP 1 Funil
        FROM CRMC
        WHERE CodEmp = @CodEmp
          AND {LIMPA_WA} IN ({inTel})
          AND Funil IS NOT NULL AND Funil <> ''
        ORDER BY Codigo DESC", conn, transaction))
            {
                cmdFunil.Parameters.AddWithValue("@CodEmp", codEmp);
                for (int i = 0; i < variantes.Count; i++)
                    cmdFunil.Parameters.AddWithValue(pTel[i], variantes[i]);

                var r = await cmdFunil.ExecuteScalarAsync();
                if (r != null && r != DBNull.Value) funilParaUsar = r.ToString()!;
            }

            // Cliente pelo número (todas as variantes)
            int codCliente = 0;
            string nomeCliente = contactName ?? "";
            using (var cmdCliente = new SqlCommand($@"
        SELECT TOP 1 Codigo, ISNULL(Apelido, Nome)
        FROM Cliente
        WHERE CodEmp = @CodEmp
          AND (
               {LIMPA_CEL} IN ({inTel})
            OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Telefone,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','') IN ({inTel})
          )
        ORDER BY Codigo DESC", conn, transaction))
            {
                cmdCliente.Parameters.AddWithValue("@CodEmp", codEmp);
                for (int i = 0; i < variantes.Count; i++)
                    cmdCliente.Parameters.AddWithValue(pTel[i], variantes[i]);

                using var rd = await cmdCliente.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    codCliente = rd.GetInt32(0);
                    nomeCliente = rd.IsDBNull(1) ? contactName ?? "" : rd.GetString(1);
                }
            }

            using var cmdInsere = new SqlCommand(@"
        DECLARE @NovoCard TABLE (Codigo INT);

        INSERT INTO CRMC
        (
            Descricao, DataHoraUltimaGravacao, UsuarioUltimaGravacao,
            DataCriacao, Funil, CodEmp, Status,
            CodCliente, NomeCliente, TelefoneWhatsApp, Novo,
            Whats, UsuarioCard, CodInstanciaWhatsApp
        )
        OUTPUT INSERTED.Codigo INTO @NovoCard
        VALUES
        (
            @Descricao, GETDATE(), 'WHATSAPP',
            CAST(GETDATE() AS DATE), @Funil, @CodEmp, 'ABERTO',
            @CodCliente, @NomeCliente, @Telefone, 'S',
            'S', 'NOVO', @CodInstancia
        );

        SELECT Codigo FROM @NovoCard;", conn, transaction);

            cmdInsere.Parameters.AddWithValue("@CodEmp", codEmp);
            cmdInsere.Parameters.AddWithValue("@Descricao", nomeCliente);
            cmdInsere.Parameters.AddWithValue("@Funil", funilParaUsar);
            cmdInsere.Parameters.Add("@CodCliente", SqlDbType.Int).Value =
                codCliente > 0 ? codCliente : (object)DBNull.Value;
            cmdInsere.Parameters.AddWithValue("@NomeCliente", nomeCliente);
            cmdInsere.Parameters.AddWithValue("@Telefone", telefone); // canônico
            cmdInsere.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                codInstancia > 0 ? codInstancia : (object)DBNull.Value;

            return Convert.ToInt32(await cmdInsere.ExecuteScalarAsync());
        }

        private async Task<bool> VerificarSeComercialAsync(int codInstancia)
        {
            if (codInstancia <= 0) return false;
            try
            {
                using var conn = GetConn();
                await conn.OpenAsync();
                using var cmd = new SqlCommand(@"
            SELECT COUNT(1) FROM WhatsAppInstancia
            WHERE Codigo = @Codigo
              AND (UPPER(Nome) LIKE '%COMERCIAL%'
                OR UPPER(Nome) LIKE '%CRMC%'
                OR UPPER(Nome) LIKE '%VENDA%')", conn);
                cmd.Parameters.AddWithValue("@Codigo", codInstancia);
                int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch { return false; }
        }

        // =========================================================
        // INSERE MENSAGEM EM CRMAnotacao
        // Mesma estrutura do InserirMensagemTicketDAsync
        // =========================================================
        private async Task InserirMensagemCRMAnotacaoAsync(
    SqlConnection conn,
    SqlTransaction transaction,
    int codCrmc,
    string anotacao,
    string waid,
    string messageId,
    string messageCustomId,
    DateTime? dataHora,
    string contactName,
    string telefoneWhatsAppBruto,
    byte[]? imagemBytes = null,
    byte[]? audioBytes = null,
    byte[]? videoBytes = null,
    string? mimeType = null,
    string? fileName = null,
    int codInstancia = 0,
    int codEmp = 2,
    bool ehEnviadoPeloCelular = false)
        {
            bool temImagem = imagemBytes != null && imagemBytes.Length > 0;
            bool temAudio = audioBytes != null && audioBytes.Length > 0;
            bool temVideo = videoBytes != null && videoBytes.Length > 0;

            string envioCliente = (ehEnviadoPeloCelular ||
    string.Equals(contactName?.Trim(), "WhatsApp", StringComparison.OrdinalIgnoreCase))
    ? "N" : "S";
            string statusWhatsApp = ehEnviadoPeloCelular ? "sent" : "received";

            string usuario = ehEnviadoPeloCelular
    ? "Por Whats"
    : (string.IsNullOrWhiteSpace(contactName) ? "CLIENTE" : contactName.Trim());
            if (usuario.Length > 100) usuario = usuario.Substring(0, 100);

            string telefone = TelefoneBR.Normalizar(telefoneWhatsAppBruto);

            using var cmd = new SqlCommand(@"
INSERT INTO CRMAnotacao
(
    CodEmp, CodCRMC, Anotacao, DataHora, Usuario,
    TelefoneWhatsApp,
    WaidWhatsApp, MessageIdWhatsApp, MessageCustomIdWhatsApp,
    WhatsAppEnviado, EnvioCliente, StatusWhatsApp, LidoSuporte,
    Imagem,    NomeImagem,
    Audio,     AudioMimeType, AudioFileName,
    CodInstancia
)
VALUES
(
    @CodEmp, @CodCRMC, @Anotacao, @DataHora, @Usuario,
    @TelefoneWhatsApp,
    @WaidWhatsApp, @MessageIdWhatsApp, @MessageCustomIdWhatsApp,
    @WhatsAppEnviado, @EnvioCliente, @StatusWhatsApp, @LidoSuporte,
    @Imagem,   @NomeImagem,
    @Audio,    @AudioMimeType, @AudioFileName,
    @CodInstancia
)", conn, transaction);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@CodCRMC", codCrmc);
            cmd.Parameters.Add("@Anotacao", SqlDbType.VarChar, 500).Value =
                (anotacao?.Length > 500 ? anotacao.Substring(0, 500) : anotacao) ?? "";
            cmd.Parameters.AddWithValue("@DataHora", dataHora ?? DateTime.Now);
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = usuario;
            cmd.Parameters.Add("@TelefoneWhatsApp", SqlDbType.VarChar, 30).Value = telefone;
            cmd.Parameters.AddWithValue("@WaidWhatsApp", waid ?? "");
            cmd.Parameters.AddWithValue("@MessageIdWhatsApp", messageId ?? "");
            cmd.Parameters.AddWithValue("@MessageCustomIdWhatsApp", messageCustomId ?? "");
            cmd.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                codInstancia > 0 ? codInstancia : (object)DBNull.Value;

            // Controle de envio
            cmd.Parameters.Add("@WhatsAppEnviado", SqlDbType.Char, 1).Value =
                ehEnviadoPeloCelular ? "S" : (object)DBNull.Value;
            cmd.Parameters.Add("@EnvioCliente", SqlDbType.Char, 1).Value = envioCliente;
            cmd.Parameters.Add("@StatusWhatsApp", SqlDbType.VarChar, 30).Value = statusWhatsApp;
            cmd.Parameters.Add("@LidoSuporte", SqlDbType.Char, 1).Value =
    ehEnviadoPeloCelular ? "S" : "N";

            // Imagem
            cmd.Parameters.Add("@Imagem", SqlDbType.VarBinary, -1).Value =
                temImagem ? (object)imagemBytes! : DBNull.Value;
            cmd.Parameters.Add("@NomeImagem", SqlDbType.VarChar, 255).Value =
                temImagem ? (object)(fileName ?? "imagem-whatsapp.jpg") : DBNull.Value;

            // Áudio
            cmd.Parameters.Add("@Audio", SqlDbType.VarBinary, -1).Value =
                temAudio ? (object)audioBytes! : DBNull.Value;
            cmd.Parameters.Add("@AudioMimeType", SqlDbType.VarChar, 200).Value =
                temAudio ? "audio/ogg; codecs=opus" : (object)DBNull.Value;
            cmd.Parameters.Add("@AudioFileName", SqlDbType.VarChar, 255).Value =
                temAudio ? "file.ogg" : (object)DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }


        /// <summary>
        /// Gera a variação do telefone com/sem o 9 extra após o DDD.
        /// Ex: 5544999990000 → 554499990000 (e vice-versa)
        /// </summary>
        private string GerarTelefoneAlternativo(string telefone)
        {
            if (string.IsNullOrWhiteSpace(telefone) || !telefone.StartsWith("55"))
                return telefone;

            string semDDI = telefone.Substring(2); // remove "55"
            if (semDDI.Length < 10) return telefone;

            string ddd = semDDI.Substring(0, 2);
            string numero = semDDI.Substring(2);

            if (numero.Length == 9 && numero.StartsWith("9"))
            {
                // Tem 9 extra → gera versão sem ele
                return "55" + ddd + numero.Substring(1);
            }
            else if (numero.Length == 8)
            {
                // Não tem 9 → gera versão com ele
                return "55" + ddd + "9" + numero;
            }

            return telefone;
        }

        private async Task<int> BuscarTicketExistenteAsync(
    SqlConnection conn, SqlTransaction transaction, string telefoneBruto, int codEmp)
        {
            var variantes = TelefoneBR.GerarVariantes(telefoneBruto);
            if (variantes.Count == 0) return 0;

            var pTel = variantes.Select((_, i) => "@T" + i).ToList();
            string inTel = string.Join(",", pTel);

            const string LIMPA_TEL =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(TelefoneWhatsApp,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

            using var cmd = new SqlCommand($@"
        SELECT TOP 1 Codigo
        FROM TicketChamadoC
        WHERE CodEmp = @CodEmp
          AND {LIMPA_TEL} IN ({inTel})
          AND (
                ISNULL(CodSituacao, 1) <> 3
                OR DataHoraUltimaGravacao >= DATEADD(HOUR, -24, GETDATE())
              )
        ORDER BY Codigo DESC", conn, transaction);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            for (int i = 0; i < variantes.Count; i++)
                cmd.Parameters.AddWithValue(pTel[i], variantes[i]);

            var r = await cmd.ExecuteScalarAsync();
            return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
        }

        private async Task<int> BuscarCardComercialExistenteAsync(
    SqlConnection conn, SqlTransaction transaction, string telefoneBruto, int codEmp)
        {
            var variantes = TelefoneBR.GerarVariantes(telefoneBruto);
            if (variantes.Count == 0) return 0;

            var pTel = variantes.Select((_, i) => "@T" + i).ToList();
            string inTel = string.Join(",", pTel);

            const string LIMPA_WA =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(TelefoneWhatsApp,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";
            const string LIMPA_CEL =
                "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Celular,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

            using var cmd = new SqlCommand($@"
        SELECT TOP 1 Codigo
        FROM CRMC
        WHERE CodEmp = @CodEmp
          AND ({LIMPA_WA} IN ({inTel}) OR {LIMPA_CEL} IN ({inTel}))
          AND UPPER(ISNULL(Status,'')) NOT IN ('FECHADO','CONCLUIDO','PERDIDO')
        ORDER BY Codigo DESC", conn, transaction);

            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            for (int i = 0; i < variantes.Count; i++)
                cmd.Parameters.AddWithValue(pTel[i], variantes[i]);

            var r = await cmd.ExecuteScalarAsync();
            return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
        }

        /// <summary>
        /// Se a mensagem do cliente for só um número de 0 a 5 e existir pesquisa
        /// pendente para o ticket, grava a nota. Retorna true se registrou.
        /// </summary>
        private async Task<bool> TentarRegistrarNotaPesquisaAsync(
            SqlConnection conn, SqlTransaction transaction, int codTicket, string texto, int codEmp)
        {
            if (string.IsNullOrWhiteSpace(texto)) return false;

            // Aceita "5", "5.", "nota 5", "⭐5" — mas NÃO "5 minutos"
            var limpo = new string(texto.Where(char.IsDigit).ToArray());
            if (limpo.Length != 1) return false;

            // Rejeita se houver outras palavras relevantes junto
            var soTexto = new string(texto.Where(c => char.IsLetter(c)).ToArray()).ToUpper();
            if (soTexto.Length > 0 && soTexto != "NOTA") return false;

            int nota = limpo[0] - '0';
            if (nota < 0 || nota > 5) return false;

            using var cmd = new SqlCommand(@"
        UPDATE TOP (1) TicketPesquisaSatisfacao
        SET Nota         = @Nota,
            Respondido   = 'S',
            DataResposta = GETDATE()
        WHERE CodTicketChamadoC = @CodTicket
          AND CodEmp = @CodEmp
          AND ISNULL(Respondido, 'N') <> 'S'
          AND DataEnvio >= DATEADD(HOUR, -48, GETDATE())", conn, transaction);

            cmd.Parameters.AddWithValue("@Nota", nota);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}
