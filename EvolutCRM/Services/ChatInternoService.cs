using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.CodeDom;
using System.Text.Json;

namespace EvolutCRM.Services
{
    public class ChatInternoService
    {
        private readonly string _conn;
        private readonly IWebHostEnvironment _env;

        public ChatInternoService(IConfiguration config, IWebHostEnvironment env)
        {
            _conn = config.GetConnectionString("Connection")!;
            _env = env;
        }

        // ══════════════════════════════════════════════════════
        // PRESENÇA / USUÁRIOS
        // ══════════════════════════════════════════════════════

        /// <summary>Lista usuários ativos exceto o logado, com status online via ChatUltimaAtividade.</summary>
        public async Task<List<ChatInternoUsuarioModel>> ObterUsuariosAtivosAsync(string usuarioLogado, string CodEmp)
        {
            var lista = new List<ChatInternoUsuarioModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT
    LTRIM(RTRIM(Usuario)) AS Usuario,
    CASE
        WHEN ChatUltimaAtividade IS NOT NULL
         AND DATEDIFF(SECOND, ChatUltimaAtividade, GETDATE()) <= 10
        THEN 'S' ELSE 'N'
    END AS Online,
    ChatUltimaAtividade AS UltimaAtividade
FROM Usuario
WHERE ISNULL(Inativo,'N') = 'N'
  AND ISNULL(Help,'N') = 'S'
  AND UPPER(LTRIM(RTRIM(Usuario))) <> UPPER(LTRIM(RTRIM(@Me)))
and CodEmp = @CodEmp
ORDER BY Usuario";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado.Trim());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                lista.Add(new ChatInternoUsuarioModel
                {
                    Usuario = dr["Usuario"]?.ToString()?.Trim() ?? "",
                    Online = dr["Online"]?.ToString() == "S",
                    UltimaAtividade = dr["UltimaAtividade"] == DBNull.Value
                                        ? null : Convert.ToDateTime(dr["UltimaAtividade"])
                });
            }

            return lista;
        }

        /// <summary>Heartbeat de presença — chamado a cada polling no Razor.</summary>
        /// <summary>Heartbeat de presença — chamado a cada polling no Razor.</summary>
        public async Task AtualizarPresencaAsync(string usuario, string CodEmp)
        {
            // ← CORREÇÃO: era new SqlConnection("Connection") — string literal em vez da connection string
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        UPDATE Usuario
        SET Online = 'S',
            DataHoraUltimoPing = GETDATE(),
            ChatUltimaAtividade = GETDATE()
        WHERE UPPER(LTRIM(RTRIM(Usuario)))
            = UPPER(LTRIM(RTRIM(@Usuario)))
          AND ISNULL(Inativo, 'N') = 'N' and CodEmp = @CodEmp", conn);

            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Marca o usuário como offline — chamado no Dispose e pelo beforeunload.</summary>
        public async Task MarcarOfflineAsync(string usuario, string CodEmp)
        {
            if (string.IsNullOrWhiteSpace(usuario)) return;

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
        UPDATE Usuario
        SET Online = 'N',
            DataHoraUltimoPing = NULL
        WHERE UPPER(LTRIM(RTRIM(Usuario)))
            = UPPER(LTRIM(RTRIM(@Usuario)))
          AND ISNULL(Inativo, 'N') = 'N' and CodEmp = @CodEmp", conn);

            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        // ══════════════════════════════════════════════════════
        // CONVERSAS  (ChatInternoC)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Lista todas as conversas ativas onde o usuário é participante,
        /// com preview da última mensagem e contagem de não lidas.
        /// </summary>
        public async Task<List<ChatInternoConversaModel>> ObterConversasAsync(string usuarioLogado, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();
            var lista = new List<ChatInternoConversaModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT
    C.Codigo,
    C.Tipo,
    ISNULL(C.NomeGrupo,'')     AS NomeGrupo,
    ISNULL(C.Participantes,'') AS Participantes,
    C.Ativa,
    UM.DataHora            AS UltimaMsgDataHora,
    ISNULL(UM.Texto,'')    AS UltimaAnotacao,
    UM.Usuario             AS UltimoUsuario,
    ISNULL(UM.Tipo,'TEXT') AS UltimaMsgTipo,
    NL.NaoLidas
FROM ChatInternoC C
OUTER APPLY (
    SELECT TOP 1 D.DataHora, D.Texto, D.Usuario, D.Tipo
    FROM ChatInternoD D
    WHERE D.CodConversa = C.Codigo AND D.Excluida = 0 AND D.Tipo <> 'DIGITANDO'
    ORDER BY D.DataHora DESC, D.Codigo DESC
) UM
CROSS APPLY (
    SELECT COUNT(*) AS NaoLidas
    FROM ChatInternoD D6
    WHERE D6.CodConversa = C.Codigo
      AND D6.Excluida = 0
      AND D6.Tipo <> 'DIGITANDO'
      AND UPPER(D6.Usuario) <> @Me
      AND ISNULL(D6.LidoPor,'') NOT LIKE '%;' + @Me + ';%'
) NL
WHERE C.Ativa = 1
  AND ISNULL(C.Participantes,'') LIKE '%;' + @Me + ';%' and CodEmp = @CodEmp
ORDER BY ISNULL(UM.DataHora, C.CriadoEm) DESC";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                var tipo = dr["Tipo"]?.ToString() ?? "INDIVIDUAL";
                var nomeGrupo = dr["NomeGrupo"]?.ToString() ?? "";
                var participantes = dr["Participantes"]?.ToString() ?? "";

                // título: para INDIVIDUAL mostra o outro participante; para GRUPO mostra o nome
                var titulo = tipo == "GRUPO"
                    ? nomeGrupo
                    : participantes
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(x => !x.Equals(usuarioLogado, StringComparison.OrdinalIgnoreCase))
                      ?? "Conversa";

                lista.Add(new ChatInternoConversaModel
                {
                    Codigo = Convert.ToInt32(dr["Codigo"]),
                    ChatTipo = tipo,
                    NomeGrupo = nomeGrupo,
                    Participantes = participantes,
                    Titulo = titulo,
                    Ativa = Convert.ToBoolean(dr["Ativa"]),
                    UltimaMensagem = dr["UltimaMsgDataHora"] == DBNull.Value
                                          ? null : Convert.ToDateTime(dr["UltimaMsgDataHora"]),
                    UltimaAnotacao = dr["UltimaAnotacao"]?.ToString() ?? "",
                    UltimoUsuario = dr["UltimoUsuario"]?.ToString() ?? "",
                    UltimaMsgTipo = dr["UltimaMsgTipo"]?.ToString() ?? "TEXT",
                    MensagensNaoLidas = dr["NaoLidas"] == DBNull.Value
                                          ? 0 : Convert.ToInt32(dr["NaoLidas"])
                });
            }

            return lista;
        }

        /// <summary>Cria ou retorna conversa INDIVIDUAL existente entre dois usuários.</summary>
        public async Task<int> CriarConversaIndividualAsync(string usuarioLogado, string usuarioDestino, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();
            usuarioDestino = usuarioDestino.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(usuarioLogado))
                throw new Exception("Usuário logado não identificado.");
            if (string.IsNullOrWhiteSpace(usuarioDestino))
                throw new Exception("Usuário destino não informado.");
            if (usuarioLogado == usuarioDestino)
                throw new Exception("Não é possível iniciar conversa consigo mesmo.");

            // Participantes sempre em ordem alfabética para garantir unicidade
            var partes = new[] { usuarioLogado, usuarioDestino }.OrderBy(x => x).ToArray();
            var participantesStr = $";{partes[0]};{partes[1]};";

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // Verifica se já existe conversa individual ativa entre exatamente esses dois
            const string sqlExiste = @"
SELECT TOP 1 Codigo
FROM ChatInternoC
WHERE Tipo  = 'INDIVIDUAL'
  AND Ativa = 1
  AND Participantes = @Partic
and CodEmp = @CodEmp
ORDER BY Codigo DESC";

            using (var cmdEx = new SqlCommand(sqlExiste, con))
            {
                cmdEx.Parameters.AddWithValue("@Partic", participantesStr);
                cmdEx.Parameters.AddWithValue("@CodEmp", CodEmp);
                var existente = await cmdEx.ExecuteScalarAsync();
                if (existente != null && existente != DBNull.Value)
                    return Convert.ToInt32(existente);
            }

            // Cria nova conversa
            const string sqlInsert = @"
INSERT INTO ChatInternoC (Tipo, Participantes, CriadoPor, CodEmp)
OUTPUT INSERTED.Codigo
VALUES ('INDIVIDUAL', @Partic, @CriadoPor, @CodEmp)";

            using var cmdIns = new SqlCommand(sqlInsert, con);
            cmdIns.Parameters.AddWithValue("@Partic", participantesStr);
            cmdIns.Parameters.AddWithValue("@CriadoPor", usuarioLogado);
            cmdIns.Parameters.AddWithValue("@CodEmp", CodEmp);
            return Convert.ToInt32(await cmdIns.ExecuteScalarAsync());
        }

        /// <summary>Cria um grupo novo.</summary>
        public async Task<int> CriarGrupoAsync(string usuarioLogado, string nomeGrupo, List<string> outrosUsuarios, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();

            var membros = outrosUsuarios
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();

            if (!membros.Contains(usuarioLogado))
                membros.Add(usuarioLogado);

            // "JOAO;MARIA;PEDRO;"
            var participantesStr = ";" + string.Join(";", membros) + ";";

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
INSERT INTO ChatInternoC (Tipo, NomeGrupo, Participantes, CriadoPor, CodEmp)
OUTPUT INSERTED.Codigo
VALUES ('GRUPO', @Nome, @Partic, @CriadoPor, @CodEmp)";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Nome", nomeGrupo.Trim());
            cmd.Parameters.AddWithValue("@Partic", participantesStr);
            cmd.Parameters.AddWithValue("@CriadoPor", usuarioLogado);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        /// <summary>Adiciona um usuário à lista de participantes de um grupo.</summary>
        public async Task AdicionarParticipanteAsync(int codConversa, string usuario, string CodEmp)
        {
            usuario = usuario.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
UPDATE ChatInternoC
SET Participantes = Participantes + @Usuario + ';'
WHERE Codigo = @Cod
  AND Tipo   = 'GRUPO'
  AND Ativa  = 1
and CodEmp = @CodEmp
  AND ISNULL(Participantes,'') NOT LIKE '%;' + @Usuario + ';%'";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Finaliza (arquiva) uma conversa.</summary>
        public async Task FinalizarConversaAsync(int codConversa, string usuarioLogado, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
UPDATE ChatInternoC
SET Ativa = 0, FinalizadaPor = @Me, FinalizadaEm = GETDATE()
WHERE Codigo = @Cod
  AND Ativa  = 1
  AND ISNULL(Participantes,'') LIKE '%;' + @Me + ';%' and CodEmp = @CodEmp";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        // ══════════════════════════════════════════════════════
        // MENSAGENS  (ChatInternoD)
        // ══════════════════════════════════════════════════════

        /// <summary>Retorna todas as mensagens não excluídas de uma conversa.</summary>
        public async Task<List<ChatInternoMensagemModel>> ObterMensagensAsync(
            int codConversa, string usuarioLogado, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();
            var lista = new List<ChatInternoMensagemModel>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT
    Codigo,
    CodConversa,
    Usuario,
    ISNULL(Tipo,'TEXT')   AS Tipo,
    ISNULL(Texto,'')      AS Texto,
    DataHora,
    Excluida,
    ISNULL(LidoPor,'')    AS LidoPor,
    ArquivoNome,
    ArquivoUrl,
    ArquivoMime,
    ArquivoTamanho,
    RespostaCodigo,
    RespostaUsuario,
    RespostaTexto,
    Reacoes
FROM ChatInternoD
WHERE CodConversa = @Cod
  AND Excluida    = 0
  AND Tipo        <> 'DIGITANDO'
and CodEmp = @CodEmp
ORDER BY DataHora ASC, Codigo ASC";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                var usuario = dr["Usuario"]?.ToString()?.Trim() ?? "";
                var lidoPor = dr["LidoPor"]?.ToString() ?? "";

                lista.Add(new ChatInternoMensagemModel
                {
                    Codigo = Convert.ToInt32(dr["Codigo"]),
                    CodConversa = Convert.ToInt32(dr["CodConversa"]),
                    Usuario = usuario,
                    Tipo = dr["Tipo"]?.ToString() ?? "TEXT",
                    Texto = dr["Texto"]?.ToString() ?? "",
                    DataHora = Convert.ToDateTime(dr["DataHora"]),
                    Excluida = Convert.ToBoolean(dr["Excluida"]),
                    LidoPor = lidoPor,
                    ArquivoNome = dr["ArquivoNome"] == DBNull.Value ? null : dr["ArquivoNome"]?.ToString(),
                    ArquivoUrl = dr["ArquivoUrl"] == DBNull.Value ? null : dr["ArquivoUrl"]?.ToString(),
                    ArquivoMime = dr["ArquivoMime"] == DBNull.Value ? null : dr["ArquivoMime"]?.ToString(),
                    ArquivoTamanho = dr["ArquivoTamanho"] == DBNull.Value ? null : Convert.ToInt64(dr["ArquivoTamanho"]),
                    RespostaCodigo = dr["RespostaCodigo"] == DBNull.Value ? null : Convert.ToInt32(dr["RespostaCodigo"]),
                    RespostaUsuario = dr["RespostaUsuario"] == DBNull.Value ? null : dr["RespostaUsuario"]?.ToString(),
                    RespostaTexto = dr["RespostaTexto"] == DBNull.Value ? null : dr["RespostaTexto"]?.ToString(),
                    Reacoes = dr["Reacoes"] == DBNull.Value ? null : dr["Reacoes"]?.ToString(),
                    MinhaMensagem = usuario.Equals(usuarioLogado, StringComparison.OrdinalIgnoreCase),
                    // "Lida" para MINHA mensagem = alguém além de mim já leu
                    Lida = usuario.Equals(usuarioLogado, StringComparison.OrdinalIgnoreCase)
    ? lidoPor.Split(';', StringSplitOptions.RemoveEmptyEntries)
             .Any(x => !x.Equals(usuarioLogado, StringComparison.OrdinalIgnoreCase))
    : lidoPor.Contains($";{usuarioLogado};", StringComparison.OrdinalIgnoreCase)
                });
            }

            return lista;
        }

        /// <summary>Envia mensagem de texto, imagem ou arquivo, com suporte a quote.</summary>
        public async Task<int> EnviarMensagemAsync(
            int codConversa,
            string usuario,
            string texto,
            string CodEmp,
            string tipo = "TEXT",
            string? arquivoNome = null,
            string? arquivoUrl = null,
            string? arquivoMime = null,
            long? arquivoTamanho = null,
            int? respostaCodigo = null,
            string? respostaUsuario = null,
            string? respostaTexto = null
            )
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // Só insere se a conversa estiver ativa e o usuário for participante
            const string sql = @"
INSERT INTO ChatInternoD
(CodConversa, Usuario, Tipo, Texto, DataHora, LidoPor,
 ArquivoNome, ArquivoUrl, ArquivoMime, ArquivoTamanho,
 RespostaCodigo, RespostaUsuario, RespostaTexto, CodEmp)
OUTPUT INSERTED.Codigo
SELECT
    @Cod, @Usuario, @Tipo, @Texto, GETDATE(), ';' + @Usuario + ';',
    @AqNome, @AqUrl, @AqMime, @AqTam,
    @RespCod, @RespUsu, @RespTxt, @CodEmp
WHERE EXISTS (
    SELECT 1 FROM ChatInternoC
    WHERE Codigo = @Cod
      AND Ativa  = 1
and CodEmp = @CodEmp
      AND ISNULL(Participantes,'') LIKE '%;' + @Usuario + ';%'
)";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpper());
            cmd.Parameters.AddWithValue("@Tipo", tipo);
            cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AqNome", (object?)arquivoNome ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AqUrl", (object?)arquivoUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AqMime", (object?)arquivoMime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AqTam", (object?)arquivoTamanho ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RespCod", (object?)respostaCodigo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RespUsu", (object?)respostaUsuario ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RespTxt", (object?)respostaTexto ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        /// <summary>Soft-delete — apenas o próprio autor pode excluir.</summary>
        public async Task ExcluirMensagemAsync(int codigoMensagem, string usuario, string CodEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
UPDATE ChatInternoD SET Excluida = 1
WHERE Codigo = @Codigo and CodEmp = @CodEmp
  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Codigo", codigoMensagem);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Toggle de reação em emoji (adiciona ou remove o usuário da lista).</summary>
        public async Task ReagirMensagemAsync(int codigoMensagem, string usuario, string emoji, string CodEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sqlSel = "SELECT ISNULL(Reacoes,'{}') FROM ChatInternoD WHERE Codigo = @Codigo and CodEmp = @CodEmp";
            using var cmdSel = new SqlCommand(sqlSel, con);
            cmdSel.Parameters.AddWithValue("@Codigo", codigoMensagem);
            cmdSel.Parameters.AddWithValue("@CodEmp", CodEmp);
            var raw = (await cmdSel.ExecuteScalarAsync())?.ToString() ?? "{}";

            Dictionary<string, List<string>> reacoes;
            try { reacoes = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(raw) ?? new(); }
            catch { reacoes = new(); }

            if (!reacoes.ContainsKey(emoji)) reacoes[emoji] = new List<string>();

            if (reacoes[emoji].Contains(usuario, StringComparer.OrdinalIgnoreCase))
                reacoes[emoji].RemoveAll(x => x.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            else
                reacoes[emoji].Add(usuario);

            if (!reacoes[emoji].Any()) reacoes.Remove(emoji);

            const string sqlUpd = "UPDATE ChatInternoD SET Reacoes = @Json WHERE Codigo = @Codigo and CodEmp = @CodEmp";
            using var cmdUpd = new SqlCommand(sqlUpd, con);
            cmdUpd.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(reacoes));
            cmdUpd.Parameters.AddWithValue("@Codigo", codigoMensagem);
            cmdUpd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmdUpd.ExecuteNonQueryAsync();
        }

        /// <summary>Busca de texto dentro de uma conversa.</summary>
        public async Task<List<ChatInternoMensagemModel>> BuscarMensagensAsync(
            int codConversa, string usuarioLogado, string termo, string CodEmp)
        {
            var lista = new List<ChatInternoMensagemModel>();
            if (string.IsNullOrWhiteSpace(termo)) return lista;

            usuarioLogado = usuarioLogado.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT TOP 60
    Codigo, CodConversa, Usuario,
    ISNULL(Tipo,'TEXT') AS Tipo,
    ISNULL(Texto,'')    AS Texto,
    DataHora,
    ISNULL(LidoPor,'') AS LidoPor,
    ArquivoNome, ArquivoUrl, ArquivoMime, ArquivoTamanho,
    RespostaCodigo, RespostaUsuario, RespostaTexto, Reacoes
FROM ChatInternoD
WHERE CodConversa = @Cod
  AND Excluida    = 0
  AND Tipo        = 'TEXT'
  AND Texto LIKE '%' + @Termo + '%'
and CodEmp = @CodEmp
ORDER BY DataHora DESC, Codigo DESC";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@Termo", termo.Trim());
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                var usuario = dr["Usuario"]?.ToString()?.Trim() ?? "";
                var lidoPor = dr["LidoPor"]?.ToString() ?? "";
                lista.Add(new ChatInternoMensagemModel
                {
                    Codigo = Convert.ToInt32(dr["Codigo"]),
                    CodConversa = Convert.ToInt32(dr["CodConversa"]),
                    Usuario = usuario,
                    Tipo = dr["Tipo"]?.ToString() ?? "TEXT",
                    Texto = dr["Texto"]?.ToString() ?? "",
                    DataHora = Convert.ToDateTime(dr["DataHora"]),
                    LidoPor = lidoPor,
                    ArquivoNome = dr["ArquivoNome"] == DBNull.Value ? null : dr["ArquivoNome"]?.ToString(),
                    ArquivoUrl = dr["ArquivoUrl"] == DBNull.Value ? null : dr["ArquivoUrl"]?.ToString(),
                    ArquivoMime = dr["ArquivoMime"] == DBNull.Value ? null : dr["ArquivoMime"]?.ToString(),
                    ArquivoTamanho = dr["ArquivoTamanho"] == DBNull.Value ? null : Convert.ToInt64(dr["ArquivoTamanho"]),
                    RespostaCodigo = dr["RespostaCodigo"] == DBNull.Value ? null : Convert.ToInt32(dr["RespostaCodigo"]),
                    RespostaUsuario = dr["RespostaUsuario"] == DBNull.Value ? null : dr["RespostaUsuario"]?.ToString(),
                    RespostaTexto = dr["RespostaTexto"] == DBNull.Value ? null : dr["RespostaTexto"]?.ToString(),
                    Reacoes = dr["Reacoes"] == DBNull.Value ? null : dr["Reacoes"]?.ToString(),
                    MinhaMensagem = usuario.Equals(usuarioLogado, StringComparison.OrdinalIgnoreCase),
                    Lida = lidoPor.Contains($";{usuarioLogado};", StringComparison.OrdinalIgnoreCase)
                });
            }

            return lista;
        }

        // ══════════════════════════════════════════════════════
        // LEITURA
        // ══════════════════════════════════════════════════════

        /// <summary>Marca todas as mensagens não lidas de uma conversa como lidas pelo usuário.</summary>
        public async Task MarcarConversaComoLidaAsync(int codConversa, string usuario, string CodEmp)
        {
            usuario = usuario.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            // Acrescenta o usuário no LidoPor de cada mensagem que ainda não o tem
            const string sql = @"
UPDATE ChatInternoD
SET LidoPor = ISNULL(LidoPor,'') + @Usuario + ';'
WHERE CodConversa = @Cod
  AND Excluida    = 0
  AND Tipo        <> 'DIGITANDO'
  AND UPPER(Usuario) <> @Usuario
  AND ISNULL(LidoPor,'') NOT LIKE '%;' + @Usuario + ';%' and CodEmp = @CodEmp";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Remove a leitura da última mensagem recebida (marcar como não lida).</summary>
        public async Task MarcarComoNaoLidaAsync(int codConversa, string usuario, string CodEmp)
        {
            usuario = usuario.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
UPDATE ChatInternoD
SET LidoPor = REPLACE(ISNULL(LidoPor,''), ';' + @Usuario + ';', ';')
WHERE Codigo = (
    SELECT TOP 1 Codigo FROM ChatInternoD
    WHERE CodConversa = @Cod
      AND Excluida    = 0
      AND Tipo        <> 'DIGITANDO'
      AND UPPER(Usuario) <> @Usuario
and CodEmp = @CodEmp
    ORDER BY DataHora DESC, Codigo DESC
)";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            await cmd.ExecuteNonQueryAsync();
        }

        // ══════════════════════════════════════════════════════
        // DIGITANDO  (linha com Tipo = 'DIGITANDO' em ChatInternoD)
        // A linha é upsert/delete; TTL de 5 s garante limpeza automática.
        // ══════════════════════════════════════════════════════

        /// <summary>Grava ou remove o indicador "digitando" do usuário na conversa.</summary>
        public async Task SalvarDigitandoAsync(string usuario, int codConversa, bool digitando, string CodEmp)
        {
            usuario = usuario.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            if (digitando)
            {
                // Upsert: atualiza DataHora se já existe, insere se não existe
                const string sql = @"
IF EXISTS (
    SELECT 1 FROM ChatInternoD
    WHERE CodConversa = @Cod AND UPPER(Usuario) = @Usuario AND Tipo = 'DIGITANDO' and CodEmp = @CodEmp
)
    UPDATE ChatInternoD
    SET DataHora = GETDATE()
    WHERE CodConversa = @Cod AND UPPER(Usuario) = @Usuario AND Tipo = 'DIGITANDO' and CodEmp = @CodEmp
ELSE
    INSERT INTO ChatInternoD (CodConversa, Usuario, Tipo, Texto, DataHora, Excluida, LidoPor, CodEmp)
    VALUES (@Cod, @Usuario, 'DIGITANDO', '', GETDATE(), 0, '', @CodEmp)";

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Cod", codConversa);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string sql = @"
DELETE FROM ChatInternoD
WHERE CodConversa = @Cod AND UPPER(Usuario) = @Usuario AND Tipo = 'DIGITANDO' and CodEmp = @CodEmp";

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Cod", codConversa);
                cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>Retorna lista de usuários que estão digitando na conversa (TTL 5 s).</summary>
        public async Task<List<string>> ObterQuemEstaDigitandoAsync(int codConversa, string usuarioLogado, string CodEmp)
        {
            var lista = new List<string>();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT Usuario
FROM ChatInternoD
WHERE CodConversa = @Cod
  AND CodEmp      = @CodEmp
  AND Tipo        = 'DIGITANDO'
  AND UPPER(Usuario) <> UPPER(@Me)
  AND DATEDIFF(SECOND, DataHora, GETDATE()) <= 5";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado.Trim());

            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
                lista.Add(dr["Usuario"]?.ToString() ?? "");

            return lista;
        }

        // ══════════════════════════════════════════════════════
        // NOTIFICAÇÕES GLOBAIS
        // ══════════════════════════════════════════════════════

        /// <summary>Total de mensagens não lidas em todas as conversas do usuário.</summary>
        public async Task<int> ObterQuantidadeMensagensNaoLidasAsync(string usuarioLogado, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT COUNT(*)
FROM ChatInternoD D
INNER JOIN ChatInternoC C
        ON C.Codigo = D.CodConversa
       AND C.CodEmp = D.CodEmp
       AND C.Ativa = 1
WHERE D.Excluida = 0
  AND D.CodEmp   = @CodEmp
  AND C.CodEmp   = @CodEmp
  AND D.Tipo     <> 'DIGITANDO'
  AND UPPER(D.Usuario) <> @Me
  AND C.Participantes LIKE '%;' + @Me + ';%'
  AND ISNULL(D.LidoPor,'') NOT LIKE '%;' + @Me + ';%'";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        /// <summary>Última mensagem não lida recebida (para notificação global na navbar).</summary>
        public async Task<ChatInternoMensagemModel?> ObterUltimaMensagemNaoLidaAsync(string usuarioLogado, string CodEmp)
        {
            usuarioLogado = usuarioLogado.Trim().ToUpper();

            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            const string sql = @"
SELECT TOP 1
    D.Codigo, D.CodConversa, D.Usuario,
    ISNULL(D.Tipo,'TEXT') AS Tipo,
    ISNULL(D.Texto,'')    AS Texto,
    D.DataHora
FROM ChatInternoD D
INNER JOIN ChatInternoC C
        ON C.Codigo = D.CodConversa
       AND C.CodEmp = D.CodEmp
       AND C.Ativa = 1
WHERE D.Excluida = 0
  AND D.CodEmp   = @CodEmp
  AND C.CodEmp   = @CodEmp
  AND D.Tipo     <> 'DIGITANDO'
  AND UPPER(D.Usuario) <> @Me
  AND C.Participantes LIKE '%;' + @Me + ';%'
  AND ISNULL(D.LidoPor,'') NOT LIKE '%;' + @Me + ';%'
ORDER BY D.DataHora DESC, D.Codigo DESC";

            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Me", usuarioLogado);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var dr = await cmd.ExecuteReaderAsync();
            if (!await dr.ReadAsync()) return null;

            return new ChatInternoMensagemModel
            {
                Codigo = Convert.ToInt32(dr["Codigo"]),
                CodConversa = Convert.ToInt32(dr["CodConversa"]),
                Usuario = dr["Usuario"]?.ToString() ?? "",
                Tipo = dr["Tipo"]?.ToString() ?? "TEXT",
                Texto = dr["Texto"]?.ToString() ?? "",
                DataHora = Convert.ToDateTime(dr["DataHora"]),
                MinhaMensagem = false
            };
        }

        // ══════════════════════════════════════════════════════
        // UPLOAD DE ARQUIVO
        // ══════════════════════════════════════════════════════

        /// <summary>Salva arquivo em wwwroot/chat-uploads/ e retorna os metadados.</summary>
        public async Task<(string url, string nome, string mime, long tamanho)> SalvarArquivoAsync(
            Stream stream, string nomeOriginal, string contentType)
        {
            var pasta = Path.Combine(_env.WebRootPath, "chat-uploads");
            Directory.CreateDirectory(pasta);

            var ext = Path.GetExtension(nomeOriginal);
            var nomeArquivo = $"{Guid.NewGuid()}{ext}";
            var caminho = Path.Combine(pasta, nomeArquivo);

            await using var fs = new FileStream(caminho, FileMode.Create);
            await stream.CopyToAsync(fs);

            return ($"/chat-uploads/{nomeArquivo}", nomeOriginal, contentType, fs.Length);
        }

        // ADICIONAR em ChatInternoService.cs
        public async Task<(int Total, int UltimoCodigo)> ResumoMensagensAsync(int codConversa, string CodEmp)
        {
            using var con = new SqlConnection(_conn);
            await con.OpenAsync();

            using var cmd = new SqlCommand(@"
SELECT COUNT(1), ISNULL(MAX(Codigo), 0)
FROM ChatInternoD
WHERE CodConversa = @Cod
  AND CodEmp      = @CodEmp
  AND Excluida    = 0
  AND Tipo        <> 'DIGITANDO'", con);

            cmd.Parameters.AddWithValue("@Cod", codConversa);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmp);

            using var rd = await cmd.ExecuteReaderAsync();
            await rd.ReadAsync();
            return (rd.GetInt32(0), rd.GetInt32(1));
        }

    }
}