using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Data;

namespace EvolutCRM.Mobile
{
    /// <summary>
    /// Consultas que só o HELP Mobile usa (lista estilo WhatsApp e inscrições de push).
    /// Tudo o que é regra de ticket (envio, leitura, histórico) continua no TicketService.
    /// </summary>
    public class MobileRepository
    {
        private readonly string _conn;

        public MobileRepository(IConfiguration config)
        {
            _conn = config.GetConnectionString("Connection")!;
        }

        // Usuários que não são atendentes de verdade
        internal const string UsuariosAutomaticosSql = "'', 'NOVO', 'WHATSAPP', 'CLIENTE', 'BOT', 'SISTEMA', 'IA', 'EVOLUTTECH'";

        // Ticket "sem responsável" = na fila: mesma regra do MainLayout do HELP desktop
        internal const string FilaSql = "'', 'NOVO', 'WHATSAPP', 'CLIENTE'";

        // ───────────────────────────── Usuário ─────────────────────────────

        private static bool _colunaMobileExiste = true;

        /// <summary>
        /// Confere se o usuário do token continua ativo e com acesso ao HELP e registra que ele está
        /// usando o app agora (Usuario.UltimaAtividadeMobile). O OfflineTicketReassignService usa isso
        /// para não devolver para a fila os tickets de quem está atendendo pelo celular.
        /// </summary>
        public async Task<bool> UsuarioAtivoAsync(string usuario, int codEmp)
        {
            // O app chama a API a cada 3s com a conversa aberta. Conferir e gravar no banco em toda
            // chamada pesava (UPDATE na tabela Usuario disputando com o HELP desktop), então a
            // confirmação vale por alguns segundos. Fica bem abaixo dos 90s que o
            // OfflineTicketReassignService usa para considerar o usuário ativo no celular.
            var chave = $"{codEmp}|{usuario.Trim().ToUpperInvariant()}";
            if (_confirmados.TryGetValue(chave, out var quando) && DateTime.UtcNow - quando < ValidadeConfirmacao)
                return true;

            var ativo = await UsuarioAtivoBancoAsync(usuario, codEmp);
            if (ativo) _confirmados[chave] = DateTime.UtcNow;
            else _confirmados.TryRemove(chave, out _);
            return ativo;
        }

        private static readonly ConcurrentDictionary<string, DateTime> _confirmados = new();
        private static readonly TimeSpan ValidadeConfirmacao = TimeSpan.FromSeconds(40);

        private async Task<bool> UsuarioAtivoBancoAsync(string usuario, int codEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            if (_colunaMobileExiste)
            {
                try
                {
                    using var upd = new SqlCommand(@"
UPDATE Usuario
SET UltimaAtividadeMobile = GETDATE()
WHERE CodEmp = @CodEmp
  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
  AND ISNULL(Inativo, 'N') = 'N'
  AND ISNULL(Help, 'N') = 'S';
SELECT @@ROWCOUNT;", con);
                    upd.Parameters.AddWithValue("@CodEmp", codEmp);
                    upd.Parameters.AddWithValue("@Usuario", usuario);
                    return Convert.ToInt32(await upd.ExecuteScalarAsync()) > 0;
                }
                catch (SqlException ex) when (ex.Number == 207)
                {
                    // Coluna ainda não criada (script 2026-10-14_MobileAtividade.sql): segue só conferindo o usuário
                    _colunaMobileExiste = false;
                }
            }

            using var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM Usuario
WHERE CodEmp = @CodEmp
  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
  AND ISNULL(Inativo, 'N') = 'N'
  AND ISNULL(Help, 'N') = 'S'", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        public async Task<string> NomeEmpresaAsync(int codEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 ISNULL(NomeReduzido, Fantasia) FROM Empresa WHERE Codigo = @CodEmp", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            return (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
        }

        public async Task<string> DescricaoSituacaoAsync(int codEmp, int codSituacao)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 ISNULL(Descricao, '') FROM TicketSituacaoCRM WHERE CodEmp = @CodEmp AND Codigo = @Cod", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Cod", codSituacao);
            return (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
        }

        // ───────────────────────────── Lista de conversas ─────────────────────────────

        /// <summary>
        /// Tickets de Suporte em aberto do usuário + os da fila (sem finalizados e sem chat interno),
        /// com a última mensagem e quantas mensagens do cliente estão sem resposta.
        /// Ordenado pela última mensagem, como no WhatsApp.
        /// </summary>
        public async Task<List<MobileTicketItemDto>> ListarConversasAsync(int codEmp, string usuario, bool todos = false)
        {
            var lista = new List<MobileTicketItemDto>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            var sql = $@"
SELECT
    T.Codigo,
    ISNULL(T.Assunto, '')                                   AS Assunto,
    LTRIM(RTRIM(ISNULL(T.Usuario, '')))                     AS Responsavel,
    ISNULL(T.CodSituacao, 1)                                AS CodSituacao,
    ISNULL(S.Descricao, '')                                 AS Situacao,
    ISNULL(T.Novo, 'N')                                     AS Novo,
    ISNULL(T.Prioridade, 2)                                 AS Prioridade,
    ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)), ''), ISNULL(C.Nome, '')) AS Nome,
    ISNULL(T.TelefoneWhatsApp, '')                          AS Telefone,
    F.FotoUrl,
    U.Codigo                                                AS UltCodigo,
    LEFT(ISNULL(U.Anotacao, ''), 220)                       AS UltTexto,
    U.DataHora                                              AS UltDataHora,
    ISNULL(U.EnvioCliente, 'N')                             AS UltDoCliente,
    ISNULL(U.Usuario, '')                                   AS UltUsuario,
    ISNULL(U.TemImagem, 0)                                  AS UltTemImagem,
    ISNULL(U.TemAudio, 0)                                   AS UltTemAudio,
    ISNULL(U.TemVideo, 0)                                   AS UltTemVideo,
    ISNULL(U.Interno, 'N')                                  AS UltInterno,
    ISNULL(U.StatusWhatsApp, '')                            AS UltStatus,
    ISNULL(U.MensagemExcluida, 'N')                         AS UltExcluida,
    ISNULL(N.Qtd, 0)                                        AS NaoRespondidas,
    ISNULL(L.Qtd, 0)                                        AS NaoLidas,
    ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura)    AS DataRef
FROM TicketChamadoC T
LEFT JOIN Cliente C
       ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp
LEFT JOIN ClienteWhatsAppFoto F
       ON F.CodEmp = T.CodEmp
      AND F.Telefone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(T.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', '')
LEFT JOIN TicketSituacaoCRM S
       ON S.CodEmp = T.CodEmp AND S.Codigo = T.CodSituacao
OUTER APPLY (
    SELECT TOP 1
        D.Codigo, D.Anotacao, D.DataHora, D.EnvioCliente, D.Usuario,
        CASE WHEN D.Imagem IS NOT NULL OR ISNULL(D.CaminhoImagem, '') <> '' THEN 1 ELSE 0 END AS TemImagem,
        CASE WHEN D.Audio IS NOT NULL THEN 1 ELSE 0 END AS TemAudio,
        CASE WHEN D.Video IS NOT NULL THEN 1 ELSE 0 END AS TemVideo,
        D.Interno, D.StatusWhatsApp, D.MensagemExcluida
    FROM TicketChamadoD D
    WHERE D.CodTicketChamadoC = T.Codigo
      AND D.CodEmp = T.CodEmp
      AND ISNULL(D.StatusWhatsApp, '') <> 'ia_processing'
    ORDER BY D.Codigo DESC
) U
OUTER APPLY (
    SELECT COUNT(1) AS Qtd
    FROM TicketChamadoD D2
    WHERE D2.CodTicketChamadoC = T.Codigo
      AND D2.CodEmp = T.CodEmp
      AND ISNULL(D2.EnvioCliente, 'N') = 'S'
      AND ISNULL(D2.MensagemExcluida, 'N') <> 'S'
      AND D2.Codigo > ISNULL((
            SELECT MAX(D3.Codigo)
            FROM TicketChamadoD D3
            WHERE D3.CodTicketChamadoC = T.Codigo
              AND D3.CodEmp = T.CodEmp
              AND ISNULL(D3.EnvioCliente, 'N') = 'N'
              AND ISNULL(D3.Interno, 'N') <> 'S'
              AND ISNULL(D3.StatusWhatsApp, '') NOT IN ('saudacao_auto', 'aguarde_auto', 'ia_processing')
              AND UPPER(LTRIM(RTRIM(ISNULL(D3.Usuario, '')))) NOT IN ({UsuariosAutomaticosSql})
          ), 0)
) N
OUTER APPLY (
    SELECT COUNT(1) AS Qtd
    FROM TicketChamadoD DL
    WHERE DL.CodTicketChamadoC = T.Codigo
      AND DL.CodEmp = T.CodEmp
      AND ISNULL(DL.EnvioCliente, 'N') = 'S'
      AND ISNULL(DL.MensagemExcluida, 'N') <> 'S'
      AND ISNULL(DL.LidoSuporte, 'N') <> 'S'
) L
WHERE T.CodEmp = @CodEmp
  AND ISNULL(T.CodSetor, 1) = 1          -- só Suporte (Desenvolvimento = 2 fica fora do mobile por enquanto)
  -- Padrão igual ao HELP desktop: os tickets do usuário + os sem responsável (fila).
  -- @Todos = 1 (aba Equipe do app): tickets em aberto de todos os usuários.
  AND (
        @Todos = 1
        OR UPPER(LTRIM(RTRIM(ISNULL(T.Usuario, '')))) = UPPER(@Usuario)
        OR UPPER(LTRIM(RTRIM(ISNULL(T.Usuario, '')))) IN ({FilaSql})
      )
  AND ISNULL(T.CodSituacao, 1) = 1        -- só situação Suporte (tira Desenvolvimento, Atualizar, Pendente, Aprovação e Finalizado)
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
ORDER BY ISNULL(U.DataHora, ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura)) DESC";

            using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Usuario", (usuario ?? "").Trim());
            cmd.Parameters.AddWithValue("@Todos", todos ? 1 : 0);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var temImagem = Convert.ToInt32(rd["UltTemImagem"]) == 1;
                var temAudio = Convert.ToInt32(rd["UltTemAudio"]) == 1;
                var temVideo = Convert.ToInt32(rd["UltTemVideo"]) == 1;
                var interno = rd["UltInterno"]?.ToString() == "S";
                var excluida = rd["UltExcluida"]?.ToString() == "S";
                var texto = rd["UltTexto"]?.ToString() ?? "";

                var tipo = interno ? "interno"
                         : temAudio ? "audio"
                         : temVideo ? "video"
                         : temImagem ? "imagem"
                         : "texto";

                if (excluida) { tipo = "texto"; texto = "Mensagem apagada"; }

                var telefone = rd["Telefone"]?.ToString() ?? "";
                var nome = rd["Nome"]?.ToString() ?? "";

                lista.Add(new MobileTicketItemDto
                {
                    Codigo = rd.GetInt32(rd.GetOrdinal("Codigo")),
                    Assunto = rd["Assunto"]?.ToString() ?? "",
                    Responsavel = rd["Responsavel"]?.ToString() ?? "",
                    CodSituacao = Convert.ToInt32(rd["CodSituacao"]),
                    Situacao = rd["Situacao"]?.ToString() ?? "",
                    Novo = rd["Novo"]?.ToString() == "S",
                    Prioridade = Convert.ToInt32(rd["Prioridade"]),
                    Nome = string.IsNullOrWhiteSpace(nome) ? FormatarTelefone(telefone) : nome.Trim(),
                    Telefone = telefone,
                    FotoUrl = rd["FotoUrl"] == DBNull.Value ? null : rd["FotoUrl"]?.ToString(),
                    Previa = LimparPrevia(texto),
                    PreviaTipo = tipo,
                    PreviaDoCliente = rd["UltDoCliente"]?.ToString() == "S",
                    PreviaUsuario = rd["UltUsuario"]?.ToString()?.Trim() ?? "",
                    PreviaStatus = rd["UltStatus"]?.ToString() ?? "",
                    DataHora = rd["UltDataHora"] != DBNull.Value
                        ? Convert.ToDateTime(rd["UltDataHora"])
                        : (rd["DataRef"] != DBNull.Value ? Convert.ToDateTime(rd["DataRef"]) : DateTime.Now),
                    NaoRespondidas = Convert.ToInt32(rd["NaoRespondidas"]),
                    NaoLidas = Convert.ToInt32(rd["NaoLidas"])
                });
            }

            return lista;
        }

        private static string LimparPrevia(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            var t = texto.Replace("\r", " ").Replace("\n", " ").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t.Length > 160 ? t[..160] + "…" : t;
        }

        internal static string FormatarTelefone(string telefone)
        {
            var d = new string((telefone ?? "").Where(char.IsDigit).ToArray());
            if (d.StartsWith("55") && d.Length >= 12) d = d[2..];
            return d.Length switch
            {
                11 => $"({d[..2]}) {d.Substring(2, 5)}-{d[7..]}",
                10 => $"({d[..2]}) {d.Substring(2, 4)}-{d[6..]}",
                0 => "Sem nome",
                _ => telefone ?? ""
            };
        }

        // ───────────────────────────── Conversa ─────────────────────────────

        /// <summary>
        /// Status de entrega (WhatsApp) só das nossas mensagens a partir de <paramref name="desde"/>.
        /// O app só tem as últimas mensagens carregadas, não precisa do ticket inteiro a cada 3s.
        /// </summary>
        public async Task<Dictionary<int, string>> StatusRecentesAsync(int codEmp, int codTicket, int desde)
        {
            var mapa = new Dictionary<int, string>();
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(@"
SELECT Codigo, ISNULL(StatusWhatsApp, '')
FROM TicketChamadoD
WHERE CodTicketChamadoC = @Id AND CodEmp = @CodEmp
  AND Codigo >= @Desde
  AND ISNULL(StatusWhatsApp, '') <> 'ia_processing'
  AND ISNULL(EnvioCliente, 'N') = 'N'", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Id", codTicket);
            cmd.Parameters.AddWithValue("@Desde", desde);
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                mapa[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
            return mapa;
        }

        // ───────────────────────────── Push ─────────────────────────────

        public async Task SalvarInscricaoAsync(int codEmp, string usuario, MobilePushInscricaoRequest req)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM MobilePushInscricao WHERE Endpoint = @Endpoint)
    UPDATE MobilePushInscricao
    SET CodEmp = @CodEmp, Usuario = @Usuario, P256dh = @P256dh, Auth = @Auth,
        Dispositivo = @Dispositivo, DataHoraCadastro = GETDATE(), Falhas = 0
    WHERE Endpoint = @Endpoint
ELSE
    INSERT INTO MobilePushInscricao (CodEmp, Usuario, Endpoint, P256dh, Auth, Dispositivo, DataHoraCadastro, Falhas)
    VALUES (@CodEmp, @Usuario, @Endpoint, @P256dh, @Auth, @Dispositivo, GETDATE(), 0)", con);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpperInvariant());
            cmd.Parameters.Add("@Endpoint", SqlDbType.VarChar, 700).Value = req.Endpoint;
            cmd.Parameters.Add("@P256dh", SqlDbType.VarChar, 200).Value = req.P256dh;
            cmd.Parameters.Add("@Auth", SqlDbType.VarChar, 100).Value = req.Auth;
            cmd.Parameters.Add("@Dispositivo", SqlDbType.NVarChar, 200).Value =
                (req.Dispositivo ?? "").Length > 200 ? req.Dispositivo![..200] : (req.Dispositivo ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoverInscricaoAsync(string endpoint)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();
            using var cmd = new SqlCommand("DELETE FROM MobilePushInscricao WHERE Endpoint = @Endpoint", con);
            cmd.Parameters.Add("@Endpoint", SqlDbType.VarChar, 700).Value = endpoint;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}