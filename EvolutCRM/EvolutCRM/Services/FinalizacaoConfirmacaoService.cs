using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services;

/// <summary>
/// Aviso "pode finalizar o ticket?" da finalização automática.
/// Quando passa o tempo sem mensagem (do cliente ou do atendente), o FinalizacaoAutomaticaService NÃO finaliza:
/// grava uma pergunta aqui (dbo.TicketFinalizacaoPergunta) e o MainLayout abre um modal no meio da tela do
/// responsável, em qualquer tela do HELP, para ele decidir:
///  - Finalizar (com ou sem insight);
///  - Não finalizar: a contagem recomeça e, se o cliente continuar sem resposta, o aviso volta depois do mesmo tempo.
/// Mensagem nova no ticket (de qualquer lado) invalida a pergunta.
/// </summary>
public sealed class FinalizacaoConfirmacaoService
{
    private readonly string _cs;
    private readonly UserState _state;
    private readonly InsightAutomaticoService _insightAuto;
    private readonly ParametroDinamicoService _parametros;
    private readonly TreinamentoClienteService _treinos;

    public FinalizacaoConfirmacaoService(IConfiguration config, UserState state, InsightAutomaticoService insightAuto,
        ParametroDinamicoService parametros, TreinamentoClienteService treinos)
    {
        _cs = config.GetConnectionString("Connection")!;
        _state = state;
        _insightAuto = insightAuto;
        _parametros = parametros;
        _treinos = treinos;
    }

    /// <param name="Tipo">CLIENTE = cliente aguardando resposta (finaliza?); INSIGHT = finalizado automaticamente (gera insight?).</param>
    /// <param name="UltimaDoCliente">A última mensagem de verdade (sem nota interna e sem mensagem automática) é do cliente.</param>
    public sealed record Pergunta(int Ticket, string Cliente, string UltimaMensagem, DateTime UltimaInteracao, DateTime PerguntadoEm,
        string Tipo = "CLIENTE", bool UltimaDoCliente = true);

    private string Usuario => (_state.CurrentUser ?? "").Trim().ToUpperInvariant();

    // ───────────────────────────── Tabela ─────────────────────────────

    private static volatile bool _tabelaOk;

    internal static async Task GarantirTabelaAsync(SqlConnection conn, CancellationToken ct = default)
    {
        if (_tabelaOk) return;
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.TicketFinalizacaoPergunta', 'U') IS NULL
    CREATE TABLE dbo.TicketFinalizacaoPergunta
    (
        CodTicketChamadoC INT          NOT NULL CONSTRAINT PK_TicketFinalizacaoPergunta PRIMARY KEY,
        CodEmp            INT          NOT NULL,
        UltimaInteracao   DATETIME     NOT NULL,   -- a pergunta vale para este período parado
        Status            VARCHAR(10)  NOT NULL,   -- PENDENTE (modal na tela) / MANTIDO (não finalizou) / INSIGHT (finalizado: gera insight?)
        PerguntadoEm      DATETIME     NOT NULL,
        DecididoEm        DATETIME     NULL,
        DecididoPor       VARCHAR(100) NULL
    );", conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _tabelaOk = true;
    }

    // ───────────────────────────── Modal ─────────────────────────────

    /// <summary>Perguntas pendentes dos tickets em que o usuário logado é o responsável (mais antiga primeiro).</summary>
    public async Task<List<Pergunta>> ListarMinhasAsync()
    {
        var lista = new List<Pergunta>();
        if (string.IsNullOrWhiteSpace(Usuario) || _state.CurrentCompanyId <= 0) return lista;

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);

        await using var cmd = new SqlCommand(@"
SELECT TOP 10
    Q.CodTicketChamadoC,
    ISNULL(NULLIF(LTRIM(RTRIM(CL.Apelido)), ''), ISNULL(NULLIF(LTRIM(RTRIM(CL.Nome)), ''), ISNULL(C.Assunto, ''))),
    ISNULL(M.Texto, ''),
    Q.UltimaInteracao,
    Q.PerguntadoEm,
    CASE WHEN ISNULL(M.EnvioCliente, 'S') = 'S' THEN 1 ELSE 0 END
FROM dbo.TicketFinalizacaoPergunta Q
INNER JOIN TicketChamadoC C ON C.Codigo = Q.CodTicketChamadoC AND C.CodEmp = Q.CodEmp
LEFT JOIN Cliente CL ON CL.Codigo = C.CodCliente AND CL.CodEmp = C.CodEmp
-- Última mensagem de verdade (cliente ou atendente; sem notas internas e sem mensagens automáticas)
OUTER APPLY (SELECT TOP 1 LTRIM(RTRIM(ISNULL(NULLIF(D.Anotacao, ''), ISNULL(D.TranscricaoAudio, '')))) AS Texto,
                    ISNULL(D.EnvioCliente, 'N') AS EnvioCliente
             FROM TicketChamadoD D
             WHERE D.CodTicketChamadoC = C.Codigo AND D.CodEmp = C.CodEmp
               AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
               AND ISNULL(D.Interno, 'N') <> 'S'
               AND ISNULL(D.StatusWhatsApp, '') NOT IN ('ia_processing', 'saudacao_auto', 'aguarde_auto', 'interno')
               AND (ISNULL(D.EnvioCliente, 'N') = 'S'
                    OR UPPER(LTRIM(RTRIM(ISNULL(D.Usuario, '')))) NOT IN
                       ('', 'WHATSAPP', 'CLIENTE', 'BOT', 'IA', 'SISTEMA', 'NOVO', 'EVOLUTTECH', 'POR WHATS', 'PESQUISA', 'AUTOMATICO'))
             ORDER BY D.DataHora DESC, D.Codigo DESC) M
WHERE Q.Status = 'PENDENTE'
  AND Q.CodEmp = @e
  AND ISNULL(C.CodSituacao, 1) = 1
  AND ISNULL(C.CodSetor, 1) = 1
  AND UPPER(LTRIM(RTRIM(ISNULL(C.Usuario, '')))) = @u
  AND NOT EXISTS (SELECT 1 FROM TicketChamadoD D2
                  WHERE D2.CodTicketChamadoC = C.Codigo AND D2.CodEmp = C.CodEmp AND D2.DataHora > Q.UltimaInteracao)
ORDER BY Q.PerguntadoEm;", conn);
        cmd.Parameters.AddWithValue("@e", _state.CurrentCompanyId);
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Usuario;

        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
                lista.Add(new Pergunta(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetDateTime(3), rd.GetDateTime(4),
                    "CLIENTE", rd.GetInt32(5) == 1));
        }

        // Tickets finalizados automaticamente esperando a decisão do insight (depois dos de cliente aguardando)
        await using var cmdI = new SqlCommand(@"
SELECT TOP 10
    Q.CodTicketChamadoC,
    ISNULL(NULLIF(LTRIM(RTRIM(CL.Apelido)), ''), ISNULL(NULLIF(LTRIM(RTRIM(CL.Nome)), ''), ISNULL(C.Assunto, ''))),
    Q.UltimaInteracao,
    Q.PerguntadoEm
FROM dbo.TicketFinalizacaoPergunta Q
INNER JOIN TicketChamadoC C ON C.Codigo = Q.CodTicketChamadoC AND C.CodEmp = Q.CodEmp
LEFT JOIN Cliente CL ON CL.Codigo = C.CodCliente AND CL.CodEmp = C.CodEmp
WHERE Q.Status = 'INSIGHT'
  AND Q.CodEmp = @e
  AND ISNULL(C.CodSituacao, 1) = 3
  AND UPPER(LTRIM(RTRIM(ISNULL(C.Usuario, '')))) = @u
ORDER BY Q.PerguntadoEm;", conn);
        cmdI.Parameters.AddWithValue("@e", _state.CurrentCompanyId);
        cmdI.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Usuario;
        await using (var rd = await cmdI.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
                lista.Add(new Pergunta(rd.GetInt32(0), rd.GetString(1), "", rd.GetDateTime(2), rd.GetDateTime(3), "INSIGHT"));
        }

        return lista;
    }

    /// <summary>Resposta do aviso "ticket finalizado automaticamente — gerar insight?".</summary>
    public async Task ResponderInsightAsync(int ticket, bool gerar)
    {
        var emp = _state.CurrentCompanyId;

        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            await GarantirTabelaAsync(conn);
            await using var cmd = new SqlCommand(
                "DELETE FROM dbo.TicketFinalizacaoPergunta WHERE CodTicketChamadoC = @t AND CodEmp = @e AND Status = 'INSIGHT';", conn);
            cmd.Parameters.AddWithValue("@t", ticket);
            cmd.Parameters.AddWithValue("@e", emp);
            await cmd.ExecuteNonQueryAsync();
        }

        if (gerar)
            await _insightAuto.GerarPorDecisaoAsync(ticket, emp);
        else
            await _insightAuto.MarcarPuladoAsync(ticket);

        LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{ticket}: {Usuario} respondeu o aviso de insight ({(gerar ? "gerar" : "não gerar")}).");
    }

    /// <summary>"Não finalizar": a contagem recomeça agora (sem as pausas de antes).</summary>
    public async Task ManterAbertoAsync(int ticket)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelaAsync(conn);

        await using var cmd = new SqlCommand(@"
UPDATE dbo.TicketFinalizacaoPergunta
SET Status = 'MANTIDO', DecididoEm = GETDATE(), DecididoPor = @u
WHERE CodTicketChamadoC = @t AND CodEmp = @e;

IF OBJECT_ID('dbo.TicketFinalizacaoPausa', 'U') IS NOT NULL
    DELETE FROM dbo.TicketFinalizacaoPausa WHERE CodTicketChamadoC = @t;", conn);
        cmd.Parameters.AddWithValue("@t", ticket);
        cmd.Parameters.AddWithValue("@e", _state.CurrentCompanyId);
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Usuario;
        await cmd.ExecuteNonQueryAsync();

        LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{ticket}: {Usuario} decidiu não finalizar (cliente aguardando); contagem recomeça.");
    }

    /// <summary>
    /// Finaliza pelo aviso. Devolve null quando deu certo, ou o motivo de não ter finalizado
    /// (o cliente mandou mensagem nova enquanto o aviso estava aberto, ou o ticket já foi finalizado).
    /// Quando a pesquisa de satisfação vai sair, manda antes o mesmo aviso de encerramento da finalização
    /// automática, para o cliente entender por que recebeu a pesquisa.
    /// </summary>
    public async Task<string?> FinalizarAsync(int ticket, bool comInsight)
    {
        var emp = _state.CurrentCompanyId;
        var avisouCliente = false;

        await using (var conn = new SqlConnection(_cs))
        {
            await conn.OpenAsync();
            await GarantirTabelaAsync(conn);

            // Aviso de encerramento: só quando a pesquisa vai sair (mesma regra da finalização automática)
            string textoAviso = "", destino = "";
            var pesquisaLigada = await _parametros.BuscarBoolAsync(emp, FinalizacaoAutomaticaService.ParamPesquisa, "TICKET",
                FinalizacaoAutomaticaService.DescPesquisa, false);
            if (pesquisaLigada)
            {
                string telefone = "";
                DateTime? ultima = null;
                await using (var cmdT = new SqlCommand(@"
SELECT ISNULL(C.TelefoneWhatsApp, ''), Q.UltimaInteracao
FROM TicketChamadoC C
LEFT JOIN dbo.TicketFinalizacaoPergunta Q ON Q.CodTicketChamadoC = C.Codigo
WHERE C.Codigo = @t AND C.CodEmp = @e AND ISNULL(C.ChatInterno, 'N') <> 'S';", conn))
                {
                    cmdT.Parameters.AddWithValue("@t", ticket);
                    cmdT.Parameters.AddWithValue("@e", emp);
                    await using var rd = await cmdT.ExecuteReaderAsync();
                    if (await rd.ReadAsync())
                    {
                        telefone = rd.GetString(0);
                        ultima = rd.IsDBNull(1) ? null : rd.GetDateTime(1);
                    }
                }

                if (telefone.Length > 0 && await FinalizacaoAutomaticaService.PesquisaVaiSairAsync(conn, ticket, emp, telefone))
                {
                    var modelo = await _parametros.BuscarValorAsync(emp, FinalizacaoAutomaticaService.ParamMensagem, "TICKET",
                        FinalizacaoAutomaticaService.DescMensagem, FinalizacaoAutomaticaService.MensagemPadrao);
                    var minutosParado = ultima.HasValue ? (int)Math.Round((DateTime.Now - ultima.Value).TotalMinutes) : 60;
                    textoAviso = FinalizacaoAutomaticaService.MontarAvisoEncerramento(modelo, minutosParado);
                    destino = await FinalizacaoAutomaticaService.ObterDestinoAsync(conn, ticket, emp, telefone);
                    avisouCliente = true;
                }
            }

            await using var cmd = new SqlCommand(@"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ok INT = 0;

BEGIN TRAN;

UPDATE C
SET CodSituacao = 3,
    Novo = 'N',
    UsuarioUltimaGravacao = @u,
    DataHoraUltimaGravacao = GETDATE()
FROM TicketChamadoC C
INNER JOIN dbo.TicketFinalizacaoPergunta Q ON Q.CodTicketChamadoC = C.Codigo AND Q.CodEmp = C.CodEmp
WHERE C.Codigo = @t AND C.CodEmp = @e
  AND ISNULL(C.CodSituacao, 1) = 1
  AND ISNULL(C.CodSetor, 1) = 1
  AND NOT EXISTS (SELECT 1 FROM TicketChamadoD D
                  WHERE D.CodTicketChamadoC = C.Codigo AND D.CodEmp = C.CodEmp AND D.DataHora > Q.UltimaInteracao);
SET @ok = @@ROWCOUNT;

IF @ok > 0
BEGIN
    -- Aviso de encerramento ao cliente (antes da pesquisa, que é gravada depois pelo envio do WhatsApp)
    IF @avisar = 1
        INSERT INTO TicketChamadoD
            (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
             TelefoneWhatsApp, CodInstancia, LidoSuporte, EnvioCliente, LidoCliente, StatusWhatsApp, Interno)
        VALUES
            (@e, @t, @aviso, GETDATE(), @auto,
             NULLIF(@tel, ''), NULL, 'S', 'N', NULL, NULL, 'N');

    INSERT INTO TicketChamadoD
        (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
         TelefoneWhatsApp, CodInstancia, LidoSuporte, EnvioCliente, LidoCliente, StatusWhatsApp, Interno)
    VALUES
        (@e, @t, @nota, GETDATE(), @u, NULL, NULL, 'S', 'N', NULL, 'interno', 'S');

    DELETE FROM dbo.TicketFinalizacaoPergunta WHERE CodTicketChamadoC = @t;
    IF OBJECT_ID('dbo.TicketFinalizacaoPausa', 'U') IS NOT NULL
        DELETE FROM dbo.TicketFinalizacaoPausa WHERE CodTicketChamadoC = @t;
END

COMMIT;
SELECT @ok;", conn);
            cmd.Parameters.AddWithValue("@t", ticket);
            cmd.Parameters.AddWithValue("@e", emp);
            cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Usuario;
            cmd.Parameters.Add("@nota", SqlDbType.NVarChar, -1).Value =
                $"Ticket finalizado por {Usuario} pelo aviso de cliente aguardando resposta." +
                (avisouCliente ? " O cliente foi avisado do encerramento e vai receber a pesquisa de satisfação." : "");
            cmd.Parameters.Add("@avisar", SqlDbType.Bit).Value = avisouCliente;
            cmd.Parameters.Add("@aviso", SqlDbType.NVarChar, -1).Value = textoAviso;
            cmd.Parameters.Add("@tel", SqlDbType.VarChar, 30).Value = destino;
            cmd.Parameters.Add("@auto", SqlDbType.VarChar, 50).Value = FinalizacaoAutomaticaService.UsuarioAutomatico;

            var ok = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (ok == 0)
            {
                // Pergunta não vale mais: some do modal
                await using var limpar = new SqlCommand(
                    "DELETE FROM dbo.TicketFinalizacaoPergunta WHERE CodTicketChamadoC = @t AND Status = 'PENDENTE';", conn);
                limpar.Parameters.AddWithValue("@t", ticket);
                await limpar.ExecuteNonQueryAsync();
                return "O ticket não foi finalizado: o cliente mandou mensagem nova ou o ticket já tinha sido finalizado.";
            }
        }

        LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{ticket}: finalizado por {Usuario} pelo aviso ({(comInsight ? "com" : "sem")} insight).");

        // O mesmo que acontece quando o atendente finaliza na tela do ticket
        try
        {
            var treino = await _parametros.BuscarBoolAsync(emp, "TREINAMENTO_REGISTRAR_AUTOMATICO", "TICKET",
                "Ao finalizar o ticket, a IA registra no histórico de treinamento do cliente o que foi ensinado. S=SIM N=NAO. PADRAO=S",
                true);
            if (treino)
                _treinos.ProcessarTicketFinalizadoEmSegundoPlano(ticket, emp, Usuario);

            if (comInsight)
                _insightAuto.Enfileirar(ticket, emp);
            else
                await _insightAuto.MarcarPuladoAsync(ticket);
        }
        catch (Exception ex)
        {
            LogHelper.Log($"[FINALIZACAO AUTO] Ticket #{ticket}: finalizado, mas falhou treinamento/insight ({ex.Message}).");
        }

        return null;
    }
}
