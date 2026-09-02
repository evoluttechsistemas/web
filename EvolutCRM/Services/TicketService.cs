using EvolutCRM.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using EvolutCRM.Helpers;


namespace EvolutCRM.Services
{
    using System.Runtime.InteropServices;
    using System.Text;

    public class KHelpResult
    {
        public bool Success { get; set; }
        public int Code { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Data { get; set; } = "";
    }

    public static class KHelpDeskIntegraNative
    {
        private const string DllName = "KHelpDeskIntegraV3.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int KHelp_Connect(
            string ip,
            int port,
            string privateKey,
            string id,
            string password,
            byte[] result,
            ref int resultSize
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int KHelp_GetLastErrorText(
            byte[] result,
            ref int resultSize
        );

        public static KHelpResult Connect(
            string ip,
            int port,
            string privateKey,
            string id,
            string password
        )
        {
            var buffer = new byte[8192];
            var size = buffer.Length;

            var code = KHelp_Connect(
                ip,
                port,
                privateKey,
                id,
                password,
                buffer,
                ref size
            );

            var data = Encoding.Default.GetString(buffer, 0, Math.Max(0, Math.Min(size, buffer.Length)))
                .TrimEnd('\0');

            var result = new KHelpResult
            {
                Code = code,
                Success = code == 0,
                Data = data
            };

            if (!result.Success)
                result.ErrorMessage = GetLastErrorText();

            return result;
        }

        private static string GetLastErrorText()
        {
            var buffer = new byte[4096];
            var size = buffer.Length;

            var code = KHelp_GetLastErrorText(
                buffer,
                ref size
            );

            if (code != 0)
                return "";

            return Encoding.Default.GetString(buffer, 0, Math.Max(0, Math.Min(size, buffer.Length)))
                .TrimEnd('\0');
        }
    }


    public class TicketService
    {
        private readonly string _conn;
        private readonly UserState _state;
        private readonly OpenAiClient _openAi;
        private readonly HealthMonitorService _monitor;  // ← ADD

        public TicketService(IConfiguration cfg, UserState state, OpenAiClient openAi, HealthMonitorService monitor)  // ← ADD
        {
            _conn = cfg.GetConnectionString("Connection");
            _state = state;
            _openAi = openAi;
            _monitor = monitor;  // ← ADD
        }

        private int CodEmpAtual
        {
            get
            {
                if (_state.CurrentCompanyId <= 0)
                    throw new InvalidOperationException("Empresa do usuário não carregada no UserState.");

                return _state.CurrentCompanyId;
            }
        }

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        private static void AddCodEmp(SqlCommand cmd, int codEmp)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        }

        private async Task<int> ObterCodEmpPorInstanciaAsync(SqlConnection con, int codInstancia, int codEmpInformado = 0)
        {
            if (codEmpInformado > 0)
                return codEmpInformado;

            if (codInstancia <= 0)
                return 2;

            using var cmd = new SqlCommand(@"
SELECT TOP 1 CodEmp
FROM WhatsAppInstancia
WHERE Codigo = @CodInstancia", con);

            cmd.Parameters.AddWithValue("@CodInstancia", codInstancia);

            var result = await cmd.ExecuteScalarAsync();
            var codEmp = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);

            if (codEmp <= 0)
                return 2;

            return codEmp;
        }

        public async Task<List<TicketChamadoCModel>> ListarChamadosAsync()
        {
            var lista = new List<TicketChamadoCModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
SELECT 
    T.Codigo,
    ISNULL(T.CodSetor, 1) AS CodSetor,
    ISNULL(T.CodCliente, 0) AS CodCliente,
    C.Nome AS NomeCliente,
    C.Apelido AS ApelidoCliente,
    ISNULL(T.Assunto, '') AS Assunto,
    T.Usuario,
    T.UsuarioAbertura,
    ISNULL(T.CodSituacao, 1) AS CodSituacao,
    ISNULL(T.DataHoraAbertura, GETDATE()) AS DataHoraAbertura,
    ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura) AS DataHoraUltimaGravacao,
    ISNULL(T.Novo, 'N') AS Novo,
    ISNULL(T.CodTipo, 1) AS CodTipo,
    ISNULL(T.Prioridade, 2) AS Prioridade,
    AP.StatusAprovacao,
    F.FotoUrl AS FotoClienteUrl
FROM TicketChamadoC T
LEFT JOIN Cliente C ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
LEFT JOIN ClienteWhatsAppFoto F
       ON F.CodEmp = T.CodEmp
      AND F.Telefone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(T.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', '')
OUTER APPLY (
    SELECT TOP 1
        CASE
            WHEN D.Anotacao LIKE N'✅ Ticket aprovado%' THEN 'APROVADO'
            WHEN D.Anotacao LIKE N'❌ Ticket recusado%' THEN 'RECUSADO'
            ELSE NULL
        END AS StatusAprovacao
    FROM TicketChamadoD D
    WHERE D.CodTicketChamadoC = T.Codigo
      AND D.CodEmp = T.CodEmp
      AND (
          D.Anotacao LIKE N'✅ Ticket aprovado%'
          OR D.Anotacao LIKE N'❌ Ticket recusado%'
      )
    ORDER BY D.DataHora DESC, D.Codigo DESC
) AP
WHERE T.CodEmp = @CodEmp
  AND ISNULL(T.CodSituacao, 1) <> 3
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
ORDER BY 
    CASE WHEN ISNULL(T.Novo, 'N') = 'S' THEN 0 ELSE 1 END,
    ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura) DESC";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new TicketChamadoCModel
                {
                    Codigo = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                    CodSetor = rd.IsDBNull(1) ? 1 : rd.GetInt32(1),
                    CodCliente = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
                    NomeCliente = rd.IsDBNull(3) ? null : rd.GetString(3),
                    ApelidoCliente = rd.IsDBNull(4) ? null : rd.GetString(4),
                    Assunto = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Usuario = rd.IsDBNull(6) ? null : rd.GetString(6),
                    UsuarioAbertura = rd.IsDBNull(7) ? null : rd.GetString(7),
                    CodSituacao = rd.IsDBNull(8) ? 1 : rd.GetInt32(8),
                    DataHoraAbertura = rd.IsDBNull(9) ? DateTime.Now : rd.GetDateTime(9),
                    DataHoraUltimaGravacao = rd.IsDBNull(10)
                        ? (rd.IsDBNull(9) ? DateTime.Now : rd.GetDateTime(9))
                        : rd.GetDateTime(10),
                    Novo = rd.IsDBNull(11) ? "N" : rd.GetString(11),
                    CodTipo = rd.IsDBNull(12) ? 1 : rd.GetInt32(12),
                    Prioridade = rd.IsDBNull(13) ? 2 : rd.GetInt32(13),
                    StatusAprovacao = rd.IsDBNull(14) ? null : rd.GetString(14),
                    FotoClienteUrl = rd.IsDBNull(15) ? null : rd.GetString(15)
                });
            }

            return lista;
        }

        public Task<(bool Sucesso, string Mensagem)> AcessarRemotoClienteAsync(string codigoAcesso)
        {
            if (string.IsNullOrWhiteSpace(codigoAcesso))
                return Task.FromResult((false, "Código de acesso não informado."));

            var ip = "127.0.0.1";
            var port = 5100;
            var privateKey = "HDTk1Zns3OI1kyG4JlajvL3Nrl6ZduZWH0/XPMbd9JI=";

            var senhaPadrao = "evolut91401149";

            try
            {
                codigoAcesso = LimparEntradaAcessoRemoto(codigoAcesso);
                senhaPadrao = LimparEntradaAcessoRemoto(senhaPadrao);

                var result = KHelpDeskIntegraNative.Connect(
                    ip,
                    port,
                    privateKey,
                    codigoAcesso,
                    senhaPadrao
                );

                if (result.Success)
                {
                    _monitor.Log(LogCategory.Sistema, LogSeverity.Success,    // ← ADD
                        $"Acesso remoto iniciado — código {codigoAcesso}");
                    return Task.FromResult((true, "Acesso remoto iniciado com sucesso."));
                }

                var erro = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.Data
                    : result.ErrorMessage;

                _monitor.Log(LogCategory.Sistema, LogSeverity.Warning,    // ← ADD
    $"Falha no acesso remoto — {erro}");
                return Task.FromResult((false, $"Falha ao iniciar acesso remoto: {erro}"));
            }
            catch (DllNotFoundException)
            {
                return Task.FromResult((false, "DLL KHelpDesk não encontrada. Verifique se ela está junto do executável."));
            }
            catch (BadImageFormatException)
            {
                return Task.FromResult((false, "Arquitetura incompatível entre o sistema e a DLL. Verifique x86/x64."));
            }
            catch (Exception ex)
            {
                return Task.FromResult((false, "Erro ao iniciar acesso remoto: " + ex.Message));
            }
        }

        private static string LimparEntradaAcessoRemoto(string? valor)
        {
            if (valor == null)
                return "";

            return valor
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("\u200B", "")
                .Replace("\uFEFF", "")
                .Trim();
        }

        public async Task<List<AcessoRemotoModel>> BuscarAcessosRemotosAsync(string termo)
        {
            var lista = new List<AcessoRemotoModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
SELECT TOP 100
    A.Codigo,
    A.CodCliente,
    ISNULL(C.Apelido, C.Nome) AS NomeCliente,
    A.NomeComputador,
    A.CodigoAcesso
FROM ClienteAcessoRemoto A
INNER JOIN Cliente C ON C.Codigo = A.CodCliente AND C.CodEmp = A.CodEmp
WHERE A.CodEmp = @CodEmp
  AND (
        @Termo = ''
        OR C.Nome LIKE @Busca
        OR C.Apelido LIKE @Busca
        OR A.NomeComputador LIKE @Busca
        OR A.CodigoAcesso LIKE @Busca
      )
ORDER BY ISNULL(C.Apelido, C.Nome), A.NomeComputador";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Termo", termo?.Trim() ?? "");
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Busca", "%" + (termo?.Trim() ?? "") + "%");

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new AcessoRemotoModel
                {
                    Codigo = Convert.ToInt32(rd["Codigo"]),
                    CodCliente = Convert.ToInt32(rd["CodCliente"]),
                    NomeCliente = rd["NomeCliente"]?.ToString() ?? "",
                    NomeComputador = rd["NomeComputador"]?.ToString() ?? "",
                    CodigoAcesso = rd["CodigoAcesso"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        public async Task<List<TicketChamadoCModel>> ListarChamadosFinalizadosAsync()
        {
            var lista = new List<TicketChamadoCModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
SELECT TOP 300
    T.Codigo, T.CodSetor, T.CodCliente,
    C.Nome AS NomeCliente, C.Apelido AS ApelidoCliente,
    T.Assunto, T.Usuario, T.UsuarioAbertura, T.CodSituacao,
    T.DataHoraAbertura, T.DataHoraUltimaGravacao, T.Novo,
    ISNULL(T.CodTipo, 1) AS CodTipo,
    ISNULL(T.Prioridade, 2) AS Prioridade,
    F.FotoUrl AS FotoClienteUrl
FROM TicketChamadoC T
LEFT JOIN Cliente C ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
LEFT JOIN ClienteWhatsAppFoto F
       ON F.CodEmp = T.CodEmp
      AND F.Telefone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(T.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', '')
WHERE T.CodEmp = @CodEmp
  AND T.CodSituacao = 3
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
ORDER BY ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura) DESC";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new TicketChamadoCModel
                {
                    Codigo = rd.GetInt32(0),
                    CodSetor = rd.GetInt32(1),
                    CodCliente = rd.GetInt32(2),
                    NomeCliente = rd.IsDBNull(3) ? null : rd.GetString(3),
                    ApelidoCliente = rd.IsDBNull(4) ? null : rd.GetString(4),
                    Assunto = rd.GetString(5),
                    Usuario = rd.IsDBNull(6) ? null : rd.GetString(6),
                    UsuarioAbertura = rd.IsDBNull(7) ? null : rd.GetString(7),
                    CodSituacao = rd.GetInt32(8),
                    DataHoraAbertura = rd.GetDateTime(9),
                    DataHoraUltimaGravacao = rd.IsDBNull(10) ? rd.GetDateTime(9) : rd.GetDateTime(10),
                    Novo = rd.IsDBNull(11) ? "N" : rd.GetString(11),
                    CodTipo = rd.GetInt32(12),
                    Prioridade = rd.GetInt32(13),
                    FotoClienteUrl = rd.IsDBNull(14) ? null : rd.GetString(14)
                });
            }

            return lista;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // WHATSAPP — recebimento e saudação automática
        // ═══════════════════════════════════════════════════════════════════════

        public Task ProcessarMensagemWhatsAppAsync(string telefoneBruto, string texto)
        {
            return ProcessarMensagemWhatsAppAsync(telefoneBruto, texto, 0, 2);
        }

        public async Task ProcessarMensagemWhatsAppAsync(string telefoneBruto, string texto, int codInstancia)
        {
            await ProcessarMensagemWhatsAppAsync(telefoneBruto, texto, codInstancia, 0);
        }

        public async Task ProcessarMensagemWhatsAppAsync(string telefoneBruto, string texto, int codInstancia, int codEmpInformado)
        {
            Log("ProcessarMensagemWhatsAppAsync CHAMADO | Telefone=" + telefoneBruto + " | Texto=" + texto + " | CodInstancia=" + codInstancia + " | CodEmpInformado=" + codEmpInformado);

            string telefone = SoDigitos(telefoneBruto);
            Log("Telefone normalizado: " + telefone);

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var codEmpMensagem = await ObterCodEmpPorInstanciaAsync(con, codInstancia, codEmpInformado);
            Log("CodEmp resolvido pela instância WhatsApp: " + codEmpMensagem);

            // ── 1. Descobre CodCliente pelo histórico de tickets ─────────────────
            int? codClienteRelacionando = null;

            var variantes = GerarVariantesTelefone(telefone);

            if (variantes.Any())
            {
                var paramNomes = variantes.Select((_, i) => "@T" + i).ToList();

                using var cmdBusca = new SqlCommand($@"
        WITH Candidatos AS (
            SELECT
                CodCliente,
                Codigo,
                COUNT(DISTINCT CodCliente) OVER () AS QtdClientesDistintos
            FROM TicketChamadoC
            WHERE CodEmp = @CodEmp
              AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                      ISNULL(TelefoneWhatsApp,''),
                  '(',''),')',''),'-',''),' ',''),'+','')
                  IN ({string.Join(",", paramNomes)})
              AND ISNULL(CodCliente,0) > 0
        )
        SELECT TOP 1 CodCliente
        FROM Candidatos
        WHERE QtdClientesDistintos = 1   -- só confia se o número aponta para UM cliente
        ORDER BY Codigo DESC", con);

                AddCodEmp(cmdBusca, codEmpMensagem);
                for (int i = 0; i < variantes.Count; i++)
                    cmdBusca.Parameters.AddWithValue(paramNomes[i], variantes[i]);

                var result = await cmdBusca.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    codClienteRelacionando = Convert.ToInt32(result);
            }

            // ── 1b. Fallback: busca cliente pela coluna Celular na tabela Cliente ─
            //        Cobre o caso em que NUNCA houve ticket vinculado com cliente,
            //        mas o celular está cadastrado no cliente.
            if (codClienteRelacionando == null && variantes.Any())
            {
                var paramNomes2 = variantes.Select((_, i) => "@C" + i).ToList();

                // Limpeza completa: ( ) - espaço + . /
                const string LIMPA = @"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        ISNULL({0},''),
    '(',''),')',''),'-',''),' ',''),'+',''),'.',''),'/','')";

                var limpaCelular = string.Format(LIMPA, "Celular");
                var limpaTelefone = string.Format(LIMPA, "Telefone");

                using var cmdCliente = new SqlCommand($@"
        SELECT TOP 1 Codigo
        FROM Cliente
        WHERE CodEmp = @CodEmp
          AND (
               {limpaCelular}  IN ({string.Join(",", paramNomes2)})
            OR {limpaTelefone} IN ({string.Join(",", paramNomes2)})
          )
        ORDER BY
            CASE WHEN ISNULL(ClienteMensalista,'N') = 'S' THEN 0 ELSE 1 END,
            Codigo DESC", con);

                AddCodEmp(cmdCliente, codEmpMensagem);
                for (int i = 0; i < variantes.Count; i++)
                    cmdCliente.Parameters.AddWithValue(paramNomes2[i], variantes[i]);

                var resultCliente = await cmdCliente.ExecuteScalarAsync();
                if (resultCliente != null && resultCliente != DBNull.Value)
                {
                    codClienteRelacionando = Convert.ToInt32(resultCliente);
                    Log($"[CLIENTE] Encontrado via tabela Cliente: CodCliente={codClienteRelacionando}");
                }
            }

            Log("CodCliente relacionado: " + (codClienteRelacionando?.ToString() ?? "nenhum"));

            // ── 2. Verifica se já existe ticket aberto para este telefone ─────────
            int? ticketAberto = null;

            if (variantes.Any())
            {
                var paramAberto = variantes.Select((_, i) => "@A" + i).ToList();

                using var cmdAberto = new SqlCommand($@"
        SELECT TOP 1 Codigo
        FROM TicketChamadoC
        WHERE CodEmp = @CodEmp
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                  ISNULL(TelefoneWhatsApp,''),
              '(',''),')',''),'-',''),' ',''),'+','')
              IN ({string.Join(",", paramAberto)})
          AND ISNULL(CodSituacao, 1) <> 3
        ORDER BY ISNULL(DataHoraUltimaGravacao, DataHoraAbertura) DESC, Codigo DESC", con);

                AddCodEmp(cmdAberto, codEmpMensagem);
                for (int i = 0; i < variantes.Count; i++)
                    cmdAberto.Parameters.AddWithValue(paramAberto[i], variantes[i]);

                var result = await cmdAberto.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    ticketAberto = Convert.ToInt32(result);
            }

            int codTicket;

            Log("Ticket aberto encontrado: " + (ticketAberto?.ToString() ?? "nenhum - criará novo"));

            if (ticketAberto.HasValue)
            {
                codTicket = ticketAberto.Value;
                Log("Usando ticket existente #" + codTicket);
                _monitor.Log(LogCategory.Tickets, LogSeverity.Info,    // ← ADD
                    $"Mensagem WhatsApp recebida — ticket existente #{codTicket}",
                    $"telefone={telefone}");

                // ── 2b. Se o ticket aberto está sem cliente mas agora temos um, vincula ──
                if (codClienteRelacionando.HasValue)
                {
                    using var cmdVincular = new SqlCommand(@"
                UPDATE TicketChamadoC
                SET CodCliente = @CodCliente
                WHERE Codigo = @Cod AND CodEmp = @CodEmp
                  AND ISNULL(CodCliente, 0) = 0", con);
                    AddCodEmp(cmdVincular, codEmpMensagem);
                    cmdVincular.Parameters.AddWithValue("@CodCliente", codClienteRelacionando.Value);
                    cmdVincular.Parameters.AddWithValue("@Cod", codTicket);
                    var rowsVinculados = await cmdVincular.ExecuteNonQueryAsync();
                    if (rowsVinculados > 0)
                        Log($"[CLIENTE] Ticket #{codTicket} vinculado ao cliente {codClienteRelacionando} via UPDATE");
                }

                using var cmdUpdate = new SqlCommand(@"
            UPDATE TicketChamadoC
            SET UsuarioUltimaGravacao = 'CLIENTE',
                DataHoraUltimaGravacao = GETDATE(),
                Novo = 'S',
                Usuario = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM Usuario U
                        WHERE U.CodEmp = @CodEmp
  AND UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(TicketChamadoC.Usuario)))
  AND ISNULL(U.Online, 'N') = 'S'
  AND ISNULL(U.Help, 'N') = 'S'
                    )
                    THEN TicketChamadoC.Usuario
                    ELSE 'NOVO'
                END
            WHERE Codigo = @Cod AND CodEmp = @CodEmp", con);

                AddCodEmp(cmdUpdate, codEmpMensagem);
                cmdUpdate.Parameters.AddWithValue("@Cod", codTicket);
                await cmdUpdate.ExecuteNonQueryAsync();

                Log("UPDATE responsavel executado para ticket #" + codTicket);
            }
            else
            {
                // ── 3. Cria novo ticket ───────────────────────────────────────────
                using (var cmdC = new SqlCommand(@"
            DECLARE @NovoTicket TABLE (Codigo INT);

            INSERT INTO TicketChamadoC
            (CodEmp, 
                Status, CodSetor, CodCategoria,
                DataAbertura, DataHoraAbertura,
                Usuario, UsuarioUltimaGravacao, DataHoraUltimaGravacao,
                CodCliente, TelefoneWhatsApp, Assunto,
                CodSituacao, Novo, CodTipo, CodInstanciaWhatsApp
            )
            OUTPUT INSERTED.Codigo INTO @NovoTicket
            VALUES
            (
                @CodEmp, 1, 1, NULL,
                GETDATE(), GETDATE(),
                'WHATSAPP', 'WHATSAPP', GETDATE(),
                @CodCliente, @Telefone, @Assunto,
                1, 'S', 5, @CodInstancia
            );

            SELECT Codigo FROM @NovoTicket;", con))
                {
                    string assunto = "[WhatsApp] " + (
                        texto.Length > 50 ? texto.Substring(0, 50) + "..." : texto
                    );

                    AddCodEmp(cmdC, codEmpMensagem);
                    cmdC.Parameters.AddWithValue("@Assunto", assunto);
                    // ← antes passava 0 quando null; agora mantém 0 mas já temos
                    //   o fallback do passo 1b preenchendo codClienteRelacionando
                    cmdC.Parameters.Add("@CodCliente", SqlDbType.Int).Value =
                        codClienteRelacionando ?? 0;
                    cmdC.Parameters.AddWithValue("@Telefone", telefone);
                    cmdC.Parameters.AddWithValue("@CodInstancia", codInstancia);

                    codTicket = Convert.ToInt32(await cmdC.ExecuteScalarAsync());
                    Log($"Novo ticket criado: #{codTicket} | CodCliente={codClienteRelacionando ?? 0}");
                    _monitor.Log(LogCategory.Tickets, LogSeverity.Info,    // ← ADD
                        $"Novo ticket criado via WhatsApp #{codTicket}",
                        $"telefone={telefone} cliente={codClienteRelacionando ?? 0}");
                }

                using (var cmdTel = new SqlCommand(@"
            INSERT INTO TicketChamadoD
            (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario, CodInstancia)
            VALUES (@CodEmp, @Cod, @Texto, GETDATE(), 'WHATSAPP', @CodInstancia)", con))
                {
                    AddCodEmp(cmdTel, codEmpMensagem);
                    cmdTel.Parameters.AddWithValue("@Cod", codTicket);
                    cmdTel.Parameters.AddWithValue("@Texto", $"Celular WhatsApp: {telefone}");
                    cmdTel.Parameters.AddWithValue("@CodInstancia", codInstancia);
                    await cmdTel.ExecuteNonQueryAsync();
                }
            }

            Log("Ticket final para mensagem: #" + codTicket);

            // ── 4. Insere a mensagem do cliente ──────────────────────────────────
            using (var cmdMsg = new SqlCommand(@"
                INSERT INTO TicketChamadoD
                (CodEmp, 
                    CodTicketChamadoC, Anotacao, DataHora,
                    Usuario, EnvioCliente, LidoSuporte, StatusWhatsApp, CodInstancia
                )
                VALUES
                (
                    @CodEmp, @Cod, @Texto, GETDATE(),
                    'CLIENTE', 'S', 'N', 'received', @CodInstancia
                )", con))
            {
                AddCodEmp(cmdMsg, codEmpMensagem);
                cmdMsg.Parameters.AddWithValue("@Cod", codTicket);
                cmdMsg.Parameters.AddWithValue("@Texto", texto);
                cmdMsg.Parameters.AddWithValue("@CodInstancia", codInstancia);
                await cmdMsg.ExecuteNonQueryAsync();
                Log("Mensagem do cliente inserida no ticket #" + codTicket);
            }

            // ── 5. Dispara saudação automática em background ─────────────────────
            // Não bloqueia o retorno do webhook.
            var _codTicketSaudacao = codTicket;
            var _codEmpSaudacao = codEmpMensagem;
            _ = Task.Run(async () =>
            {
                try
                {
                    Log("[SAUDACAO] Iniciando para ticket #" + _codTicketSaudacao);
                    await ProcessarSaudacaoAutomaticaAsync(_codTicketSaudacao, _codEmpSaudacao);
                    Log("[SAUDACAO] Concluido para ticket #" + _codTicketSaudacao);
                }
                catch (Exception exSaudacao)
                {
                    Log("[SAUDACAO] ERRO ticket #" + _codTicketSaudacao + ": " + exSaudacao.Message + " | " + exSaudacao.StackTrace);
                }
            });

            // ── 6. Analisa contexto do cliente em background ─────────────────────
            var _codTicketAnalise = codTicket;
            var _codEmpAnalise = codEmpMensagem;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)); // aguarda chegar mais mensagens
                    await AnalisarContextoClienteAsync(_codTicketAnalise, _codEmpAnalise);
                }
                catch (Exception ex)
                {
                    Log("[ANALISE] ERRO: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Orquestra o fluxo de saudação automática:
        ///   1. Verifica se é a 1ª mensagem do cliente neste ticket
        ///   2. Trava contra duplicata (race condition)
        ///   3. Gera saudação via IA e salva
        ///   4. Aguarda 5 segundos
        ///   5. Se nenhum atendente humano respondeu → envia mensagem de aguarde
        /// </summary>
        private async Task ProcessarSaudacaoAutomaticaAsync(int codTicket, int codEmp)
        {
            Log($"[SAUDACAO] ProcessarSaudacaoAutomaticaAsync ticket #{codTicket}");

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // Limpa travas expiradas (> 2 min)
            using (var cmdLimpa = new SqlCommand(@"
        DELETE FROM TicketChamadoD
        WHERE CodEmp = @CodEmp
          AND ISNULL(StatusWhatsApp, '') = 'ia_processing'
          AND Anotacao = '[IA_PROCESSANDO]'
          AND DataHora < DATEADD(MINUTE, -2, GETDATE())", con))
            {
                AddCodEmp(cmdLimpa, codEmp);
                await cmdLimpa.ExecuteNonQueryAsync();
            }

            var ehPrimeira = await EhPrimeiraMensagemClienteAsync(con, codTicket, codEmp);
            Log($"[SAUDACAO] EhPrimeiraMensagem={ehPrimeira} para ticket #{codTicket}");
            if (!ehPrimeira)
            {
                _monitor.Log(LogCategory.Tickets, LogSeverity.Info,
                    $"Saudação ignorada — não é 1ª mensagem ticket #{codTicket}");
                return;
            }

            var travou = await TentarTravarRespostaIaWhatsappAsync(con, codTicket, codEmp);
            Log($"[SAUDACAO] TentarTravar={travou} para ticket #{codTicket}");
            if (!travou)
            {
                _monitor.Log(LogCategory.Tickets, LogSeverity.Warning,
                    $"Saudação ignorada — trava já existe ticket #{codTicket}");
                return;
            }

            var nomeCliente = await BuscarNomeClienteDoTicketAsync(con, codTicket, codEmp);
            Log($"[SAUDACAO] NomeCliente='{nomeCliente}' para ticket #{codTicket}");

            await GerarSomenteSaudacaoWhatsappAsync(con, codTicket, nomeCliente, codEmp);
            Log($"[SAUDACAO] Saudacao salva para ticket #{codTicket}");

            await Task.Delay(TimeSpan.FromSeconds(5));
            await EnviarMensagemAguardarAsync(codTicket, nomeCliente, codEmp);
            Log($"[SAUDACAO] Aguarde salvo para ticket #{codTicket}");
        }

        /// <summary>
        /// Retorna true quando o atendente ainda NÃO respondeu após a última mensagem do cliente.
        /// Isso cobre tanto ticket novo quanto ticket reaberto pelo cliente.
        /// Lógica:
        ///   1. Pega o DataHora da mensagem mais recente do cliente (received)
        ///   2. Verifica se existe alguma resposta de atendente humano APÓS esse momento
        ///   3. Se não existe → deve enviar saudação
        /// </summary>
        private async Task<bool> EhPrimeiraMensagemClienteAsync(SqlConnection con, int codTicket, int codEmp)
        {
            // Pega o momento da última mensagem do cliente
            DateTime? ultimaMsgCliente = null;

            using (var cmdUltima = new SqlCommand(@"
                SELECT TOP 1 DataHora
                FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(EnvioCliente, 'N') = 'S'
                  AND ISNULL(StatusWhatsApp, '') = 'received'
                  AND ISNULL(MensagemExcluida, 'N') = 'N'
                  AND ISNULL(Anotacao, '') <> ''
                ORDER BY DataHora DESC, Codigo DESC", con))
            {
                AddCodEmp(cmdUltima, codEmp);
                cmdUltima.Parameters.AddWithValue("@CodTicket", codTicket);
                var r = await cmdUltima.ExecuteScalarAsync();
                if (r == null || r == DBNull.Value) return false;
                ultimaMsgCliente = Convert.ToDateTime(r);
            }

            if (ultimaMsgCliente == null) return false;

            // Verifica se atendente humano já respondeu APÓS a última mensagem do cliente
            using var cmdAtend = new SqlCommand(@"
                SELECT COUNT(1)
                FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(EnvioCliente, 'N') = 'N'
                  AND ISNULL(MensagemExcluida, 'N') = 'N'
                  AND DataHora > @UltimaMsgCliente
                  AND ISNULL(StatusWhatsApp, '') NOT IN
                      ('ia_processing', 'saudacao_auto', 'aguarde_auto')
                  AND UPPER(ISNULL(Usuario, '')) NOT IN
                      ('WHATSAPP','CLIENTE','IA','EVOLUTTECH','BOT','SISTEMA')", con);

            AddCodEmp(cmdAtend, codEmp);
            cmdAtend.Parameters.AddWithValue("@CodTicket", codTicket);
            cmdAtend.Parameters.AddWithValue("@UltimaMsgCliente", ultimaMsgCliente.Value);

            // Retorna true se NENHUM atendente respondeu ainda
            return Convert.ToInt32(await cmdAtend.ExecuteScalarAsync()) == 0;
        }

        /// <summary>
        /// Insere trava 'ia_processing' de forma atômica.
        /// Verifica se já existe saudação/trava APÓS a última mensagem do cliente.
        /// Se a saudação for de uma conversa anterior → permite nova saudação.
        /// </summary>
        private async Task<bool> TentarTravarRespostaIaWhatsappAsync(SqlConnection con, int codTicket, int codEmp)
        {
            using var cmd = new SqlCommand(@"
                DECLARE @UltimaMsgCliente DATETIME;

                SELECT TOP 1 @UltimaMsgCliente = DataHora
                FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(EnvioCliente, 'N') = 'S'
                  AND ISNULL(StatusWhatsApp, '') = 'received'
                  AND ISNULL(MensagemExcluida, 'N') = 'N'
                ORDER BY DataHora DESC, Codigo DESC;

                IF NOT EXISTS (
                    SELECT 1 FROM TicketChamadoD
                    WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                      AND DataHora >= ISNULL(@UltimaMsgCliente, '2000-01-01')
                      AND (
                          ISNULL(StatusWhatsApp, '') IN ('ia_processing', 'saudacao_auto', 'aguarde_auto')
                      )
                )
                BEGIN
                    INSERT INTO TicketChamadoD
                    (CodEmp, 
                        CodTicketChamadoC, Anotacao, DataHora, Usuario,
                        EnvioCliente, LidoCliente, LidoSuporte,
                        StatusWhatsApp, MensagemExcluida
                    )
                    VALUES
                    (
                        @CodEmp, @CodTicket, '[IA_PROCESSANDO]', GETDATE(), 'EvolutTech',
                        'N', 'N', 'S',
                        'ia_processing', 'N'
                    );
                    SELECT 1;
                END
                ELSE
                    SELECT 0;", con);

            AddCodEmp(cmd, codEmp);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        /// <summary>
        /// Gera apenas a saudação (sem responder o conteúdo da mensagem).
        /// Personaliza com o nome do cliente se disponível.
        /// Protege contra frases de atendimento retornadas pela IA.
        /// Salva com StatusWhatsApp = 'saudacao_auto' → não aparece como 'pending'.
        /// </summary>
        private async Task GerarSomenteSaudacaoWhatsappAsync(
            SqlConnection con,
            int codTicket,
            string? nomeCliente,
            int codEmp)
        {
            var agora = ObterHoraBrasil();
            var periodo = agora.Hour >= 5 && agora.Hour < 12 ? "MANHA" : "TARDE";
            var cumpr = periodo == "MANHA" ? "Bom dia" : "Boa tarde";
            Log($"[SAUDACAO] GerarSaudacao | hora={agora.Hour} periodo={periodo} ticket={codTicket}");

            string saudacao;

            try
            {
                Log($"[SAUDACAO] Chamando GenerateWhatsAppGreetingOnlyAsync...");
                saudacao = await _openAi.GenerateWhatsAppGreetingOnlyAsync(periodo);
                saudacao = saudacao?.Trim() ?? "";
                Log($"[SAUDACAO] IA retornou: '{saudacao}'");

                // Injeta nome se a IA não usou
                if (!string.IsNullOrWhiteSpace(nomeCliente)
                    && !saudacao.Contains(nomeCliente, StringComparison.OrdinalIgnoreCase))
                {
                    saudacao = saudacao.EndsWith("!")
                        ? saudacao.Insert(saudacao.LastIndexOf('!'), $", {nomeCliente}")
                        : $"{cumpr}, {nomeCliente}! Tudo bem?";
                }

                // Proteção: bloqueia qualquer frase de atendimento
                if (_termosBloqueadosSaudacao.Any(t =>
                        saudacao.Contains(t, StringComparison.OrdinalIgnoreCase))
                    || string.IsNullOrWhiteSpace(saudacao))
                {
                    saudacao = string.IsNullOrWhiteSpace(nomeCliente)
                        ? $"{cumpr}! Tudo bem?"
                        : $"{cumpr}, {nomeCliente}! Tudo bem?";
                }
            }
            catch (Exception exIa)
            {
                Log($"[SAUDACAO] ERRO na IA: {exIa.Message}");
                _monitor.LogException(LogCategory.Tickets, exIa,    // ← ADD
                    $"GerarSaudacao IA ticket #{codTicket}");
                saudacao = string.IsNullOrWhiteSpace(nomeCliente)
                    ? $"{cumpr}! Tudo bem?"
                    : $"{cumpr}, {nomeCliente}! Tudo bem?";
            }

            Log($"[SAUDACAO] Saudacao final: '{saudacao}'");
            _monitor.Log(LogCategory.Tickets, LogSeverity.Success,    // ← ADD
                $"Saudação gerada ticket #{codTicket}",
                saudacao.Length > 100 ? saudacao[..100] : saudacao);
            await SalvarMensagemAutomaticaAsync(con, codTicket, saudacao, "saudacao_auto", codEmp);
        }

        /// <summary>
        /// Chamado 5 segundos após a saudação.
        /// Se nenhum atendente humano respondeu ainda → envia mensagem de aguarde.
        /// </summary>
        private async Task EnviarMensagemAguardarAsync(int codTicket, string? nomeCliente, int codEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // Já enviou aguarde APÓS a última mensagem do cliente?
            using (var cmdCheck = new SqlCommand(@"
                DECLARE @UltimaMsgCliente2 DATETIME;
                SELECT TOP 1 @UltimaMsgCliente2 = DataHora
                FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(EnvioCliente,'N') = 'S'
                  AND ISNULL(StatusWhatsApp,'') = 'received'
                  AND ISNULL(MensagemExcluida,'N') = 'N'
                ORDER BY DataHora DESC, Codigo DESC;

                SELECT COUNT(1) FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(StatusWhatsApp, '') = 'aguarde_auto'
                  AND DataHora >= ISNULL(@UltimaMsgCliente2, '2000-01-01')", con))
            {
                AddCodEmp(cmdCheck, codEmp);
                cmdCheck.Parameters.AddWithValue("@CodTicket", codTicket);
                if (Convert.ToInt32(await cmdCheck.ExecuteScalarAsync()) > 0)
                    return;
            }

            // Atendente humano já respondeu?
            using (var cmdAtend = new SqlCommand(@"
                SELECT COUNT(1) FROM TicketChamadoD
                WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                  AND ISNULL(EnvioCliente, 'N')   = 'N'
                  AND ISNULL(MensagemExcluida,'N') = 'N'
                  AND ISNULL(StatusWhatsApp, '') NOT IN
                      ('saudacao_auto','aguarde_auto','ia_processing','pending')
                  AND UPPER(ISNULL(Usuario,'')) NOT IN
                      ('WHATSAPP','CLIENTE','IA','EVOLUTTECH','BOT','SISTEMA')", con))
            {
                AddCodEmp(cmdAtend, codEmp);
                cmdAtend.Parameters.AddWithValue("@CodTicket", codTicket);
                if (Convert.ToInt32(await cmdAtend.ExecuteScalarAsync()) > 0)
                    return;
            }

            var msg = string.IsNullOrWhiteSpace(nomeCliente)
                ? "Pode aguardar um momento? Em instantes um de nossos atendentes irá te ajudar. 😊"
                : $"{nomeCliente}, pode aguardar um momento? Em instantes um de nossos atendentes irá te ajudar. 😊";

            await SalvarMensagemAutomaticaAsync(con, codTicket, msg, "aguarde_auto", codEmp);
        }

        /// <summary>
        /// Busca o nome do cliente vinculado ao ticket.
        /// Tenta primeiro pela tabela Cliente, depois pelo UsuarioAbertura do ticket.
        /// </summary>
        private async Task<string?> BuscarNomeClienteDoTicketAsync(SqlConnection con, int codTicket, int codEmp)
        {
            // 1. Pelo CodCliente → tabela Cliente
            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)),''), LTRIM(RTRIM(C.Nome)))
                FROM TicketChamadoC T
                INNER JOIN Cliente C ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
                WHERE T.Codigo = @CodTicket
                  AND T.CodEmp = @CodEmp
                  AND T.CodCliente > 0", con))
            {
                AddCodEmp(cmd, codEmp);
                cmd.Parameters.AddWithValue("@CodTicket", codTicket);
                var r = await cmd.ExecuteScalarAsync();
                var nome = r?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(nome)) return nome;
            }

            // 2. Fallback: UsuarioAbertura do ticket
            using (var cmd2 = new SqlCommand(@"
                SELECT LTRIM(RTRIM(ISNULL(UsuarioAbertura,'')))
                FROM TicketChamadoC WHERE Codigo = @CodTicket AND CodEmp = @CodEmp", con))
            {
                AddCodEmp(cmd2, codEmp);
                cmd2.Parameters.AddWithValue("@CodTicket", codTicket);
                var r = await cmd2.ExecuteScalarAsync();
                var nome = r?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(nome)
                    && !_usuariosAutomaticos.Contains(nome))
                    return nome;
            }

            return null;
        }

        /// <summary>
        /// Atualiza o registro de trava (ia_processing) com a mensagem real.
        /// Se a trava não existir mais (expirou), insere diretamente.
        /// StatusWhatsApp customizado evita que apareça como 'pending' no histórico.
        /// </summary>
        private async Task SalvarMensagemAutomaticaAsync(
            SqlConnection con,
            int codTicket,
            string mensagem,
            string statusWhatsApp,
            int codEmp)
        {
            // Tenta atualizar a trava existente
            using (var cmdUpd = new SqlCommand(@"
                UPDATE TicketChamadoD
                SET Anotacao       = @Mensagem,
                    DataHora       = GETDATE(),
                    StatusWhatsApp = @Status,
                    MensagemExcluida = 'N'
                WHERE Codigo = (
                    SELECT TOP 1 Codigo FROM TicketChamadoD
                    WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                      AND ISNULL(StatusWhatsApp,'') = 'ia_processing'
                    ORDER BY DataHora DESC, Codigo DESC
                )", con))
            {
                AddCodEmp(cmdUpd, codEmp);
                cmdUpd.Parameters.AddWithValue("@CodTicket", codTicket);
                cmdUpd.Parameters.AddWithValue("@Mensagem", mensagem);
                cmdUpd.Parameters.AddWithValue("@Status", statusWhatsApp);

                if (await cmdUpd.ExecuteNonQueryAsync() > 0)
                    return; // atualizou a trava → pronto
            }

            // Nenhuma trava ativa: insere diretamente
            using var cmdIns = new SqlCommand(@"
                INSERT INTO TicketChamadoD
                (CodEmp, 
                    CodTicketChamadoC, Anotacao, DataHora, Usuario,
                    EnvioCliente, LidoCliente, LidoSuporte,
                    StatusWhatsApp, MensagemExcluida
                )
                VALUES
                (
                    @CodEmp, @CodTicket, @Mensagem, GETDATE(), 'EvolutTech',
                    'N', 'N', 'S',
                    @Status, 'N'
                )", con);

            AddCodEmp(cmdIns, codEmp);
            cmdIns.Parameters.AddWithValue("@CodTicket", codTicket);
            cmdIns.Parameters.AddWithValue("@Mensagem", mensagem);
            cmdIns.Parameters.AddWithValue("@Status", statusWhatsApp);

            await cmdIns.ExecuteNonQueryAsync();
        }

        // ── Conjuntos de constantes ───────────────────────────────────────────

        private static readonly HashSet<string> _usuariosAutomaticos =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "WHATSAPP", "CLIENTE", "IA", "EVOLUTTECH",
                "BOT", "SISTEMA", "NOVO"
            };

        private static readonly string[] _termosBloqueadosSaudacao =
        {
            "como posso ajudar", "em que posso ajudar", "posso ajudar",
            "como posso te ajudar", "em que posso te ajudar",
            "qual é o problema", "qual o problema",
            "o que aconteceu", "o que precisa",
            "qual a sua dúvida", "qual a dúvida",
            "me diga", "pode me dizer",
            "em que posso ser útil", "como posso ser útil"
        };

        // ─────────────────────────────────────────────────────────────────────

        private string NormalizarTelefone(string telefone)
        {
            return TelefoneBR.Normalizar(telefone);
        }

        /// <summary>
        /// Gera todas as variantes plausíveis do número para comparação:
        /// com/sem 55, com/sem 9º dígito. Cobre divergências entre cadastro e WhatsApp.
        /// </summary>
        private static List<string> GerarVariantesTelefone(string telefoneNormalizado)
        {
            return TelefoneBR.GerarVariantes(telefoneNormalizado);
        }

        private static DateTime ObterHoraBrasil()
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DEMAIS MÉTODOS (sem alteração)
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<List<ClienteModel>> BuscarClientesAsync(string termo)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            var termoLimpo = (termo ?? "").Trim();

            var cmd = new SqlCommand(@"
        SELECT TOP 15
            Codigo,
            ISNULL(NULLIF(LTRIM(RTRIM(Apelido)),''), ISNULL(Nome,'')) AS NomeExibicao
        FROM Cliente
        WHERE CodEmp = @CodEmp
          AND (
                Nome LIKE @Busca
                OR Apelido LIKE @Busca
                OR CAST(Codigo AS VARCHAR(20)) = @Termo
          )
        ORDER BY
            CASE WHEN ISNULL(ClienteMensalista,'N') = 'S' THEN 0 ELSE 1 END,
            ISNULL(NULLIF(LTRIM(RTRIM(Apelido)),''), ISNULL(Nome,''))", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Busca", "%" + termoLimpo + "%");
            cmd.Parameters.AddWithValue("@Termo", termoLimpo);

            var resultado = new List<ClienteModel>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultado.Add(new ClienteModel
                {
                    Codigo = reader.GetInt32(0),
                    Nome = reader.IsDBNull(1) ? "" : reader.GetString(1)
                });
            }

            return resultado;
        }

        public async Task<TicketChamadoCModel?> BuscarChamadoAsync(int id)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
    SELECT T.Codigo, T.Status, T.CodSetor, ISNULL(T.CodCategoria,0),
           T.DataAbertura, T.DataHoraAbertura,
           T.Usuario, T.UsuarioUltimaGravacao, T.DataHoraUltimaGravacao,
           T.CodCliente, T.Assunto, T.CodSituacao, T.Novo,
           ISNULL(T.Versao,''), ISNULL(T.CodTipo,1),
           ISNULL(T.Prioridade,2), ISNULL(T.TelefoneWhatsApp,''),
    ISNULL(T.ObservacaoCliente,'') AS ObservacaoCliente,
    ISNULL(T.AssuntoSugerido,'') AS AssuntoSugerido,
    ISNULL(T.SentimentoCliente,'') AS SentimentoCliente,
    ISNULL(T.SentimentoEmoji,'') AS SentimentoEmoji,
    ISNULL(T.AssuntoSugeridoStatus,'') AS AssuntoSugeridoStatus,
    T.CodInstanciaWhatsApp,
    F.FotoUrl AS FotoClienteUrl
    FROM TicketChamadoC T
    LEFT JOIN ClienteWhatsAppFoto F
           ON F.CodEmp = T.CodEmp
          AND F.Telefone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(T.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', '')
    WHERE T.Codigo = @Id AND T.CodEmp = @CodEmp
      AND ISNULL(T.ChatInterno, 'N') <> 'S'";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", id);

            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                return new TicketChamadoCModel
                {
                    Codigo = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                    Status = rd.IsDBNull(1) ? 1 : rd.GetInt32(1),
                    CodSetor = rd.IsDBNull(2) ? 1 : rd.GetInt32(2),
                    CodCategoria = rd.IsDBNull(3) ? null : rd.GetInt32(3),
                    DataAbertura = rd.IsDBNull(4) ? DateTime.Now : rd.GetDateTime(4),
                    DataHoraAbertura = rd.IsDBNull(5) ? DateTime.Now : rd.GetDateTime(5),
                    Usuario = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    UsuarioUltimaGravacao = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    DataHoraUltimaGravacao = rd.IsDBNull(8)
                        ? (rd.IsDBNull(5) ? DateTime.Now : rd.GetDateTime(5))
                        : rd.GetDateTime(8),
                    CodCliente = rd.IsDBNull(9) ? 0 : rd.GetInt32(9),
                    Assunto = rd.IsDBNull(10) ? "" : rd.GetString(10),
                    CodSituacao = rd.IsDBNull(11) ? 1 : rd.GetInt32(11),
                    Novo = rd.IsDBNull(12) ? "N" : rd.GetString(12),
                    Versao = rd.IsDBNull(13) ? "" : rd.GetString(13),
                    CodTipo = rd.IsDBNull(14) ? 1 : rd.GetInt32(14),
                    Prioridade = rd.IsDBNull(15) ? 2 : rd.GetInt32(15),
                    TelefoneWhatsApp = rd.IsDBNull(16) ? "" : rd.GetString(16),
                    ObservacaoCliente = rd.IsDBNull(17) ? "" : rd.GetString(17),
                    AssuntoSugerido = rd.IsDBNull(18) ? "" : rd.GetString(18),
                    SentimentoCliente = rd.IsDBNull(19) ? "" : rd.GetString(19),
                    SentimentoEmoji = rd.IsDBNull(20) ? "" : rd.GetString(20),
                    AssuntoSugeridoStatus = rd.IsDBNull(21) ? "" : rd.GetString(21),
                    CodInstanciaWhatsApp = rd.IsDBNull(22) ? null : (int?)rd.GetInt32(22),
                    FotoClienteUrl = rd.IsDBNull(23) ? null : rd.GetString(23)
                };
            }
            return null;
        }

        public async Task<string> BuscarNomeCliente(int codCliente)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand("SELECT Apelido FROM Cliente WHERE Codigo = @Cod AND CodEmp = @CodEmp", con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Cod", codCliente);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Cliente não encontrado";
        }

        public async Task<List<TicketChamadoDModel>> BuscarHistoricoAsync(int id)
        {
            var lista = new List<TicketChamadoDModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // ATENÇÃO: filtra 'ia_processing' para nunca exibir a trava no histórico.
            // 'saudacao_auto' e 'aguarde_auto' SÃO exibidos normalmente (são mensagens reais).
            var sql = @"
    SELECT 
        Codigo, CodTicketChamadoC, Anotacao, DataHora, Usuario,
        CaminhoImagem,
        CASE WHEN Imagem IS NOT NULL AND LEN(Imagem) > 0 THEN 1 ELSE 0 END AS TemImagem,
        NomeImagem, EnvioCliente,
        ISNULL(NovaAgenda, 'N'), DataHoraAgenda,
        ISNULL(AgendaResolvida, 'N'), UltimaNotificacaoAgenda,
        ISNULL(NotificarAgenda, 'S'),
        CASE WHEN Audio IS NOT NULL AND LEN(Audio) > 0 THEN 1 ELSE 0 END AS TemAudio,
        AudioMimeType, AudioFileName,
        ISNULL(StatusWhatsApp, ''),
        ISNULL(Alterado, 'N'),
        ISNULL(MensagemExcluida, 'N'),
        TranscricaoAudio,
ISNULL(AudioTranscrito, 'N') AS AudioTranscrito,
CASE WHEN Video IS NOT NULL AND LEN(Video) > 0 THEN 1 ELSE 0 END AS TemVideo,
        VideoMimeType, VideoFileName,
        ISNULL(CodMensagemRespondida, 0) AS CodMensagemRespondida,
        ISNULL(TextoMensagemRespondida, '') AS TextoMensagemRespondida,
        ISNULL(UsuarioMensagemRespondida, '') AS UsuarioMensagemRespondida,
        ISNULL(Interno, 'N') AS Interno
    FROM TicketChamadoD
    WHERE CodTicketChamadoC = @Id AND CodEmp = @CodEmp
      AND ISNULL(StatusWhatsApp, '') <> 'ia_processing'
    ORDER BY DataHora DESC";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", id);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var codigo = rd.GetInt32(0);
                var caminhoImagem = rd.IsDBNull(5) ? null : rd.GetString(5);
                var temImagem = rd.GetInt32(6) == 1;
                var nomeImagem = rd.IsDBNull(7) ? null : rd.GetString(7);
                var envioCliente = rd.IsDBNull(8) ? null : rd.GetString(8);

                if (temImagem && string.IsNullOrWhiteSpace(nomeImagem))
                    nomeImagem = "imagem-whatsapp.jpg";

                var temAudio = rd.GetInt32(14) == 1;
                var audioMimeType = rd.IsDBNull(15) ? null : rd.GetString(15);
                var audioFileName = rd.IsDBNull(16) ? null : rd.GetString(16);
                var statusWhats = rd.IsDBNull(17) ? "" : rd.GetString(17);
                var alterado = rd.IsDBNull(18) ? "N" : rd.GetString(18);
                var excluida = rd.IsDBNull(19) ? "N" : rd.GetString(19);

                string? audioUrl = temAudio ? $"/api/tickets/audio/{codigo}?t={codigo}" : null;
                // Se tem áudio, não usa caminhoImagem para não duplicar
                string? imagemUrl = temImagem ? $"/api/tickets/anexo/{codigo}" : (temAudio ? null : caminhoImagem);

                var transcricao = rd["TranscricaoAudio"] == DBNull.Value ? "" : rd["TranscricaoAudio"].ToString();
                var audioTranscrito = rd["AudioTranscrito"] == DBNull.Value ? "N" : rd["AudioTranscrito"].ToString();
                var temVideoRaw = rd["TemVideo"];
                var temVideo = temVideoRaw != DBNull.Value && Convert.ToInt32(temVideoRaw) == 1;
                var videoMimeType = rd["VideoMimeType"]?.ToString() ?? "";
                var videoFileName = rd["VideoFileName"]?.ToString() ?? "";
                var videoUrl = temVideo ? $"/api/tickets/video/{codigo}?t={codigo}" : "";

                lista.Add(new TicketChamadoDModel
                {
                    Codigo = rd.GetInt32(0),
                    CodTicketChamadoC = rd.GetInt32(1),
                    Anotacao = rd.GetString(2),
                    DataHora = rd.GetDateTime(3),
                    Usuario = rd.GetString(4),
                    CaminhoImagem = imagemUrl,
                    NomeImagem = nomeImagem,
                    NovaAgenda = rd.IsDBNull(9) ? "N" : rd.GetString(9),
                    DataHoraAgenda = rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                    AgendaResolvida = rd.IsDBNull(11) ? "N" : rd.GetString(11),
                    UltimaNotificacaoAgenda = rd.IsDBNull(12) ? null : rd.GetDateTime(12),
                    NotificarAgenda = rd.IsDBNull(13) ? "S" : rd.GetString(13),
                    AudioUrl = audioUrl,
                    AudioMimeType = audioMimeType,
                    AudioFileName = audioFileName,
                    StatusEnvioWhatsApp = statusWhats,
                    MensagemExcluida = excluida,
                    Alterado = alterado,
                    EnvioCliente = envioCliente,
                    TranscricaoAudio = transcricao,
                    AudioTranscrito = audioTranscrito,
                    VideoUrl = videoUrl,
                    VideoMimeType = videoMimeType,
                    VideoFileName = videoFileName,
                    CodMensagemRespondida = rd["CodMensagemRespondida"] == DBNull.Value || (int)rd["CodMensagemRespondida"] == 0 ? null : (int?)rd["CodMensagemRespondida"],
                    TextoMensagemRespondida = rd["TextoMensagemRespondida"]?.ToString(),
                    UsuarioMensagemRespondida = rd["UsuarioMensagemRespondida"]?.ToString(),
                    Interno = rd["Interno"]?.ToString() ?? "N",
                });
            }

            return lista;
        }

        public async Task<List<TicketChamadoDModel>> BuscarHistoricoNovasAsync(int id, int ultimoCodigo)
        {
            var lista = new List<TicketChamadoDModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
    SELECT 
        Codigo, CodTicketChamadoC, Anotacao, DataHora, Usuario,
        CaminhoImagem,
        CASE WHEN Imagem IS NOT NULL AND LEN(Imagem) > 0 THEN 1 ELSE 0 END AS TemImagem,
        NomeImagem, EnvioCliente,
        ISNULL(NovaAgenda, 'N'), DataHoraAgenda,
        ISNULL(AgendaResolvida, 'N'), UltimaNotificacaoAgenda,
        ISNULL(NotificarAgenda, 'S'),
        CASE WHEN Audio IS NOT NULL AND LEN(Audio) > 0 THEN 1 ELSE 0 END AS TemAudio,
        AudioMimeType, AudioFileName,
        ISNULL(StatusWhatsApp, ''),
        ISNULL(Alterado, 'N'),
        ISNULL(MensagemExcluida, 'N'),
        TranscricaoAudio,
        ISNULL(AudioTranscrito, 'N') AS AudioTranscrito,
        CASE WHEN Video IS NOT NULL AND LEN(Video) > 0 THEN 1 ELSE 0 END AS TemVideo,
        VideoMimeType, VideoFileName,
        ISNULL(CodMensagemRespondida, 0) AS CodMensagemRespondida,
        ISNULL(TextoMensagemRespondida, '') AS TextoMensagemRespondida,
        ISNULL(UsuarioMensagemRespondida, '') AS UsuarioMensagemRespondida,
        ISNULL(Interno, 'N') AS Interno
    FROM TicketChamadoD
    WHERE CodTicketChamadoC = @Id AND CodEmp = @CodEmp
      AND Codigo > @UltimoCodigo
      AND ISNULL(StatusWhatsApp, '') <> 'ia_processing'
    ORDER BY DataHora DESC";

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", id);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@UltimoCodigo", ultimoCodigo);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var codigo = rd.GetInt32(0);
                var caminhoImagem = rd.IsDBNull(5) ? null : rd.GetString(5);
                var temImagem = rd.GetInt32(6) == 1;
                var nomeImagem = rd.IsDBNull(7) ? null : rd.GetString(7);
                var envioCliente = rd.IsDBNull(8) ? null : rd.GetString(8);

                if (temImagem && string.IsNullOrWhiteSpace(nomeImagem))
                    nomeImagem = "imagem-whatsapp.jpg";

                var temAudio = rd.GetInt32(14) == 1;
                var audioMimeType = rd.IsDBNull(15) ? null : rd.GetString(15);
                var audioFileName = rd.IsDBNull(16) ? null : rd.GetString(16);
                var statusWhats = rd.IsDBNull(17) ? "" : rd.GetString(17);
                var alterado = rd.IsDBNull(18) ? "N" : rd.GetString(18);
                var excluida = rd.IsDBNull(19) ? "N" : rd.GetString(19);

                string? audioUrl = temAudio ? $"/api/tickets/audio/{codigo}?t={codigo}" : null;
                string? imagemUrl = temImagem ? $"/api/tickets/anexo/{codigo}" : (temAudio ? null : caminhoImagem);

                var transcricao = rd["TranscricaoAudio"] == DBNull.Value ? "" : rd["TranscricaoAudio"].ToString();
                var audioTranscrito = rd["AudioTranscrito"] == DBNull.Value ? "N" : rd["AudioTranscrito"].ToString();
                var temVideoRaw = rd["TemVideo"];
                var temVideo = temVideoRaw != DBNull.Value && Convert.ToInt32(temVideoRaw) == 1;
                var videoMimeType = rd["VideoMimeType"]?.ToString() ?? "";
                var videoFileName = rd["VideoFileName"]?.ToString() ?? "";
                var videoUrl = temVideo ? $"/api/tickets/video/{codigo}?t={codigo}" : "";

                lista.Add(new TicketChamadoDModel
                {
                    Codigo = rd.GetInt32(0),
                    CodTicketChamadoC = rd.GetInt32(1),
                    Anotacao = rd.GetString(2),
                    DataHora = rd.GetDateTime(3),
                    Usuario = rd.GetString(4),
                    CaminhoImagem = imagemUrl,
                    NomeImagem = nomeImagem,
                    NovaAgenda = rd.IsDBNull(9) ? "N" : rd.GetString(9),
                    DataHoraAgenda = rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                    AgendaResolvida = rd.IsDBNull(11) ? "N" : rd.GetString(11),
                    UltimaNotificacaoAgenda = rd.IsDBNull(12) ? null : rd.GetDateTime(12),
                    NotificarAgenda = rd.IsDBNull(13) ? "S" : rd.GetString(13),
                    AudioUrl = audioUrl,
                    AudioMimeType = audioMimeType,
                    AudioFileName = audioFileName,
                    StatusEnvioWhatsApp = statusWhats,
                    MensagemExcluida = excluida,
                    Alterado = alterado,
                    EnvioCliente = envioCliente,
                    TranscricaoAudio = transcricao,
                    AudioTranscrito = audioTranscrito,
                    VideoUrl = videoUrl,
                    VideoMimeType = videoMimeType,
                    VideoFileName = videoFileName,
                    CodMensagemRespondida = rd["CodMensagemRespondida"] == DBNull.Value || (int)rd["CodMensagemRespondida"] == 0 ? null : (int?)rd["CodMensagemRespondida"],
                    TextoMensagemRespondida = rd["TextoMensagemRespondida"]?.ToString(),
                    UsuarioMensagemRespondida = rd["UsuarioMensagemRespondida"]?.ToString(),
                    Interno = rd["Interno"]?.ToString() ?? "N",
                });
            }

            return lista;
        }

        public async Task<Dictionary<int, string>> BuscarStatusWhatsAppAsync(int id)
        {
            var mapa = new Dictionary<int, string>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = @"
    SELECT Codigo, ISNULL(StatusWhatsApp, '')
    FROM TicketChamadoD
    WHERE CodTicketChamadoC = @Id AND CodEmp = @CodEmp
      AND ISNULL(StatusWhatsApp, '') <> 'ia_processing'
      AND ISNULL(EnvioCliente, 'N') = 'N'";  // só mensagens NOSSAS têm status de entrega

            using var cmd = new SqlCommand(sql, con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", id);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                mapa[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);

            return mapa;
        }

        public async Task<string> BuscarTelefoneCliente(int codCliente)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand("SELECT Celular FROM Cliente WHERE Codigo = @Cod AND CodEmp = @CodEmp", con);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Cod", codCliente);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Cliente não encontrado";
        }

        public async Task<List<AcessoRemotoModel>> ListarAcessosRemotosClienteAsync(int codCliente)
        {
            var lista = new List<AcessoRemotoModel>();

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            var sql = @"
        SELECT Codigo, CodCliente, NomeComputador, CodigoAcesso
        FROM ClienteAcessoRemoto
        WHERE CodCliente = @CodCliente AND CodEmp = @CodEmp
        ORDER BY NomeComputador";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new AcessoRemotoModel
                {
                    Codigo = reader.GetInt32(reader.GetOrdinal("Codigo")),
                    CodCliente = reader.GetInt32(reader.GetOrdinal("CodCliente")),
                    NomeComputador = reader["NomeComputador"]?.ToString() ?? "",
                    CodigoAcesso = reader["CodigoAcesso"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        public async Task SalvarAcessoRemotoAsync(AcessoRemotoModel acesso)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            if (acesso.Codigo == 0)
            {
                var sql = @"
            INSERT INTO ClienteAcessoRemoto
                (CodEmp, CodCliente, NomeComputador, CodigoAcesso)
            VALUES
                (@CodEmp, @CodCliente, @NomeComputador, @CodigoAcesso)";

                using var cmd = new SqlCommand(sql, conn);
                AddCodEmp(cmd);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@CodCliente", acesso.CodCliente);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@NomeComputador", acesso.NomeComputador);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@CodigoAcesso", acesso.CodigoAcesso);

                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var sql = @"
            UPDATE ClienteAcessoRemoto
            SET NomeComputador = @NomeComputador,
                CodigoAcesso = @CodigoAcesso
            WHERE Codigo = @Codigo AND CodEmp = @CodEmp
              AND CodCliente = @CodCliente";

                using var cmd = new SqlCommand(sql, conn);
                AddCodEmp(cmd);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Codigo", acesso.Codigo);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@CodCliente", acesso.CodCliente);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@NomeComputador", acesso.NomeComputador);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@CodigoAcesso", acesso.CodigoAcesso);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task ExcluirAcessoRemotoAsync(int codigo, int codCliente)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            var sql = @"
        DELETE FROM ClienteAcessoRemoto
        WHERE Codigo = @Codigo AND CodEmp = @CodEmp
          AND CodCliente = @CodCliente";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<(bool Sucesso, string Mensagem)> SalvarAlteracoesAsync(
    TicketChamadoCModel ticket,
    string anotacao,
    string usuarioQueSalvou,
    IBrowserFile? imagem = null,
    byte[]? videoBytes = null,
    string? videoMimeType = null,
    string? videoFileName = null,
    byte[]? audioBytes = null,
    string? audioMimeType = null,
    string? audioFileName = null,
    bool criarAgenda = false,
    DateTime? dataHoraAgenda = null,
    bool notificarAgenda = true,
    TicketChamadoDModel? mensagemRespondida = null,
    bool mensagemInterna = false)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            string usuario = (usuarioQueSalvou ?? "").Trim().ToUpper();

            string assuntoAnterior = "";
            using (var cmdGet = new SqlCommand("SELECT Assunto FROM TicketChamadoC WHERE Codigo = @id AND CodEmp = @CodEmp", conn))
            {
                AddCodEmp(cmdGet);
                cmdGet.Parameters.AddWithValue("@id", ticket.Codigo);
                assuntoAnterior = (string?)await cmdGet.ExecuteScalarAsync() ?? "";
            }

            using (var cmdUpdate = new SqlCommand(@"
UPDATE TicketChamadoC
SET
    CodCliente = @CodCliente,
    Assunto = @Assunto,
    CodSituacao = @Situacao,
    CodSetor = @CodSetor,
    CodTipo = @CodTipo,
    Prioridade = @Prioridade,
    Usuario = @Usuario,
    Versao = @Versao,
    ObservacaoCliente = @ObservacaoCliente,
    UsuarioUltimaGravacao = @UsuarioQueSalvou,
    DataHoraUltimaGravacao = GETDATE(),
    Novo = CASE
        WHEN UPPER(@UsuarioQueSalvou) = UPPER(@Usuario) THEN 'N'
        WHEN UPPER(@UsuarioQueSalvou) NOT IN ('WHATSAPP','CLIENTE','BOT','SISTEMA','NOVO')
         AND UPPER(@Usuario) NOT IN ('WHATSAPP','CLIENTE','BOT','SISTEMA','NOVO')
         AND UPPER(@UsuarioQueSalvou) <> UPPER(@Usuario) THEN 'S'
        ELSE 'S'
    END
WHERE Codigo = @Id AND CodEmp = @CodEmp", conn))
            {
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Assunto", ticket.Assunto ?? "");
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Situacao", ticket.CodSituacao);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Versao", ticket.Versao ?? "");
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@CodSetor", ticket.CodSetor);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Usuario", ticket.Usuario ?? "");
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@CodTipo", ticket.CodTipo);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Prioridade", ticket.Prioridade <= 0 ? 2 : ticket.Prioridade);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@UsuarioQueSalvou", usuario);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@Id", ticket.Codigo);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@CodCliente", ticket.CodCliente);
                AddCodEmp(cmdUpdate);
                cmdUpdate.Parameters.AddWithValue("@ObservacaoCliente", ticket.ObservacaoCliente ?? "");
                await cmdUpdate.ExecuteNonQueryAsync();
            }

            byte[]? imagemBytes = null;
            string? nomeImagem = null;

            if (imagem != null)
            {
                await using var stream = imagem.OpenReadStream(10 * 1024 * 1024);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                imagemBytes = ms.ToArray();
                nomeImagem = imagem.Name;
            }

            if (videoBytes != null && videoBytes.Length > 0)
            {
                videoMimeType ??= "video/webm";
                videoFileName ??= "gravacao_tela.webm";
            }

            if (audioBytes != null && audioBytes.Length > 0)
            {
                audioMimeType = "audio/ogg; codecs=opus";
                audioFileName ??= "audio_ticket.ogg";
            }

            var temImagem = imagemBytes != null && imagemBytes.Length > 0 && !string.IsNullOrWhiteSpace(nomeImagem);
            var temVideo = videoBytes != null && videoBytes.Length > 0 && !string.IsNullOrWhiteSpace(videoFileName);
            var temAudio = audioBytes != null && audioBytes.Length > 0 && !string.IsNullOrWhiteSpace(audioFileName);
            var temAnotacao = !string.IsNullOrWhiteSpace(anotacao);

            // variáveis WhatsApp reutilizadas nos dois INSERTs
            var telefoneWhats = await ObterJidDestinoAsync(conn, ticket.Codigo, ticket.TelefoneWhatsApp);
            var codInstanciaWhats = ticket.CodInstanciaWhatsApp ?? 0;
            var deveEnviarWhatsApp = !string.IsNullOrWhiteSpace(telefoneWhats)
                                     && ticket.CodSituacao != 3;

            if (criarAgenda && dataHoraAgenda.HasValue)
            {
                var conflito = await VerificarConflitoAgendaTicketAsync(ticket.Usuario ?? "", dataHoraAgenda.Value);
                if (conflito.TemConflito)
                    return (false, conflito.Mensagem);
            }

            var mensagens = new List<string>();

            // Alteração de assunto: nunca vai para o cliente
            if (!string.Equals(assuntoAnterior, ticket.Assunto ?? "", StringComparison.Ordinal))
                mensagens.Add($"Assunto alterado de \"{assuntoAnterior}\" para \"{ticket.Assunto}\"");

            if (temAnotacao)
                mensagens.Add(anotacao.Trim());
            else if (criarAgenda && dataHoraAgenda.HasValue)
                mensagens.Add("📅 Agenda criada para este ticket.");

            foreach (var msg in mensagens)
            {
                var ehLinhaAgenda = criarAgenda && dataHoraAgenda.HasValue &&
                    (temAnotacao ? msg == anotacao.Trim() : msg == "📅 Agenda criada para este ticket.");

                // Alteração de assunto nunca vai para o WhatsApp
                var ehAlteracaoAssunto = msg.StartsWith("Assunto alterado de \"");
                var deveEnviarEssaMensagem = deveEnviarWhatsApp && !ehAlteracaoAssunto && !mensagemInterna;

                using var cmdD = new SqlCommand(@"
INSERT INTO TicketChamadoD
(CodEmp, 
    CodTicketChamadoC, Anotacao, DataHora, Usuario,
    TelefoneWhatsApp, CodInstancia,
    LidoSuporte, EnvioCliente, LidoCliente,
    Imagem, NomeImagem, Video, VideoMimeType, VideoFileName,
    Audio, AudioMimeType, AudioFileName,
    NovaAgenda, DataHoraAgenda, AgendaResolvida,
    UltimaNotificacaoAgenda, NotificarAgenda, StatusWhatsApp,
    CodMensagemRespondida, TextoMensagemRespondida, UsuarioMensagemRespondida,
    Interno
)
VALUES
(
    @CodEmp, @Cod, @Msg, GETDATE(), @Usuario,
    @TelefoneWhatsApp, @CodInstancia,
    'S', 'N', NULL,
    NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL,
    @NovaAgenda, @DataHoraAgenda, @AgendaResolvida,
    NULL, @NotificarAgenda, @StatusWhatsApp,
    @CodMensagemRespondida, @TextoMensagemRespondida, @UsuarioMensagemRespondida,
    @Interno
)", conn);

                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@Cod", ticket.Codigo);
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@Msg", msg);
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@Usuario", usuario);
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@NovaAgenda", ehLinhaAgenda ? "S" : "N");
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@NotificarAgenda", ehLinhaAgenda && notificarAgenda ? "S" : "N");
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@AgendaResolvida", "N");
                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@DataHoraAgenda", SqlDbType.DateTime).Value =
                    ehLinhaAgenda ? dataHoraAgenda!.Value : DBNull.Value;

                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@StatusWhatsApp", SqlDbType.VarChar, 50).Value =
                    mensagemInterna ? (object)"interno" : DBNull.Value;
                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@Interno", SqlDbType.Char, 1).Value =
                    mensagemInterna ? "S" : "N";

                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@CodMensagemRespondida", SqlDbType.Int).Value =
                    mensagemRespondida?.Codigo ?? (object)DBNull.Value;
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@TextoMensagemRespondida",
                    mensagemRespondida?.Anotacao?.Length > 200
                        ? mensagemRespondida.Anotacao.Substring(0, 200) + "..."
                        : mensagemRespondida?.Anotacao ?? "");
                AddCodEmp(cmdD);
                cmdD.Parameters.AddWithValue("@UsuarioMensagemRespondida",
                    mensagemRespondida?.Usuario ?? "");

                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@TelefoneWhatsApp", SqlDbType.VarChar, 30).Value =
                    deveEnviarEssaMensagem && !string.IsNullOrWhiteSpace(telefoneWhats)
                        ? telefoneWhats : (object)DBNull.Value;

                AddCodEmp(cmdD);
                cmdD.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                    deveEnviarEssaMensagem && codInstanciaWhats > 0
                        ? codInstanciaWhats : (object)DBNull.Value;

                await cmdD.ExecuteNonQueryAsync();
            }

            if (temImagem || temVideo || temAudio)
            {
                using var cmdMidia = new SqlCommand(@"
INSERT INTO TicketChamadoD
(CodEmp, 
    CodTicketChamadoC, Anotacao, DataHora, Usuario,
    TelefoneWhatsApp, CodInstancia,
    LidoSuporte, EnvioCliente, LidoCliente,
    Imagem, NomeImagem, Video, VideoMimeType, VideoFileName,
    Audio, AudioMimeType, AudioFileName,
    NovaAgenda, DataHoraAgenda, AgendaResolvida,
    UltimaNotificacaoAgenda, NotificarAgenda, StatusWhatsApp
)
VALUES
(
    @CodEmp, @Cod, '', GETDATE(), @Usuario,
    @TelefoneWhatsApp, @CodInstancia,
    'S', 'N', NULL,
    @Imagem, @NomeImagem, @Video, @VideoMimeType, @VideoFileName,
    @Audio, @AudioMimeType, @AudioFileName,
    'N', NULL, 'N',
    NULL, 'N', @StatusWhatsApp
)", conn);

                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.AddWithValue("@Cod", ticket.Codigo);
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.AddWithValue("@Usuario", usuario);

                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@TelefoneWhatsApp", SqlDbType.VarChar, 30).Value =
                    deveEnviarWhatsApp && !string.IsNullOrWhiteSpace(telefoneWhats)
                        ? telefoneWhats : (object)DBNull.Value;

                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@CodInstancia", SqlDbType.Int).Value =
                    deveEnviarWhatsApp && codInstanciaWhats > 0
                        ? codInstanciaWhats : (object)DBNull.Value;

                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@StatusWhatsApp", SqlDbType.VarChar, 50).Value = DBNull.Value;

                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@Imagem", SqlDbType.VarBinary, -1).Value = temImagem ? imagemBytes! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@NomeImagem", SqlDbType.NVarChar, 255).Value = temImagem ? nomeImagem! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@Video", SqlDbType.VarBinary, -1).Value = temVideo ? videoBytes! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@VideoMimeType", SqlDbType.VarChar, 200).Value = temVideo ? videoMimeType! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@VideoFileName", SqlDbType.VarChar, 255).Value = temVideo ? videoFileName! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@Audio", SqlDbType.VarBinary, -1).Value = temAudio ? audioBytes! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@AudioMimeType", SqlDbType.VarChar, 200).Value = temAudio ? audioMimeType! : DBNull.Value;
                AddCodEmp(cmdMidia);
                cmdMidia.Parameters.Add("@AudioFileName", SqlDbType.VarChar, 255).Value = temAudio ? audioFileName! : DBNull.Value;

                await cmdMidia.ExecuteNonQueryAsync();
            }

            _monitor.Log(LogCategory.Tickets, LogSeverity.Info,    // ← ADD
    $"Ticket #{ticket.Codigo} salvo por {usuario}");
            return (true, "OK");

        }

        public async Task AlterarSituacaoAsync(int ticketId, int novaSituacao, string usuarioQueAlterou)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoC
                SET CodSituacao = @situacao,
                    Novo = CASE
                        WHEN @situacao = 6 THEN 'S'
                        WHEN UPPER(Usuario) <> @usuario THEN 'S'
                        ELSE 'N'
                    END,
                    UsuarioUltimaGravacao = @usuario,
                    DataHoraUltimaGravacao = GETDATE()
                WHERE Codigo = @id AND CodEmp = @CodEmp", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@situacao", novaSituacao);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@usuario", usuarioQueAlterou.Trim().ToUpper());
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@id", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AprovarTicketAsync(int ticketId, string usuarioQueAprovou)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(@"
                    UPDATE TicketChamadoC SET CodSituacao=6, Novo='S',
                    UsuarioUltimaGravacao=@Usuario, DataHoraUltimaGravacao=GETDATE()
                    WHERE Codigo=@TicketId
                      AND CodEmp=@CodEmp", conn, tx))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@TicketId", ticketId);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Usuario", usuarioQueAprovou.Trim().ToUpper());
                    await cmd.ExecuteNonQueryAsync();
                }
                using (var cmdD = new SqlCommand(@"
                    INSERT INTO TicketChamadoD
                    (CodEmp, CodTicketChamadoC,Anotacao,DataHora,Usuario,LidoSuporte,EnvioCliente,LidoCliente)
                    VALUES(@CodEmp,@TicketId,@Anotacao,GETDATE(),@Usuario,'S','N',NULL)", conn, tx))
                {
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@TicketId", ticketId);
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@Anotacao", "✅ Ticket aprovado.");
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@Usuario", usuarioQueAprovou.Trim().ToUpper());
                    await cmdD.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public async Task RecusarTicketAsync(int ticketId, string usuarioQueRecusou, string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new Exception("Informe o motivo da recusa.");

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(@"
                    UPDATE TicketChamadoC SET CodSituacao=6, Novo='S',
                    UsuarioUltimaGravacao=@Usuario, DataHoraUltimaGravacao=GETDATE()
                    WHERE Codigo=@TicketId
                      AND CodEmp=@CodEmp", conn, tx))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@TicketId", ticketId);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Usuario", usuarioQueRecusou.Trim().ToUpper());
                    await cmd.ExecuteNonQueryAsync();
                }
                using (var cmdD = new SqlCommand(@"
                    INSERT INTO TicketChamadoD
                    (CodEmp, CodTicketChamadoC,Anotacao,DataHora,Usuario,LidoSuporte,EnvioCliente,LidoCliente)
                    VALUES(@CodEmp,@TicketId,@Anotacao,GETDATE(),@Usuario,'S','N',NULL)", conn, tx))
                {
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@TicketId", ticketId);
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@Anotacao", $"❌ Ticket recusado. Motivo: {motivo.Trim()}");
                    AddCodEmp(cmdD);
                    cmdD.Parameters.AddWithValue("@Usuario", usuarioQueRecusou.Trim().ToUpper());
                    await cmdD.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public async Task<int> CriarChamadoComAnexoAsync(
            string assunto, string descricao, int codCliente,
            int codSetor, int codSituacao, int codTipo, int prioridade,
            string usuarioResponsavel, IBrowserFile? arquivo, string usuarioAbertura,
            int? codUsuarioCliente = null, string? usuarioClienteRelacionado = null,
            string envioMassa = "N", bool criarAgenda = false,
            DateTime? dataHoraAgenda = null, bool notificarAgenda = true,
            string? telefoneWhatsApp = null, string? nomeWhatsApp = null)
        {
            byte[]? arquivoBytes = null;
            string? nomeArquivo = null;

            if (arquivo != null)
            {
                var ext = Path.GetExtension(arquivo.Name);
                var extPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg",".jpeg",".png",".gif",".webp",".bmp",
                    ".pdf",".doc",".docx",".xls",".xlsx",
                    ".txt",".csv",".zip",".rar",".xml",".json",".pfx",".p12"
                };
                if (!extPermitidas.Contains(ext))
                    throw new Exception($"Tipo de arquivo não permitido: {ext}");

                using var stream = arquivo.OpenReadStream(20 * 1024 * 1024);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                arquivoBytes = ms.ToArray();
                nomeArquivo = arquivo.Name;
            }

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            try
            {
                var usuarioDestino = (usuarioResponsavel ?? "").Trim().ToUpper();
                var usuarioAbriuSistema = (usuarioAbertura ?? "").Trim().ToUpper();
                var usuarioCliente = (usuarioClienteRelacionado ?? "").Trim().ToUpper();

                if (criarAgenda)
                {
                    if (!dataHoraAgenda.HasValue)
                        throw new Exception("Informe a data/hora da agenda.");

                    using var cmdConflito = new SqlCommand(@"
                        SELECT TOP 1 D.DataHoraAgenda, D.CodTicketChamadoC
                        FROM TicketChamadoD D
                        INNER JOIN TicketChamadoC C ON C.Codigo = D.CodTicketChamadoC AND C.CodEmp = D.CodEmp
                        WHERE ISNULL(D.NovaAgenda,'N')='S' AND D.DataHoraAgenda IS NOT NULL
                          AND ISNULL(D.AgendaResolvida,'N')<>'S' AND ISNULL(C.CodSituacao,1)<>3
                          AND UPPER(ISNULL(C.Usuario,''))=UPPER(@Usuario)
                          AND ABS(DATEDIFF(MINUTE,D.DataHoraAgenda,@DataHoraAgenda))<30
                        ORDER BY ABS(DATEDIFF(MINUTE,D.DataHoraAgenda,@DataHoraAgenda)) ASC", conn, tx);

                    AddCodEmp(cmdConflito);
                    cmdConflito.Parameters.AddWithValue("@Usuario", usuarioDestino);
                    AddCodEmp(cmdConflito);
                    cmdConflito.Parameters.AddWithValue("@DataHoraAgenda", dataHoraAgenda.Value);

                    using var rdConflito = await cmdConflito.ExecuteReaderAsync();
                    if (await rdConflito.ReadAsync())
                    {
                        var horario = rdConflito.GetDateTime(0);
                        var codTkt = rdConflito.GetInt32(1);
                        throw new Exception(
                            $"Já existe agenda para {usuarioDestino} às {horario:dd/MM/yyyy HH:mm} no ticket #{codTkt}. Mínimo 30 minutos de diferença.");
                    }
                }

                var ehChamadoParaCliente = codTipo == 4 && codUsuarioCliente.HasValue && !string.IsNullOrWhiteSpace(usuarioCliente);
                var usuarioAberturaBanco = ehChamadoParaCliente
    ? usuarioCliente
    : (codTipo == 5 && !string.IsNullOrWhiteSpace(nomeWhatsApp)
        ? nomeWhatsApp.Trim()
        : usuarioAbriuSistema);
                var novoFlag = ehChamadoParaCliente || envioMassa == "S" ? "S" :
                    !string.Equals(usuarioDestino, usuarioAbriuSistema, StringComparison.OrdinalIgnoreCase) ? "S" : "N";

                int ticketId;
                using (var cmd = new SqlCommand(@"
                    INSERT INTO TicketChamadoC
                    (CodEmp, Status,CodSetor,CodCategoria,DataAbertura,DataHoraAbertura,
                     Usuario,CodUsuario,UsuarioUltimaGravacao,DataHoraUltimaGravacao,
                     CodCliente,Assunto,CodSituacao,Novo,CodTipo,Prioridade,
                     UsuarioAbertura,EnvioMassa,TelefoneWhatsApp)
                    OUTPUT INSERTED.Codigo
                    VALUES
                    (@CodEmp,1,@Setor,NULL,CAST(GETDATE() AS DATE),GETDATE(),
                     @UsuarioDestino,@CodUsuario,@UsuarioAbertura,GETDATE(),
                     @Cliente,@Assunto,@Situacao,@Novo,@CodTipo,@Prioridade,
                     @UsuarioAbertura,@EnvioMassa,@TelefoneWhatsApp)", conn, tx))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Setor", codSetor);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@UsuarioDestino", usuarioDestino);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@CodUsuario", (object?)codUsuarioCliente ?? DBNull.Value);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Cliente", codCliente);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Assunto", assunto);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Situacao", codSituacao);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@CodTipo", codTipo);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Prioridade", prioridade <= 0 ? 2 : prioridade);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@UsuarioAbertura", usuarioAberturaBanco);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Novo", novoFlag);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@EnvioMassa", envioMassa);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@TelefoneWhatsApp",
                        string.IsNullOrWhiteSpace(telefoneWhatsApp)
                            ? DBNull.Value : NormalizarTelefone(telefoneWhatsApp));
                    ticketId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                using (var cmd = new SqlCommand(@"
                    INSERT INTO TicketChamadoD
                    (CodEmp, CodTicketChamadoC,Anotacao,DataHora,Usuario,Imagem,NomeImagem,
                     LidoSuporte,EnvioCliente,LidoCliente,NovaAgenda,DataHoraAgenda,
                     AgendaResolvida,UltimaNotificacaoAgenda,NotificarAgenda)
                    VALUES
                    (@CodEmp,@Cod,@Desc,GETDATE(),@Usuario,@Imagem,@Nome,
                     'S',@EnvioCliente,@LidoCliente,@NovaAgenda,@DataHoraAgenda,
                     'N',NULL,@NotificarAgenda)", conn, tx))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Cod", ticketId);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Desc", descricao);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Usuario", usuarioAbertura.Trim().ToUpper());
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@EnvioCliente", ehChamadoParaCliente ? "S" : "N");
                    AddCodEmp(cmd);
                    cmd.Parameters.Add("@LidoCliente", SqlDbType.VarChar, 1).Value = ehChamadoParaCliente ? "N" : DBNull.Value;
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@NovaAgenda", criarAgenda ? "S" : "N");
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@NotificarAgenda", criarAgenda && notificarAgenda ? "S" : "N");
                    AddCodEmp(cmd);
                    cmd.Parameters.Add("@DataHoraAgenda", SqlDbType.DateTime).Value =
                        criarAgenda && dataHoraAgenda.HasValue ? dataHoraAgenda.Value : DBNull.Value;
                    AddCodEmp(cmd);
                    cmd.Parameters.Add("@Imagem", SqlDbType.VarBinary, -1).Value = (object?)arquivoBytes ?? DBNull.Value;
                    AddCodEmp(cmd);
                    cmd.Parameters.Add("@Nome", SqlDbType.NVarChar, 255).Value = (object?)nomeArquivo ?? DBNull.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();
                return ticketId;
            }
            catch { tx.Rollback(); throw; }
        }

        public async Task<List<string>> ListarUsuariosAsync()
        {
            var lista = new List<string>();
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT Usuario FROM Usuario WHERE CodEmp=@CodEmp AND ISNULL(Inativo,'N')='N' AND ISNULL(Help,'N')='S' ORDER BY Usuario", con);
            AddCodEmp(cmd);
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(rd.GetString(0));
            return lista;
        }

        public async Task<TicketsPendentesDto> ObterPendentesParaUsuarioAsync(string usuario)
        {
            usuario = usuario.Trim().ToUpper();
            var tickets = await ListarChamadosAsync();

            var ticketsNovos = tickets
                .Where(t => t.CodSituacao == 1 && t.Novo == "S"
                    && !string.IsNullOrWhiteSpace(t.Usuario)
                    && t.Usuario.Trim().ToUpper() == usuario)
                .OrderByDescending(t => t.Codigo).ToList();

            var ticketsAtualizar = tickets
                .Where(t => t.CodSituacao == 4
                    && !string.IsNullOrWhiteSpace(t.Usuario)
                    && t.Usuario.Trim().ToUpper() == usuario)
                .OrderByDescending(t => t.Codigo).ToList();

            return new TicketsPendentesDto
            {
                Novos = ticketsNovos.Count,
                Atualizar = ticketsAtualizar.Count,
                UltimoTicketId = ticketsNovos.FirstOrDefault()?.Codigo ?? 0,
                UltimoTicketAtualizarId = ticketsAtualizar.FirstOrDefault()?.Codigo ?? 0,
                TicketsNovos = ticketsNovos.Select(t => t.Codigo).ToList(),
                TicketsAtualizar = ticketsAtualizar.Select(t => t.Codigo).ToList()
            };
        }

        public async Task<int> GetQuantidadeTicketsNovosAsync(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario)) return 0;

            const string sql = @"
                SELECT COUNT(1) FROM TicketChamadoC
                WHERE CodEmp = @CodEmp
                  AND ISNULL(Novo,'N')='S'
                  AND ISNULL(CodSituacao,1)<>3
                  AND ISNULL(ChatInterno,'N')<>'S'
                  AND (
                        UPPER(LTRIM(RTRIM(ISNULL(Usuario,''))))=UPPER(LTRIM(RTRIM(@Usuario)))
                        OR UPPER(LTRIM(RTRIM(ISNULL(Usuario,''))))
                           IN ('','WHATSAPP','NOVO','CLIENTE')
                      )";

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        public async Task MarcarComoAbertoAsync(int ticketId)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            // Só marca como lido se já houver responsável humano definido
            using var cmd = new SqlCommand(@"
        UPDATE TicketChamadoC SET Novo='N'
        WHERE Codigo = @Id AND CodEmp = @CodEmp
          AND UPPER(LTRIM(RTRIM(ISNULL(Usuario,'')))) NOT IN ('','NOVO','WHATSAPP','CLIENTE','BOT','SISTEMA')", con);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<(bool Sucesso, string Mensagem)> EnviarMensagemNoChatAsync(
            int codTicket, string texto, string usuario)
        {
            try
            {
                using var con = new SqlConnection(_conn);
                await con.OpenAsync();

                var usuarioAtual = (usuario ?? "").Trim().ToUpper();
                string responsavelTicket = "";

                using (var cmdR = new SqlCommand(
                    "SELECT Usuario FROM TicketChamadoC WHERE Codigo=@CodTicket AND CodEmp=@CodEmp", con))
                {
                    AddCodEmp(cmdR);
                    cmdR.Parameters.AddWithValue("@CodTicket", codTicket);
                    responsavelTicket = (await cmdR.ExecuteScalarAsync())?.ToString()?.Trim().ToUpper() ?? "";
                }

                var envioCliente = usuarioAtual == responsavelTicket ? "N" : "S";

                using (var cmd = new SqlCommand(@"
                    INSERT INTO TicketChamadoD
                    (CodEmp, CodTicketChamadoC,Anotacao,DataHora,Usuario,EnvioCliente)
                    VALUES(@CodEmp,@CodTicket,@Anotacao,GETDATE(),@Usuario,@EnvioCliente)", con))
                {
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@CodTicket", codTicket);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Anotacao", texto);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Usuario", usuarioAtual);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@EnvioCliente", envioCliente);
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmdU = new SqlCommand(@"
                    UPDATE TicketChamadoC
                    SET Novo=CASE WHEN @EnvioCliente='S' THEN 'S' ELSE 'N' END,
                        CodSituacao=CASE WHEN @EnvioCliente='S' THEN 4 ELSE CodSituacao END,
                        Usuario=@Responsavel, DataHoraUltimaGravacao=GETDATE()
                    WHERE Codigo=@Cod AND CodEmp=@CodEmp", con))
                {
                    AddCodEmp(cmdU);
                    cmdU.Parameters.AddWithValue("@Cod", codTicket);
                    AddCodEmp(cmdU);
                    cmdU.Parameters.AddWithValue("@Responsavel", responsavelTicket);
                    AddCodEmp(cmdU);
                    cmdU.Parameters.AddWithValue("@EnvioCliente", envioCliente);
                    await cmdU.ExecuteNonQueryAsync();
                }

                return (true, "OK");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public async Task<List<UsuarioClienteDto>> ObterUsuariosClienteAsync(int codCliente)
        {
            var lista = new List<UsuarioClienteDto>();
            using var conn = new SqlConnection(_conn);
            using var cmd = new SqlCommand(@"
                SELECT Codigo, CodCliente, Usuario
                FROM UsuarioClienteEvolutTech
                WHERE CodEmp = @CodEmp
                  AND CodCliente=@CodCliente ORDER BY Usuario", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lista.Add(new UsuarioClienteDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    CodCliente = Convert.ToInt32(reader["CodCliente"]),
                    Usuario = reader["Usuario"]?.ToString() ?? ""
                });
            return lista;
        }

        public async Task<(bool Sucesso, string Mensagem, int TotalCriados)>
            CriarChamadosEmMassaMensalistasAsync(
                string assunto, string descricao, int codSetor, int codSituacao,
                int codTipo, int prioridade, string usuarioResponsavel,
                IBrowserFile? arquivo, string usuarioAbertura)
        {
            try
            {
                var destinos = new List<(int CodCliente, int CodUsuarioCliente, string UsuarioCliente)>();

                using (var conn = new SqlConnection(_conn))
                {
                    await conn.OpenAsync();
                    using var cmd = new SqlCommand(@"
                        SELECT C.Codigo, U.Codigo, U.Usuario
                        FROM Cliente C
                        INNER JOIN UsuarioClienteEvolutTech U ON U.CodCliente=C.Codigo AND U.CodEmp=C.CodEmp
                        WHERE C.CodEmp = @CodEmp
                          AND ISNULL(C.ClienteMensalista,'N')='S'
                          AND ISNULL(U.Usuario,'')<>''
                        ORDER BY C.Codigo, U.Usuario", conn);
                    AddCodEmp(cmd);
                    using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                        destinos.Add((
                            Convert.ToInt32(rd[0]),
                            Convert.ToInt32(rd[1]),
                            rd[2]?.ToString()?.Trim().ToUpper() ?? ""
                        ));
                }

                if (destinos.Count == 0)
                    return (false, "Nenhum usuário de cliente mensalista encontrado.", 0);

                int totalCriados = 0;
                var erros = new List<string>();

                foreach (var d in destinos)
                {
                    try
                    {
                        await CriarChamadoComAnexoAsync(
                            assunto, descricao, d.CodCliente, codSetor, codSituacao,
                            4, prioridade, usuarioResponsavel, arquivo, usuarioAbertura,
                            d.CodUsuarioCliente, d.UsuarioCliente, "S");
                        totalCriados++;
                    }
                    catch (Exception ex)
                    {
                        erros.Add($"Cliente {d.CodCliente} / {d.UsuarioCliente}: {ex.Message}");
                    }
                }

                if (totalCriados == 0)
                    return (false, "Nenhum ticket criado. " + string.Join(" | ", erros.Take(5)), 0);

                return erros.Count > 0
                    ? (true, $"Parcial: {totalCriados} criados, {erros.Count} erros.", totalCriados)
                    : (true, $"Concluído: {totalCriados} tickets.", totalCriados);
            }
            catch (Exception ex) { return (false, "Erro: " + ex.Message, 0); }
        }

        public async Task<List<TicketChamadoDModel>> BuscarAgendasVencidasAsync(string usuario)
        {
            var lista = new List<TicketChamadoDModel>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                SELECT D.Codigo, D.CodTicketChamadoC, D.Anotacao, D.DataHora,
                       C.Usuario, D.DataHoraAgenda, D.UltimaNotificacaoAgenda
                FROM TicketChamadoD D
                INNER JOIN TicketChamadoC C ON C.Codigo=D.CodTicketChamadoC AND C.CodEmp=D.CodEmp
                WHERE ISNULL(D.NovaAgenda,'N')='S'
                  AND ISNULL(D.AgendaResolvida,'N')='N'
                  AND ISNULL(D.NotificarAgenda,'S')='S'
                  AND D.DataHoraAgenda<=GETDATE()
                  AND ISNULL(C.CodSituacao,1)<>3
                  AND ISNULL(C.ChatInterno,'N')<>'S'
                  AND UPPER(ISNULL(C.Usuario,''))=UPPER(@Usuario)
                  AND (D.UltimaNotificacaoAgenda IS NULL
                       OR DATEDIFF(MINUTE,D.UltimaNotificacaoAgenda,GETDATE())>=5)
                ORDER BY D.DataHoraAgenda ASC", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpper());
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new TicketChamadoDModel
                {
                    Codigo = Convert.ToInt32(rd["Codigo"]),
                    CodTicketChamadoC = Convert.ToInt32(rd["CodTicketChamadoC"]),
                    Anotacao = rd["Anotacao"]?.ToString() ?? "",
                    DataHora = Convert.ToDateTime(rd["DataHora"]),
                    Usuario = rd["Usuario"]?.ToString() ?? "",
                    NovaAgenda = "S",
                    AgendaResolvida = "N",
                    DataHoraAgenda = rd["DataHoraAgenda"] == DBNull.Value ? null : Convert.ToDateTime(rd["DataHoraAgenda"]),
                    UltimaNotificacaoAgenda = rd["UltimaNotificacaoAgenda"] == DBNull.Value ? null : Convert.ToDateTime(rd["UltimaNotificacaoAgenda"])
                });
            return lista;
        }

        public async Task AtualizarUltimaNotificacaoAgendaAsync(int codigoDetalhe)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "UPDATE TicketChamadoD SET UltimaNotificacaoAgenda=GETDATE() WHERE Codigo=@Codigo AND CodEmp=@CodEmp", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigoDetalhe);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ResolverAgendaAsync(int codigoDetalhe)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "UPDATE TicketChamadoD SET AgendaResolvida='S',NotificarAgenda='N' WHERE Codigo=@Codigo AND CodEmp=@CodEmp", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigoDetalhe);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ResolverAgendasDoTicketAsync(int codTicket)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoD SET AgendaResolvida='S',NotificarAgenda='N'
                WHERE CodTicketChamadoC=@CodTicket AND CodEmp=@CodEmp
                  AND ISNULL(NovaAgenda,'N')='S'
                  AND ISNULL(AgendaResolvida,'N')='N'", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<TicketChamadoDModel>> BuscarAgendasTicketsAsync(string? usuario = null)
        {
            var lista = new List<TicketChamadoDModel>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            var filtrar = !string.IsNullOrWhiteSpace(usuario) && usuario != "TODOS";

            using var cmd = new SqlCommand(@"
                SELECT D.Codigo, D.CodTicketChamadoC, D.Anotacao, D.DataHora,
                       C.Usuario, ISNULL(D.NovaAgenda,'N'), D.DataHoraAgenda,
                       ISNULL(D.AgendaResolvida,'N'), D.UltimaNotificacaoAgenda,
                       ISNULL(D.NotificarAgenda,'S')
                FROM TicketChamadoD D
                INNER JOIN TicketChamadoC C ON C.Codigo=D.CodTicketChamadoC AND C.CodEmp=D.CodEmp
                WHERE ISNULL(D.NovaAgenda,'N')='S' AND D.DataHoraAgenda IS NOT NULL
                  AND ISNULL(C.CodSituacao,1)<>3
                  AND ISNULL(C.ChatInterno,'N')<>'S'
                  AND (@FiltrarUsuario=0 OR UPPER(ISNULL(C.Usuario,''))=UPPER(@Usuario))
                ORDER BY D.DataHoraAgenda ASC", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@FiltrarUsuario", filtrar ? 1 : 0);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario?.Trim().ToUpper() ?? "");

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new TicketChamadoDModel
                {
                    Codigo = Convert.ToInt32(rd[0]),
                    CodTicketChamadoC = Convert.ToInt32(rd[1]),
                    Anotacao = rd[2]?.ToString() ?? "",
                    DataHora = Convert.ToDateTime(rd[3]),
                    Usuario = rd[4]?.ToString() ?? "",
                    NovaAgenda = rd[5]?.ToString() ?? "S",
                    AgendaResolvida = rd[7]?.ToString() ?? "N",
                    DataHoraAgenda = rd[6] == DBNull.Value ? null : Convert.ToDateTime(rd[6]),
                    UltimaNotificacaoAgenda = rd[8] == DBNull.Value ? null : Convert.ToDateTime(rd[8]),
                    NotificarAgenda = rd[9]?.ToString() ?? "S"
                });
            return lista;
        }

        public async Task FinalizarAgendaTicketAsync(int codigoDetalhe)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "UPDATE TicketChamadoD SET AgendaResolvida='S',NotificarAgenda='N' WHERE Codigo=@Codigo AND CodEmp=@CodEmp", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigoDetalhe);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AlterarSetorAsync(int ticketId, int novoSetor, string usuarioQueAlterou)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoC
                SET CodSetor=@setor, UsuarioUltimaGravacao=@usuario, DataHoraUltimaGravacao=GETDATE()
                WHERE Codigo=@id AND CodEmp=@CodEmp", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@setor", novoSetor);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@usuario", usuarioQueAlterou.Trim().ToUpper());
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@id", ticketId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarcarComoLidoSuporteAsync(int ticketId, string usuario)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            var usuarioAtual = (usuario ?? "").Trim().ToUpper();
            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoD SET LidoSuporte='S'
                WHERE CodTicketChamadoC=@TicketId AND CodEmp=@CodEmp
                  AND ISNULL(EnvioCliente,'N')='S'
                  AND ISNULL(LidoSuporte,'N')<>'S';
                UPDATE TicketChamadoC SET Novo='N'
                WHERE Codigo=@TicketId
                  AND UPPER(ISNULL(Usuario,''))=UPPER(@Usuario);", con);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@TicketId", ticketId);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuarioAtual);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string> BuscarUltimaAnotacaoClienteAsync(int codTicket)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 Anotacao FROM TicketChamadoD
                WHERE CodTicketChamadoC=@CodTicket AND CodEmp=@CodEmp AND ISNULL(EnvioCliente,'N')='S'
                ORDER BY DataHora DESC, Codigo DESC", con);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            return (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
        }

        public async Task ExcluirMensagemClienteAsync(int codigoDetalhe, string usuarioQueExcluiu)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoD SET MensagemExcluida='S'
                WHERE Codigo=@Codigo AND CodEmp=@CodEmp AND ISNULL(EnvioCliente,'N')<>'S'", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigoDetalhe);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EditarMensagemAsync(int codigoDetalhe, string novaMensagem, string usuarioQueEditou)
        {
            if (string.IsNullOrWhiteSpace(novaMensagem))
                throw new Exception("A mensagem não pode ficar vazia.");

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE TicketChamadoD SET Anotacao=@NovaMensagem, Alterado='S'
                WHERE Codigo=@Codigo AND CodEmp=@CodEmp
                  AND UPPER(ISNULL(Usuario,''))=UPPER(@Usuario)
                  AND ISNULL(EnvioCliente,'N')<>'S'
                  AND ISNULL(MensagemExcluida,'N')<>'S'
                  AND ISNULL(Anotacao,'')<>''
                  AND Audio IS NULL", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Codigo", codigoDetalhe);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@NovaMensagem", novaMensagem.Trim());
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuarioQueEditou.Trim().ToUpper());
            if (await cmd.ExecuteNonQueryAsync() == 0)
                throw new Exception("Essa mensagem não pode ser editada.");
        }

        public async Task RegistrarVisualizacaoTicketAsync(int codTicket, string usuario)
        {
            if (codTicket <= 0 || string.IsNullOrWhiteSpace(usuario)) return;
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE Usuario
                SET TicketVisualizando=@CodTicket, DataHoraVisualizando=GETDATE()
                WHERE UPPER(LTRIM(RTRIM(Usuario)))=UPPER(LTRIM(RTRIM(@Usuario)))
                  AND CodEmp = @CodEmp
                  AND ISNULL(Inativo,'N')='N'", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoverVisualizacaoTicketAsync(int codTicket, string usuario)
        {
            if (codTicket <= 0 || string.IsNullOrWhiteSpace(usuario)) return;
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                UPDATE Usuario
                SET TicketVisualizando=NULL, DataHoraVisualizando=NULL
                WHERE UPPER(LTRIM(RTRIM(Usuario)))=UPPER(LTRIM(RTRIM(@Usuario)))
                  AND CodEmp = @CodEmp
                  AND TicketVisualizando=@CodTicket", conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<TicketVisualizacaoUsuarioModel>> ObterVisualizadoresTicketAsync(int codTicket)
        {
            var lista = new List<TicketVisualizacaoUsuarioModel>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using (var cmdLimpar = new SqlCommand(@"
                UPDATE Usuario SET TicketVisualizando=NULL, DataHoraVisualizando=NULL
                WHERE CodEmp = @CodEmp
                  AND DataHoraVisualizando < DATEADD(SECOND,-45,GETDATE())", conn))
            {
                AddCodEmp(cmdLimpar);
                await cmdLimpar.ExecuteNonQueryAsync();
            }

            using var cmdSelect = new SqlCommand(@"
    SELECT LTRIM(RTRIM(Usuario)), DataHoraVisualizando
    FROM Usuario
    WHERE CodEmp = @CodEmp
      AND TicketVisualizando=@CodTicket
      AND ISNULL(Inativo,'N')='N'
    ORDER BY Usuario", conn);
            AddCodEmp(cmdSelect);
            cmdSelect.Parameters.AddWithValue("@CodTicket", codTicket);

            using var reader = await cmdSelect.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lista.Add(new TicketVisualizacaoUsuarioModel
                {
                    Usuario = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    DataHoraUltimaAtualizacao = reader.GetDateTime(1)
                });
            return lista;
        }

        public async Task<(bool TemConflito, string Mensagem)> VerificarConflitoAgendaTicketAsync(
            string usuarioResponsavel, DateTime dataHoraAgenda, int? ignorarCodigoDetalhe = null)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                SELECT TOP 1 D.DataHoraAgenda, D.CodTicketChamadoC
                FROM TicketChamadoD D
                INNER JOIN TicketChamadoC C ON C.Codigo=D.CodTicketChamadoC AND C.CodEmp=D.CodEmp
                WHERE ISNULL(D.NovaAgenda,'N')='S' AND D.DataHoraAgenda IS NOT NULL
                  AND ISNULL(D.AgendaResolvida,'N')<>'S' AND ISNULL(C.CodSituacao,1)<>3
                  AND UPPER(ISNULL(C.Usuario,''))=UPPER(@Usuario)
                  AND ABS(DATEDIFF(MINUTE,D.DataHoraAgenda,@DataHoraAgenda))<30
                  AND (@IgnorarCodigoDetalhe IS NULL OR D.Codigo<>@IgnorarCodigoDetalhe)
                ORDER BY ABS(DATEDIFF(MINUTE,D.DataHoraAgenda,@DataHoraAgenda)) ASC", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuarioResponsavel.Trim().ToUpper());
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@DataHoraAgenda", dataHoraAgenda);
            AddCodEmp(cmd);
            cmd.Parameters.Add("@IgnorarCodigoDetalhe", SqlDbType.Int).Value =
                ignorarCodigoDetalhe.HasValue ? ignorarCodigoDetalhe.Value : DBNull.Value;

            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                var horario = Convert.ToDateTime(rd["DataHoraAgenda"]);
                var codTkt = Convert.ToInt32(rd["CodTicketChamadoC"]);
                return (true,
                    $"Já existe agenda para {usuarioResponsavel} às {horario:dd/MM/yyyy HH:mm} no ticket #{codTkt}. Mínimo 30 minutos de diferença.");
            }
            return (false, "");
        }

        public async Task<List<WhatsAppContatoDto>> BuscarTelefonesWhatsAppClienteAsync(int codCliente)
        {
            var lista = new List<WhatsAppContatoDto>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        SELECT DISTINCT TOP 20
            -- Prefere UsuarioAbertura se parecer nome de contato
            -- (não é usuário interno do sistema)
            ISNULL(
                NULLIF(
                    CASE
                        -- Se UsuarioAbertura existe e NÃO é um usuário interno,
                        -- usa como nome do contato
                        WHEN LTRIM(RTRIM(ISNULL(UsuarioAbertura,''))) <> ''
                         AND NOT EXISTS (
    SELECT 1 FROM Usuario U
    WHERE U.CodEmp = @CodEmp
      AND UPPER(LTRIM(RTRIM(U.Usuario))) 
        = UPPER(LTRIM(RTRIM(UsuarioAbertura)))
      AND ISNULL(U.Inativo,'N') = 'N'
      AND ISNULL(U.Help,'N') = 'S'
)
                        THEN LTRIM(RTRIM(UsuarioAbertura))
                        ELSE NULL
                    END
                ,''),
                'Contato'
            ) AS Nome,
            TelefoneWhatsApp
        FROM TicketChamadoC
        WHERE CodEmp = @CodEmp
          AND CodCliente = @CodCliente
          AND ISNULL(TelefoneWhatsApp,'') <> ''
        ORDER BY 1", conn);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new WhatsAppContatoDto
                {
                    Nome = rd[0]?.ToString() ?? "",
                    Telefone = rd[1]?.ToString() ?? ""
                });
            return lista;
        }

        public async Task<List<TicketChamadoCModel>> ListarTicketsDoClienteAsync(
            int codCliente, int ticketAtualId)
        {
            var lista = new List<TicketChamadoCModel>();
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            using var cmd = new SqlCommand(@"
                SELECT TOP 100
                    T.Codigo, T.CodSetor, T.CodCliente,
                    C.Nome, C.Apelido, T.Assunto, T.Usuario, T.UsuarioAbertura,
                    T.CodSituacao, T.DataHoraAbertura, T.DataHoraUltimaGravacao, T.Novo,
                    ISNULL(T.CodTipo,1), ISNULL(T.Prioridade,2),
                    F.FotoUrl AS FotoClienteUrl
                FROM TicketChamadoC T
                LEFT JOIN Cliente C ON C.Codigo=T.CodCliente AND C.CodEmp=T.CodEmp
                LEFT JOIN ClienteWhatsAppFoto F
                       ON F.CodEmp = T.CodEmp
                      AND F.Telefone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(T.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', '')
                WHERE T.CodEmp = @CodEmp
                  AND T.CodCliente=@CodCliente AND T.Codigo<>@TicketAtualId
                  AND ISNULL(T.ChatInterno,'N')<>'S'
                ORDER BY ISNULL(T.DataHoraUltimaGravacao,T.DataHoraAbertura) DESC, T.Codigo DESC", con);

            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@TicketAtualId", ticketAtualId);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new TicketChamadoCModel
                {
                    Codigo = rd.GetInt32(0),
                    CodSetor = rd.GetInt32(1),
                    CodCliente = rd.GetInt32(2),
                    NomeCliente = rd.IsDBNull(3) ? null : rd.GetString(3),
                    ApelidoCliente = rd.IsDBNull(4) ? null : rd.GetString(4),
                    Assunto = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Usuario = rd.IsDBNull(6) ? null : rd.GetString(6),
                    UsuarioAbertura = rd.IsDBNull(7) ? null : rd.GetString(7),
                    CodSituacao = rd.GetInt32(8),
                    DataHoraAbertura = rd.GetDateTime(9),
                    DataHoraUltimaGravacao = rd.IsDBNull(10) ? rd.GetDateTime(9) : rd.GetDateTime(10),
                    Novo = rd.IsDBNull(11) ? "N" : rd.GetString(11),
                    CodTipo = rd.GetInt32(12),
                    Prioridade = rd.GetInt32(13),
                    FotoClienteUrl = rd.IsDBNull(14) ? null : rd.GetString(14)
                });
            return lista;
        }

        // Mantido para compatibilidade com outros pontos que ainda chamem este método
        private async Task<string> MontarHistoricoTicketAsync(SqlConnection con, int codTicket)
        {
            using var cmd = new SqlCommand(@"
                SELECT TOP 10 Usuario, Anotacao, DataHora, EnvioCliente, StatusWhatsApp
                FROM TicketChamadoD
                WHERE CodTicketChamadoC=@CodTicket AND CodEmp=@CodEmp
                ORDER BY DataHora DESC, Codigo DESC", con);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodTicket", codTicket);

            var linhas = new List<string>();
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                linhas.Add(
                    $"{rd["Usuario"]} | EnvioCliente={rd["EnvioCliente"]} | Status={rd["StatusWhatsApp"]}: {rd["Anotacao"]}");

            linhas.Reverse();
            return string.Join(Environment.NewLine, linhas);
        }

        /// <summary>
        /// Mantido para compatibilidade com chamadas externas que usem o nome antigo.
        /// Internamente delega para SalvarMensagemAutomaticaAsync com status 'pending'.
        /// 
        /// ATENÇÃO: prefira chamar SalvarMensagemAutomaticaAsync diretamente com
        /// 'saudacao_auto' ou 'aguarde_auto' para evitar que apareça como mensagem
        /// pendente de envio no histórico.
        /// </summary>
        private async Task InserirRespostaIaAsync(SqlConnection con, int codTicket, string resposta)
        {
            // Tenta atualizar a trava ia_processing existente
            using (var cmdUpd = new SqlCommand(@"
                UPDATE TicketChamadoD
                SET Anotacao       = @Resposta,
                    DataHora       = GETDATE(),
                    Usuario        = 'EvolutTech',
                    EnvioCliente   = 'N',
                    LidoCliente    = 'N',
                    LidoSuporte    = 'S',
                    StatusWhatsApp = 'pending'
                WHERE Codigo = (
                    SELECT TOP 1 Codigo FROM TicketChamadoD
                    WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
                      AND ISNULL(StatusWhatsApp,'') = 'ia_processing'
                    ORDER BY DataHora DESC, Codigo DESC
                )
                  AND CodEmp = @CodEmp", con))
            {
                AddCodEmp(cmdUpd);
                cmdUpd.Parameters.AddWithValue("@CodTicket", codTicket);
                AddCodEmp(cmdUpd);
                cmdUpd.Parameters.AddWithValue("@Resposta", resposta);
                if (await cmdUpd.ExecuteNonQueryAsync() > 0) return;
            }

            // Se não havia trava, insere diretamente
            using var cmdIns = new SqlCommand(@"
                INSERT INTO TicketChamadoD
                (CodEmp, CodTicketChamadoC, Anotacao, DataHora, Usuario,
                 EnvioCliente, LidoCliente, LidoSuporte, StatusWhatsApp, MensagemExcluida)
                VALUES
                (@CodEmp, @CodTicket, @Resposta, GETDATE(), 'EvolutTech',
                 'N', 'N', 'S', 'pending', 'N')", con);

            AddCodEmp(cmdIns);
            cmdIns.Parameters.AddWithValue("@CodTicket", codTicket);
            AddCodEmp(cmdIns);
            cmdIns.Parameters.AddWithValue("@Resposta", resposta);
            await cmdIns.ExecuteNonQueryAsync();
        }
        // ── Log interno no banco ────────────────────────────────────────────
        // Tabela criada via script SQL (ver abaixo).
        // Para consultar: SELECT * FROM WhatsAppSaudacaoLog ORDER BY DataHora DESC
        private void Log(string mensagem)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var con = new SqlConnection(_conn);
                    await con.OpenAsync();
                    await using var cmd = new SqlCommand(@"
                INSERT INTO WhatsAppSaudacaoLog (CodEmp, DataHora, Mensagem)
                VALUES (@CodEmp, GETDATE(), @Msg)", con);
                    AddCodEmp(cmd);
                    cmd.Parameters.AddWithValue("@Msg",
                        mensagem?.Length > 4000 ? mensagem.Substring(0, 4000) : (mensagem ?? ""));
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { }
            });
        }

        private async Task AnalisarContextoClienteAsync(int codTicket, int codEmp)
        {
            try
            {
                using var con = new SqlConnection(_conn);
                await con.OpenAsync();

                // Busca todas as mensagens do cliente neste ticket
                var mensagens = new List<string>();
                using (var cmd = new SqlCommand(@"
    SELECT TOP 20 Anotacao
    FROM TicketChamadoD
    WHERE CodTicketChamadoC = @CodTicket AND CodEmp = @CodEmp
      AND ISNULL(EnvioCliente,'N') = 'S'
      AND ISNULL(MensagemExcluida,'N') = 'N'
      AND ISNULL(Anotacao,'') <> ''
    ORDER BY DataHora ASC", con))
                {
                    AddCodEmp(cmd, codEmp);
                    cmd.Parameters.AddWithValue("@CodTicket", codTicket);
                    using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                        mensagens.Add(rd.GetString(0));
                }

                if (!mensagens.Any()) return;

                var contexto = string.Join("\n", mensagens);
                var (assunto, sentimento, emoji) = await _openAi.AnalisarContextoClienteAsync(contexto);

                // Salva no ticket
                using var cmdUpd = new SqlCommand(@"
            UPDATE TicketChamadoC
            SET AssuntoSugerido = @Assunto,
                SentimentoCliente = @Sentimento,
                SentimentoEmoji = @Emoji,
                AssuntoSugeridoStatus = 'pendente'
            WHERE Codigo = @CodTicket AND CodEmp = @CodEmp
              AND ISNULL(AssuntoSugeridoStatus,'') <> 'aplicado'", con);

                AddCodEmp(cmdUpd, codEmp);
                cmdUpd.Parameters.AddWithValue("@CodTicket", codTicket);
                cmdUpd.Parameters.AddWithValue("@Assunto", assunto);
                cmdUpd.Parameters.AddWithValue("@Sentimento", sentimento);
                cmdUpd.Parameters.AddWithValue("@Emoji", emoji);
                await cmdUpd.ExecuteNonQueryAsync();

                Log($"[ANALISE] Ticket #{codTicket} | Assunto: {assunto} | Sentimento: {sentimento} {emoji}");
                _monitor.Log(LogCategory.Tickets, LogSeverity.Info,    // ← ADD
                    $"Contexto analisado ticket #{codTicket}",
                    $"assunto={assunto} sentimento={sentimento} {emoji}");
            }
            catch (Exception ex)
            {
                Log($"[ANALISE] ERRO ticket #{codTicket}: {ex.Message}");
                _monitor.LogException(LogCategory.Tickets, ex,    // ← ADD
                    $"AnalisarContextoCliente ticket #{codTicket}");
            }
        }

        public async Task<(bool Sucesso, string Mensagem)> SalvarCabecalhoTicketAsync(
    TicketChamadoCModel ticket,
    string usuarioQueSalvou)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            string usuario = (usuarioQueSalvou ?? "").Trim().ToUpper();

            using var cmdUpdate = new SqlCommand(@"
UPDATE TicketChamadoC
SET
    CodCliente = @CodCliente,
    Assunto = @Assunto,
    ObservacaoCliente = @ObservacaoCliente,
    CodSituacao = @Situacao,
    CodSetor = @CodSetor,
    CodTipo = @CodTipo,
    Prioridade = @Prioridade,
    Usuario = @Usuario,
    Versao = @Versao,
    UsuarioUltimaGravacao = @UsuarioQueSalvou,
    DataHoraUltimaGravacao = GETDATE(),
    Novo = CASE
    -- Responsável é o mesmo que está salvando → não novo
    WHEN UPPER(@UsuarioQueSalvou) = UPPER(@Usuario) THEN 'N'
    -- Mudou o responsável para outro atendente interno → NOVO para o novo responsável
    WHEN UPPER(@UsuarioQueSalvou) NOT IN ('WHATSAPP','CLIENTE','BOT','SISTEMA','NOVO')
     AND UPPER(@Usuario) NOT IN ('WHATSAPP','CLIENTE','BOT','SISTEMA','NOVO')
     AND UPPER(@UsuarioQueSalvou) <> UPPER(@Usuario) THEN 'S'
    ELSE 'N'
END
WHERE Codigo = @Id AND CodEmp = @CodEmp", conn);

            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Id", ticket.Codigo);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@CodCliente", ticket.CodCliente);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Assunto", ticket.Assunto ?? "");
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@ObservacaoCliente", ticket.ObservacaoCliente ?? "");
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Situacao", ticket.CodSituacao);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@CodSetor", ticket.CodSetor);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@CodTipo", ticket.CodTipo);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Prioridade", ticket.Prioridade <= 0 ? 2 : ticket.Prioridade);
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Usuario", ticket.Usuario ?? "");
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@Versao", ticket.Versao ?? "");
            AddCodEmp(cmdUpdate);
            cmdUpdate.Parameters.AddWithValue("@UsuarioQueSalvou", usuario);

            await cmdUpdate.ExecuteNonQueryAsync();

            return (true, "Ticket salvo com sucesso.");
        }

        public async Task<(int Total, int UltimoCodigo)> ResumoHistoricoAsync(int id)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(@"
        SELECT COUNT(1), ISNULL(MAX(Codigo),0)
        FROM TicketChamadoD
        WHERE CodEmp = @CodEmp
          AND CodTicketChamadoC = @Id
          AND ISNULL(StatusWhatsApp,'') <> 'ia_processing'", con);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Id", id);
            using var rd = await cmd.ExecuteReaderAsync();
            await rd.ReadAsync();
            return (rd.GetInt32(0), rd.GetInt32(1));
        }

        public async Task MarcarLidoNoWhatsAppAsync(int codTicket, int codInstancia)
        {
            try
            {
                // Busca o waid da última mensagem do cliente não lida
                string? waid = null;

                using var con = new SqlConnection(_conn);
                await con.OpenAsync();

                using var cmd = new SqlCommand(@"
            SELECT TOP 1 WaidWhatsApp
            FROM TicketChamadoD
            WHERE CodEmp = @CodEmp
              AND CodTicketChamadoC = @CodTicket
              AND ISNULL(EnvioCliente, 'N') = 'S'
              AND WaidWhatsApp IS NOT NULL
              AND WaidWhatsApp <> ''
            ORDER BY DataHora DESC, Codigo DESC", con);

                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@CodTicket", codTicket);
                var r = await cmd.ExecuteScalarAsync();

                if (r == null || r == DBNull.Value)
                    return;

                waid = r.ToString();

                var payload = new { waid, cod_instancia = codInstancia };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                await http.PostAsync("http://localhost:3333/marcar-lido", content);
            }
            catch
            {
                // Silencia erros — marcar lido é secundário, não pode travar o carregamento
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Adicionar dentro da classe TicketService (TicketService.cs)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Cria um card do CRM a partir de um ticket de suporte.
        /// Copia cabeçalho + resumo do histórico como primeira anotação.
        /// Não cria nenhum vínculo: os dois registros vivem de forma independente.
        /// </summary>
        public async Task<int> CriarCardAPartirDoTicketAsync(
            int codTicket,
            string usuarioResponsavel,   // "NOVO" ou nome do atendente
            int empresa)
        {
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            // ── 1. Lê cabeçalho do ticket ─────────────────────────────────────────
            string assunto = "", telefone = "", nomeCliente = "";
            int codCliente = 0;

            using (var cmd = new SqlCommand(@"
        SELECT
            ISNULL(T.Assunto,''),
            ISNULL(T.TelefoneWhatsApp,''),
            ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)),''), ISNULL(C.Nome,'')),
            ISNULL(T.CodCliente, 0)
        FROM TicketChamadoC T
        LEFT JOIN Cliente C ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
        WHERE T.Codigo = @Cod
          AND T.CodEmp = @CodEmp", conn))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", codTicket);
                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    throw new Exception("Ticket não encontrado.");
                assunto = rd.GetString(0);
                telefone = rd.GetString(1);
                nomeCliente = rd.GetString(2);
                codCliente = rd.GetInt32(3);
            }

            // ── 2. Lê histórico de anotações (últimas 20, ordem cronológica) ──────
            var linhasHistorico = new List<string>();
            using (var cmd = new SqlCommand(@"
        SELECT TOP 20
            ISNULL(Usuario,'?'),
            ISNULL(Anotacao,''),
            DataHora,
            ISNULL(EnvioCliente,'N')
        FROM TicketChamadoD
        WHERE CodEmp = @CodEmp
          AND CodTicketChamadoC = @Cod
          AND ISNULL(MensagemExcluida,'N') = 'N'
          AND ISNULL(StatusWhatsApp,'') <> 'ia_processing'
          AND ISNULL(Anotacao,'') <> ''
        ORDER BY DataHora ASC", conn))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", codTicket);
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var origem = rd.GetString(3) == "S" ? "Cliente" : rd.GetString(0);
                    var texto = rd.GetString(1);
                    var data = rd.GetDateTime(2).ToString("dd/MM/yyyy HH:mm");
                    if (texto.Length > 300) texto = texto[..300] + "...";
                    linhasHistorico.Add($"[{data}] {origem}: {texto}");
                }
            }

            var resumo = linhasHistorico.Count > 0
                ? "📋 Histórico do Suporte:\n" + string.Join("\n", linhasHistorico)
                : "📋 Ticket sem histórico de anotações.";

            var descricaoCard = !string.IsNullOrWhiteSpace(nomeCliente)
                ? nomeCliente
                : assunto;

            if (descricaoCard.Length > 200)
                descricaoCard = descricaoCard[..200];

            var responsavel = string.IsNullOrWhiteSpace(usuarioResponsavel)
                ? "NOVO"
                : usuarioResponsavel.Trim().ToUpper();

            // ── 3. Insere CRMC ────────────────────────────────────────────────────
            int codCard;
            using (var cmd = new SqlCommand(@"
            DECLARE @NovoCard TABLE (Codigo INT);

            INSERT INTO CRMC
            (
                Descricao, CodCliente, NomeCliente,
                Telefone, Celular, TelefoneWhatsApp,
                Whats, Ligacao, Telegram,
                DataCriacao, DataHoraUltimaGravacao,
                UsuarioCard, Status, Funil,
                Novo, CodEmp
            )
            OUTPUT INSERTED.Codigo INTO @NovoCard
            VALUES
            (
                @Descricao, @CodCliente, @NomeCliente,
                @Telefone, @Telefone, @Telefone,
                'N', 'N', 'N',
                GETDATE(), GETDATE(),
                @Usuario, 'ABERTO', 'RA',
                'S', @Empresa
            );

            SELECT Codigo FROM @NovoCard;", conn))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Descricao", descricaoCard);
                AddCodEmp(cmd);
                cmd.Parameters.Add("@CodCliente", System.Data.SqlDbType.Int).Value =
                    codCliente > 0 ? codCliente : System.DBNull.Value;
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@NomeCliente", nomeCliente);
                AddCodEmp(cmd);
                cmd.Parameters.Add("@Telefone", System.Data.SqlDbType.VarChar, 50).Value =
                    string.IsNullOrWhiteSpace(telefone) ? System.DBNull.Value : (object)telefone;
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Usuario", responsavel);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Empresa", empresa);

                codCard = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // ── 4. Insere anotação inicial com resumo do ticket ───────────────────
            using (var cmd = new SqlCommand(@"
            INSERT INTO CRMAnotacao
            (
                CodEmp, CodCRMC, DataHora, Anotacao, Funil, Usuario,
                LidoCliente, LidoSuporte, Alterado, MensagemExcluida,
                EnvioCliente, StatusWhatsApp, WhatsAppEnviado
            )
            VALUES
            (
                @CodEmp, @Cod, GETDATE(), @Texto, 'RA', @Usuario,
                'N', 'S', 'N', 'N',
                'N', 'interno', 'S'
            )", conn))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", codCard);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Texto", resumo);
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Usuario", responsavel);
                await cmd.ExecuteNonQueryAsync();
            }

            _monitor.Log(LogCategory.CRM, LogSeverity.Success,    // ← ADD
    $"Card CRM #{codCard} criado a partir do ticket #{codTicket}",
    $"responsavel={responsavel}");
            return codCard;
        }

        /// <summary>
        /// Quando o atendente vincula manualmente um cliente a um ticket de WhatsApp,
        /// propaga o vínculo para todos os tickets antigos do mesmo número
        /// e grava o celular no cadastro do cliente, se estiver vazio.
        /// </summary>
        public async Task PropagarClientePorTelefoneAsync(int codTicket, int codCliente)
        {
            if (codTicket <= 0 || codCliente <= 0) return;

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            string telefone = "";
            using (var cmd = new SqlCommand(
                "SELECT ISNULL(TelefoneWhatsApp,'') FROM TicketChamadoC WHERE Codigo = @Cod AND CodEmp = @CodEmp", con))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", codTicket);
                telefone = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(telefone)) return;

            // Variantes só para BUSCAR. O valor gravado permanece intocado.
            var variantes = GerarVariantesTelefone(SoDigitos(telefone));
            if (!variantes.Any()) return;

            var pn = variantes.Select((_, i) => "@T" + i).ToList();

            // 1. Vincula tickets órfãos com o mesmo número
            using (var cmdUpd = new SqlCommand($@"
        UPDATE TicketChamadoC
        SET CodCliente = @CodCliente
        WHERE CodEmp = @CodEmp
          AND ISNULL(CodCliente,0) = 0
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                  ISNULL(TelefoneWhatsApp,''),
              '(',''),')',''),'-',''),' ',''),'+','')
              IN ({string.Join(",", pn)})", con))
            {
                AddCodEmp(cmdUpd);
                cmdUpd.Parameters.AddWithValue("@CodCliente", codCliente);

                for (int i = 0; i < variantes.Count; i++)
                {
                    cmdUpd.Parameters.AddWithValue(pn[i], variantes[i]);
                }

                var n = await cmdUpd.ExecuteNonQueryAsync();
                Log($"[VINCULO] {n} ticket(s) vinculados ao cliente {codCliente} pelo telefone {telefone}");
            }

            // 2. REMOVIDO: o UPDATE que reescrevia TelefoneWhatsApp com o número
            //    normalizado. Ele destruía o JID real da conversa e as respostas
            //    passavam a não chegar. As variantes já cobrem a busca.

            // 3. Grava o celular no cadastro só se estiver vazio
            using (var cmdCli = new SqlCommand(@"
        UPDATE Cliente
        SET Celular = @Celular
        WHERE Codigo = @CodCliente
          AND CodEmp = @CodEmp
          AND LTRIM(RTRIM(ISNULL(Celular,''))) = ''", con))
            {
                AddCodEmp(cmdCli);
                cmdCli.Parameters.AddWithValue("@CodCliente", codCliente);
                AddCodEmp(cmdCli);
                cmdCli.Parameters.AddWithValue("@Celular", SoDigitos(telefone));
                await cmdCli.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Retorna o JID real da conversa: o número que o WhatsApp de fato usa.
        /// Fonte de verdade = última mensagem RECEBIDA do cliente neste ticket.
        /// Fallback = dígitos crus do cabeçalho. NUNCA normaliza:
        /// normalizar aqui gera número inexistente e a resposta não chega.
        /// </summary>
        private async Task<string> ObterJidDestinoAsync(
            SqlConnection conn, int codTicket, string? telefoneCabecalho)
        {
            using (var cmd = new SqlCommand(@"
SELECT TOP 1 TelefoneWhatsApp
FROM TicketChamadoD
WHERE CodEmp = @CodEmp
  AND CodTicketChamadoC = @Cod
  AND ISNULL(EnvioCliente,'N') = 'S'
  AND ISNULL(TelefoneWhatsApp,'') <> ''
ORDER BY DataHora DESC, Codigo DESC", conn))
            {
                AddCodEmp(cmd);
                cmd.Parameters.AddWithValue("@Cod", codTicket);
                var r = await cmd.ExecuteScalarAsync();
                var jid = SoDigitos(r?.ToString());
                if (jid.Length >= 12) return jid;
            }

            return SoDigitos(telefoneCabecalho);
        }

        public async Task<List<MensagemPresaModel>> BuscarMensagensPresasAsync(string usuario)
        {
            var lista = new List<MensagemPresaModel>();

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
SELECT
    D.Codigo,
    D.CodTicketChamadoC,
    D.Anotacao,
    D.StatusWhatsApp,
    ISNULL(C.Assunto, '') AS NomeCliente,
    ISNULL(D.MotivoErroEnvio, '') AS MotivoErroEnvio
FROM TicketChamadoD D
INNER JOIN TicketChamadoC C ON C.Codigo = D.CodTicketChamadoC AND C.CodEmp = D.CodEmp
WHERE D.CodEmp = @CodEmp
  AND UPPER(ISNULL(D.Usuario, '')) = @Usuario
  AND ISNULL(D.EnvioCliente, 'N') = 'N'
  AND ISNULL(D.StatusWhatsApp, '') NOT IN (
        'sent', 'sent_external', 'read', 'delivered',
        'deleted', 'edited', 'interno', 'ia_processing',
        'descartado', 'pending', 'transcrevendo', 'transcricao_erro'
      )
  AND ISNULL(D.MensagemExcluida, 'N') = 'N'
  AND D.DataHora <= DATEADD(MINUTE, -2, GETDATE())
  AND D.DataHora >= DATEADD(HOUR, -24, GETDATE())
  AND ISNULL(C.CodSituacao, 1) <> 3
  AND ISNULL(C.CodSetor, 1) = 1
  AND ISNULL(C.CodSituacao, 1) = 1
  AND D.TelefoneWhatsApp IS NOT NULL
  AND (                                          -- ✅ filtro de tipo
      (D.CodTipo = 1 AND D.ChatTipo = 'user')
      OR
      (D.CodTipo = 5 AND D.ChatInterno IS NULL)
  )
ORDER BY D.DataHora ASC", conn);

            AddCodEmp(cmd);
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = usuario;

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new MensagemPresaModel
                {
                    Codigo = rd.GetInt32(0),
                    CodTicketChamadoC = rd.GetInt32(1),
                    Anotacao = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    StatusWhatsApp = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    NomeCliente = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    MotivoErroEnvio = rd.IsDBNull(5) ? "" : rd.GetString(5)
                });
            }

            return lista;
        }

        private static string SoDigitos(string? s) =>
            new string((s ?? "").Where(char.IsDigit).ToArray());

        //public async Task AplicarAssuntoSugeridoAsync(int codTicket, string novoAssunto)
        //{
        //    using var con = new SqlConnection(_conn);
        //    await con.OpenAsync();
        //    using var cmd = new SqlCommand(@"
        //UPDATE TicketChamadoC
        //SET Assunto = @Assunto,
        //    AssuntoSugeridoStatus = 'aplicado'
        //WHERE Codigo = @CodTicket AND CodEmp = @CodEmp", con);
        //    cmd.Parameters.AddWithValue("@CodTicket", codTicket);
        //    cmd.Parameters.AddWithValue("@Assunto", novoAssunto);
        //    await cmd.ExecuteNonQueryAsync();
        //}

        //public async Task IgnorarAssuntoSugeridoAsync(int codTicket)
        //{
        //    using var con = new SqlConnection(_conn);
        //    await con.OpenAsync();
        //    using var cmd = new SqlCommand(@"
        //UPDATE TicketChamadoC
        //SET AssuntoSugeridoStatus = 'ignorado'
        //WHERE Codigo = @CodTicket AND CodEmp = @CodEmp", con);
        //    cmd.Parameters.AddWithValue("@CodTicket", codTicket);
        //    await cmd.ExecuteNonQueryAsync();
        //}
    }
}








