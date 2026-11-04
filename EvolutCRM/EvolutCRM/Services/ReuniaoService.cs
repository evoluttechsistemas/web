using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EvolutCRM.Services;

public sealed class ReuniaoService
{
    public const int LimitePadrao = 6;
    private const string Permissao = "REUNIOES_ACESSAR";

    private readonly string _connectionString;
    private readonly UserState _userState;
    private readonly ReuniaoConviteNotifier _notifier;
    private readonly ReuniaoPresencaStore _presenca;

    public ReuniaoService(IConfiguration configuration, UserState userState,
        ReuniaoConviteNotifier notifier, ReuniaoPresencaStore presenca)
    {
        _connectionString = configuration.GetConnectionString("Connection")!;
        _userState = userState;
        _notifier = notifier;
        _presenca = presenca;
    }

    private int CodEmpAtual => _userState.CurrentCompanyId;
    private string UsuarioAtual => (_userState.CurrentUser ?? "").Trim().ToUpperInvariant();

    // ============================================================
    // USUÁRIOS PARA CONVIDAR
    // ============================================================

    /// <summary>Usuários do HELP com permissão de reuniões (exceto eu e quem já está na reunião).</summary>
    public async Task<List<ReuniaoParticipanteModel>> ListarUsuariosParaConvidarAsync(int? codReuniao = null)
    {
        if (CodEmpAtual <= 0) return [];

        const string sql = @"
            SELECT LTRIM(RTRIM(u.Usuario)) AS Usuario,
                   CASE WHEN ISNULL(u.Online, 'N') = 'S'
                             AND u.UltimaAtividadeOnline IS NOT NULL
                             AND DATEDIFF(SECOND, u.UltimaAtividadeOnline, GETDATE()) <= 90
                        THEN 1 ELSE 0 END AS Online
            FROM Usuario u
            WHERE u.CodEmp = @CodEmp
              AND ISNULL(u.Inativo, 'N') <> 'S'
              AND ISNULL(u.Help, 'N') = 'S'
              AND UPPER(LTRIM(RTRIM(u.Usuario))) <> @Usuario
              AND EXISTS (SELECT 1 FROM UsuarioPermissaoCRM p
                          WHERE p.CodUsuario = u.Codigo AND p.CodEmp = u.CodEmp
                            AND p.Permissao = @Permissao AND ISNULL(p.Ativo, 'N') = 'S')
              AND (@CodReuniao = 0 OR NOT EXISTS (
                          SELECT 1 FROM ReuniaoHelpParticipante rp
                          WHERE rp.CodReuniao = @CodReuniao
                            AND UPPER(LTRIM(RTRIM(rp.Usuario))) = UPPER(LTRIM(RTRIM(u.Usuario)))))
            ORDER BY Online DESC, u.Usuario";

        var usuarios = new List<ReuniaoParticipanteModel>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        cmd.Parameters.AddWithValue("@Permissao", Permissao);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao ?? 0);
        await conn.OpenAsync();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var usuario = reader["Usuario"]?.ToString()?.Trim() ?? "";
            usuarios.Add(new ReuniaoParticipanteModel
            {
                Usuario = usuario,
                NomeExibicao = ReuniaoVisual.Nome(usuario),
                Online = Convert.ToInt32(reader["Online"]) == 1
            });
        }

        return usuarios;
    }

    // ============================================================
    // LISTAS (LOBBY)
    // ============================================================

    /// <summary>Reuniões abertas das quais participo (como organizador ou convidado).</summary>
    public async Task<List<ReuniaoModel>> ListarAbertasAsync()
    {
        if (CodEmpAtual <= 0) return [];
        await EncerrarAbandonadasAsync();

        const string sql = @"
            SELECT r.Codigo, r.CodEmp, r.Titulo, r.CriadoPor, r.DataCriacao, r.DataEncerramento, r.Status, r.LimiteParticipantes,
                   (SELECT COUNT(1) FROM ReuniaoHelpParticipante x WHERE x.CodReuniao = r.Codigo) AS QtdParticipantes,
                   0 AS QtdMensagens,
                   STUFF((SELECT ',' + LTRIM(RTRIM(x.Usuario))
                          FROM ReuniaoHelpParticipante x
                          WHERE x.CodReuniao = r.Codigo
                          ORDER BY CASE WHEN x.Situacao = 'Organizador' THEN 0 ELSE 1 END, x.Usuario
                          FOR XML PATH('')), 1, 1, '') AS Participantes,
                   p.Situacao AS MeuPapel, p.Resposta AS MinhaResposta,
                   CASE WHEN p.DataEntrada IS NULL THEN 0 ELSE 1 END AS JaEntrei,
                   ISNULL(p.ConvidadoPor, r.CriadoPor) AS ConvidadoPor,
                   ISNULL(p.DataConvite, r.DataCriacao) AS DataConvite
            FROM ReuniaoHelp r
            INNER JOIN ReuniaoHelpParticipante p ON p.CodReuniao = r.Codigo
            WHERE r.CodEmp = @CodEmp
              AND r.Status = 'Aberta'
              AND UPPER(LTRIM(RTRIM(p.Usuario))) = @Usuario
            ORDER BY ISNULL(p.DataConvite, r.DataCriacao) DESC";

        var lista = await ListarReunioesAsync(sql);
        foreach (var r in lista)
        {
            r.UsuariosNaSala = _presenca.UsuariosNaSala(r.Codigo);
            r.QtdNaSala = r.UsuariosNaSala.Count;
        }
        return lista;
    }

    /// <summary>Últimas reuniões encerradas das quais participei.</summary>
    public async Task<List<ReuniaoModel>> ListarHistoricoAsync(int quantidade = 15)
    {
        if (CodEmpAtual <= 0) return [];

        var sql = $@"
            SELECT TOP {Math.Clamp(quantidade, 1, 50)}
                   r.Codigo, r.CodEmp, r.Titulo, r.CriadoPor, r.DataCriacao, r.DataEncerramento, r.Status, r.LimiteParticipantes,
                   (SELECT COUNT(1) FROM ReuniaoHelpParticipante x WHERE x.CodReuniao = r.Codigo AND x.DataEntrada IS NOT NULL) AS QtdParticipantes,
                   (SELECT COUNT(1) FROM ReuniaoHelpMensagem m WHERE m.CodReuniao = r.Codigo) AS QtdMensagens,
                   STUFF((SELECT ',' + LTRIM(RTRIM(x.Usuario))
                          FROM ReuniaoHelpParticipante x
                          WHERE x.CodReuniao = r.Codigo AND x.DataEntrada IS NOT NULL
                          ORDER BY CASE WHEN x.Situacao = 'Organizador' THEN 0 ELSE 1 END, x.Usuario
                          FOR XML PATH('')), 1, 1, '') AS Participantes,
                   p.Situacao AS MeuPapel, p.Resposta AS MinhaResposta,
                   CASE WHEN p.DataEntrada IS NULL THEN 0 ELSE 1 END AS JaEntrei,
                   ISNULL(p.ConvidadoPor, r.CriadoPor) AS ConvidadoPor,
                   ISNULL(p.DataConvite, r.DataCriacao) AS DataConvite
            FROM ReuniaoHelp r
            INNER JOIN ReuniaoHelpParticipante p ON p.CodReuniao = r.Codigo
            WHERE r.CodEmp = @CodEmp
              AND r.Status = 'Encerrada'
              AND UPPER(LTRIM(RTRIM(p.Usuario))) = @Usuario
              AND r.DataCriacao >= DATEADD(DAY, -30, GETDATE())
            ORDER BY r.DataCriacao DESC";

        return await ListarReunioesAsync(sql);
    }

    /// <summary>Convites que ainda não respondi e chegaram há pouco (usado pelo alerta de chamada).</summary>
    public async Task<List<ReuniaoConviteModel>> ListarConvitesRecentesAsync(int minutos = 3)
    {
        if (CodEmpAtual <= 0 || string.IsNullOrWhiteSpace(UsuarioAtual)) return [];

        const string sql = @"
            SELECT r.Codigo, r.Titulo, ISNULL(p.ConvidadoPor, r.CriadoPor) AS ConvidadoPor,
                   ISNULL(p.DataConvite, r.DataCriacao) AS DataConvite
            FROM ReuniaoHelp r
            INNER JOIN ReuniaoHelpParticipante p ON p.CodReuniao = r.Codigo
            WHERE r.CodEmp = @CodEmp
              AND r.Status = 'Aberta'
              AND UPPER(LTRIM(RTRIM(p.Usuario))) = @Usuario
              AND p.Situacao <> 'Organizador'
              AND p.Resposta IS NULL
              AND p.DataEntrada IS NULL
              AND ISNULL(p.DataConvite, r.DataCriacao) >= DATEADD(MINUTE, -@Minutos, GETDATE())
            ORDER BY ISNULL(p.DataConvite, r.DataCriacao) DESC";

        var convites = new List<ReuniaoConviteModel>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        cmd.Parameters.AddWithValue("@Minutos", minutos);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var codigo = Convert.ToInt32(reader["Codigo"]);
            if (_presenca.UsuarioNaSala(codigo, UsuarioAtual)) continue;
            convites.Add(new ReuniaoConviteModel
            {
                CodReuniao = codigo,
                Titulo = reader["Titulo"]?.ToString() ?? "",
                ConvidadoPor = reader["ConvidadoPor"]?.ToString()?.Trim() ?? "",
                DataConvite = Convert.ToDateTime(reader["DataConvite"])
            });
        }
        return convites;
    }

    // ============================================================
    // CRIAR / CONVIDAR / RECUSAR / ENCERRAR
    // ============================================================

    public async Task<int> CriarAsync(ReuniaoCriarRequest request)
    {
        if (CodEmpAtual <= 0 || string.IsNullOrWhiteSpace(UsuarioAtual))
            throw new InvalidOperationException("Usuário ou empresa não identificados. Entre no HELP novamente.");

        var titulo = (request.Titulo ?? "").Trim();
        if (string.IsNullOrWhiteSpace(titulo))
            titulo = $"Reunião de {ReuniaoVisual.Nome(UsuarioAtual)}";
        if (titulo.Length > 150) titulo = titulo[..150];

        var convidados = NormalizarUsuarios(request.UsuariosConvidados)
            .Take(LimitePadrao - 1)
            .ToList();

        int codReuniao;
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                const string sqlReuniao = @"
                    INSERT INTO ReuniaoHelp (CodEmp, Titulo, CriadoPor, LimiteParticipantes)
                    OUTPUT INSERTED.Codigo
                    VALUES (@CodEmp, @Titulo, @CriadoPor, @Limite);";

                using var cmdReuniao = new SqlCommand(sqlReuniao, conn, transaction);
                cmdReuniao.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
                cmdReuniao.Parameters.AddWithValue("@Titulo", titulo);
                cmdReuniao.Parameters.AddWithValue("@CriadoPor", UsuarioAtual);
                cmdReuniao.Parameters.AddWithValue("@Limite", LimitePadrao);
                codReuniao = Convert.ToInt32(await cmdReuniao.ExecuteScalarAsync());

                await InserirParticipanteAsync(conn, transaction, codReuniao, UsuarioAtual, "Organizador");
                foreach (var convidado in convidados)
                    await InserirParticipanteAsync(conn, transaction, codReuniao, convidado, "Convidado");

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        if (convidados.Count > 0)
            NotificarConvite(codReuniao, titulo, convidados);

        return codReuniao;
    }

    /// <summary>Convida mais pessoas (ou toca de novo para quem ainda não entrou).</summary>
    public async Task<int> ConvidarAsync(int codReuniao, IEnumerable<string> usuarios)
    {
        var reuniao = await ObterAsync(codReuniao)
            ?? throw new InvalidOperationException("Esta reunião foi encerrada.");

        var lista = NormalizarUsuarios(usuarios).ToList();
        if (lista.Count == 0) return 0;

        const string sql = @"
            IF EXISTS (SELECT 1 FROM ReuniaoHelpParticipante
                       WHERE CodReuniao = @CodReuniao AND UPPER(LTRIM(RTRIM(Usuario))) = @Usuario)
                UPDATE ReuniaoHelpParticipante
                SET Resposta = NULL, DataConvite = GETDATE(), ConvidadoPor = @ConvidadoPor
                WHERE CodReuniao = @CodReuniao AND UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                  AND Situacao <> 'Organizador';
            ELSE IF (SELECT COUNT(1) FROM ReuniaoHelpParticipante WHERE CodReuniao = @CodReuniao) < @Limite
                INSERT INTO ReuniaoHelpParticipante (CodReuniao, Usuario, Situacao, ConvidadoPor, DataConvite)
                VALUES (@CodReuniao, @Usuario, 'Convidado', @ConvidadoPor, GETDATE());
            ELSE
                SELECT -1;";

        var convidados = new List<string>();
        var semVaga = false;
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        foreach (var usuario in lista)
        {
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@ConvidadoPor", UsuarioAtual);
            cmd.Parameters.AddWithValue("@Limite", reuniao.LimiteParticipantes);
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado is int r && r == -1) { semVaga = true; continue; }
            convidados.Add(usuario);
        }

        if (convidados.Count > 0)
            NotificarConvite(codReuniao, reuniao.Titulo, convidados);

        if (semVaga && convidados.Count == 0)
            throw new InvalidOperationException($"A sala já tem o limite de {reuniao.LimiteParticipantes} pessoas.");

        return convidados.Count;
    }

    public async Task RecusarConviteAsync(int codReuniao)
    {
        var reuniao = await ObterAsync(codReuniao);
        if (reuniao is null) return;

        const string sql = @"
            UPDATE ReuniaoHelpParticipante
            SET Resposta = 'Recusado'
            WHERE CodReuniao = @CodReuniao AND UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
              AND Situacao <> 'Organizador';";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();

        _notifier.Notificar(new ReuniaoEvento
        {
            Tipo = "Recusado",
            CodEmp = CodEmpAtual,
            CodReuniao = codReuniao,
            Titulo = reuniao.Titulo,
            Remetente = UsuarioAtual,
            Destinatarios = [reuniao.CriadoPor.Trim().ToUpperInvariant()]
        });
    }

    public async Task EncerrarAsync(int codReuniao)
    {
        var reuniao = await ObterAsync(codReuniao);
        if (!EhOrganizador(reuniao))
            throw new InvalidOperationException("Só quem criou a reunião pode encerrar para todos.");

        const string sql = @"
            UPDATE ReuniaoHelp
            SET Status = 'Encerrada', DataEncerramento = GETDATE()
            WHERE Codigo = @CodReuniao AND CodEmp = @CodEmp AND Status = 'Aberta';

            UPDATE ReuniaoHelpParticipante
            SET DataSaida = GETDATE()
            WHERE CodReuniao = @CodReuniao AND DataEntrada IS NOT NULL AND DataSaida IS NULL;";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();

        var participantes = await ListarUsuariosDaReuniaoAsync(codReuniao);
        _notifier.Notificar(new ReuniaoEvento
        {
            Tipo = "Encerrada",
            CodEmp = CodEmpAtual,
            CodReuniao = codReuniao,
            Titulo = reuniao!.Titulo,
            Remetente = UsuarioAtual,
            Destinatarios = participantes
        });
    }

    // ============================================================
    // DENTRO DA SALA
    // ============================================================

    /// <summary>Reunião aberta da qual participo (null se encerrada ou sem acesso).</summary>
    public async Task<ReuniaoModel?> ObterAsync(int codReuniao)
    {
        if (!await PodeEntrarAsync(codReuniao, CodEmpAtual, UsuarioAtual))
            return null;

        const string sql = @"
            SELECT Codigo, CodEmp, Titulo, CriadoPor, DataCriacao, DataEncerramento, Status, LimiteParticipantes
            FROM ReuniaoHelp
            WHERE Codigo = @CodReuniao AND CodEmp = @CodEmp";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new ReuniaoModel
        {
            Codigo = Convert.ToInt32(reader["Codigo"]),
            CodEmp = Convert.ToInt32(reader["CodEmp"]),
            Titulo = reader["Titulo"]?.ToString() ?? "",
            CriadoPor = reader["CriadoPor"]?.ToString()?.Trim() ?? "",
            DataCriacao = Convert.ToDateTime(reader["DataCriacao"]),
            DataEncerramento = reader["DataEncerramento"] is DBNull ? null : Convert.ToDateTime(reader["DataEncerramento"]),
            Status = reader["Status"]?.ToString() ?? "Aberta",
            LimiteParticipantes = Convert.ToInt32(reader["LimiteParticipantes"])
        };
    }

    public bool EhOrganizador(ReuniaoModel? reuniao)
        => reuniao is not null && reuniao.CriadoPor.Trim().Equals(UsuarioAtual, StringComparison.OrdinalIgnoreCase);

    public async Task<List<ReuniaoParticipanteModel>> ListarParticipantesAsync(int codReuniao)
    {
        await GarantirAcessoAsync(codReuniao, exigirAberta: false);

        const string sql = @"
            SELECT LTRIM(RTRIM(p.Usuario)) AS Usuario, p.Situacao, p.Resposta,
                   p.DataEntrada, p.DataSaida,
                   CASE WHEN ISNULL(u.Online, 'N') = 'S'
                             AND u.UltimaAtividadeOnline IS NOT NULL
                             AND DATEDIFF(SECOND, u.UltimaAtividadeOnline, GETDATE()) <= 90
                        THEN 1 ELSE 0 END AS Online
            FROM ReuniaoHelpParticipante p
            INNER JOIN ReuniaoHelp r ON r.Codigo = p.CodReuniao AND r.CodEmp = @CodEmp
            LEFT JOIN Usuario u ON UPPER(LTRIM(RTRIM(u.Usuario))) = UPPER(LTRIM(RTRIM(p.Usuario))) AND u.CodEmp = r.CodEmp
            WHERE p.CodReuniao = @CodReuniao
            ORDER BY CASE WHEN p.Situacao = 'Organizador' THEN 0 ELSE 1 END, p.Usuario";

        var naSala = _presenca.UsuariosNaSala(codReuniao).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var participantes = new List<ReuniaoParticipanteModel>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var usuario = reader["Usuario"]?.ToString()?.Trim() ?? "";
            var estaNaSala = naSala.Contains(usuario);
            var resposta = reader["Resposta"] is DBNull ? null : reader["Resposta"]?.ToString();
            var entrou = reader["DataEntrada"] is not DBNull;

            participantes.Add(new ReuniaoParticipanteModel
            {
                CodReuniao = codReuniao,
                Usuario = usuario,
                NomeExibicao = ReuniaoVisual.Nome(usuario),
                Papel = reader["Situacao"]?.ToString() ?? "Convidado",
                Online = Convert.ToInt32(reader["Online"]) == 1,
                NaSala = estaNaSala,
                Situacao = estaNaSala ? "Na sala"
                    : resposta == "Recusado" ? "Recusou"
                    : entrou ? "Saiu"
                    : "Aguardando"
            });
        }

        // Na sala primeiro, depois organizador, depois os demais
        return participantes
            .OrderByDescending(p => p.NaSala)
            .ThenBy(p => p.Papel == "Organizador" ? 0 : 1)
            .ThenBy(p => p.NomeExibicao)
            .ToList();
    }

    public async Task<List<ReuniaoMensagemModel>> ListarMensagensAsync(int codReuniao)
    {
        await GarantirAcessoAsync(codReuniao, exigirAberta: false);
        const string sql = @"
            SELECT TOP 200 Codigo, CodReuniao, Usuario, Mensagem, DataHora
            FROM ReuniaoHelpMensagem
            WHERE CodReuniao = @CodReuniao
            ORDER BY DataHora DESC, Codigo DESC";

        var mensagens = new List<ReuniaoMensagemModel>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            mensagens.Add(new ReuniaoMensagemModel
            {
                Codigo = Convert.ToInt32(reader["Codigo"]),
                CodReuniao = Convert.ToInt32(reader["CodReuniao"]),
                Usuario = reader["Usuario"]?.ToString()?.Trim() ?? "",
                Mensagem = reader["Mensagem"]?.ToString() ?? "",
                DataHora = Convert.ToDateTime(reader["DataHora"])
            });
        }
        mensagens.Reverse();
        return mensagens;
    }

    public async Task AdicionarMensagemAsync(int codReuniao, string mensagem)
    {
        await GarantirAcessoAsync(codReuniao, exigirAberta: true);
        var texto = (mensagem ?? "").Trim();
        if (string.IsNullOrWhiteSpace(texto)) return;
        if (texto.Length > 2000) texto = texto[..2000];

        const string sql = @"
            INSERT INTO ReuniaoHelpMensagem (CodReuniao, Usuario, Mensagem)
            VALUES (@CodReuniao, @Usuario, @Mensagem);";
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        cmd.Parameters.AddWithValue("@Mensagem", texto);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Usado pelo hub (não depende do usuário logado no circuito).</summary>
    public async Task<bool> PodeEntrarAsync(int codReuniao, int codEmp, string usuario)
        => await ParticipaAsync(codReuniao, codEmp, usuario, exigirAberta: true);

    public async Task AtualizarPresencaAsync(int codReuniao, int codEmp, string usuario, bool entrou)
    {
        const string sql = @"
            UPDATE p
            SET DataEntrada = CASE WHEN @Entrou = 1 THEN GETDATE() ELSE p.DataEntrada END,
                DataSaida   = CASE WHEN @Entrou = 0 THEN GETDATE() ELSE NULL END,
                Resposta    = CASE WHEN @Entrou = 1 THEN 'Aceito' ELSE p.Resposta END
            FROM ReuniaoHelpParticipante p
            INNER JOIN ReuniaoHelp r ON r.Codigo = p.CodReuniao
            WHERE p.CodReuniao = @CodReuniao
              AND r.CodEmp = @CodEmp
              AND UPPER(LTRIM(RTRIM(p.Usuario))) = @Usuario";

        try
        {
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Entrou", entrou ? 1 : 0);
            cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Usuario", usuario.Trim().ToUpperInvariant());
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Histórico de presença não pode derrubar a chamada
        }
    }

    // ============================================================
    // AUXILIARES
    // ============================================================

    // Reunião esquecida aberta por mais de 12h é encerrada automaticamente
    private async Task EncerrarAbandonadasAsync()
    {
        const string sql = @"
            SELECT Codigo FROM ReuniaoHelp
            WHERE CodEmp = @CodEmp AND Status = 'Aberta' AND DataCriacao < DATEADD(HOUR, -12, GETDATE());";

        try
        {
            var codigos = new List<int>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) codigos.Add(reader.GetInt32(0));
            }

            foreach (var codigo in codigos.Where(c => _presenca.UsuariosNaSala(c).Count == 0))
            {
                using var upd = new SqlCommand(@"
                    UPDATE ReuniaoHelp SET Status = 'Encerrada', DataEncerramento = GETDATE()
                    WHERE Codigo = @Codigo AND Status = 'Aberta';
                    UPDATE ReuniaoHelpParticipante SET DataSaida = GETDATE()
                    WHERE CodReuniao = @Codigo AND DataEntrada IS NOT NULL AND DataSaida IS NULL;", conn);
                upd.Parameters.AddWithValue("@Codigo", codigo);
                await upd.ExecuteNonQueryAsync();
            }
        }
        catch { }
    }

    private async Task<List<ReuniaoModel>> ListarReunioesAsync(string sql)
    {
        var lista = new List<ReuniaoModel>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lista.Add(new ReuniaoModel
            {
                Codigo = Convert.ToInt32(reader["Codigo"]),
                CodEmp = Convert.ToInt32(reader["CodEmp"]),
                Titulo = reader["Titulo"]?.ToString() ?? "",
                CriadoPor = reader["CriadoPor"]?.ToString()?.Trim() ?? "",
                DataCriacao = Convert.ToDateTime(reader["DataCriacao"]),
                DataEncerramento = reader["DataEncerramento"] is DBNull ? null : Convert.ToDateTime(reader["DataEncerramento"]),
                Status = reader["Status"]?.ToString() ?? "Aberta",
                LimiteParticipantes = Convert.ToInt32(reader["LimiteParticipantes"]),
                QtdParticipantes = Convert.ToInt32(reader["QtdParticipantes"]),
                QtdMensagens = Convert.ToInt32(reader["QtdMensagens"]),
                Participantes = (reader["Participantes"] is DBNull ? "" : reader["Participantes"]?.ToString() ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                MeuPapel = reader["MeuPapel"]?.ToString() ?? "Convidado",
                MinhaResposta = reader["MinhaResposta"] is DBNull ? null : reader["MinhaResposta"]?.ToString(),
                JaEntrei = Convert.ToInt32(reader["JaEntrei"]) == 1,
                ConvidadoPor = reader["ConvidadoPor"]?.ToString()?.Trim() ?? "",
                DataConvite = Convert.ToDateTime(reader["DataConvite"])
            });
        }
        return lista;
    }

    private async Task<List<string>> ListarUsuariosDaReuniaoAsync(int codReuniao)
    {
        var lista = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(
            "SELECT UPPER(LTRIM(RTRIM(Usuario))) FROM ReuniaoHelpParticipante WHERE CodReuniao = @CodReuniao", conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lista.Add(reader.GetString(0));
        return lista;
    }

    private void NotificarConvite(int codReuniao, string titulo, List<string> convidados)
        => _notifier.Notificar(new ReuniaoEvento
        {
            Tipo = "Convite",
            CodEmp = CodEmpAtual,
            CodReuniao = codReuniao,
            Titulo = titulo,
            Remetente = UsuarioAtual,
            Destinatarios = convidados
        });

    private IEnumerable<string> NormalizarUsuarios(IEnumerable<string>? usuarios)
        => (usuarios ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x != UsuarioAtual)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private async Task<bool> ParticipaAsync(int codReuniao, int codEmp, string usuario, bool exigirAberta)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM ReuniaoHelp r
            INNER JOIN ReuniaoHelpParticipante p ON p.CodReuniao = r.Codigo
            WHERE r.Codigo = @CodReuniao
              AND r.CodEmp = @CodEmp
              AND (@ExigirAberta = 0 OR r.Status = 'Aberta')
              AND UPPER(LTRIM(RTRIM(p.Usuario))) = @Usuario";

        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@ExigirAberta", exigirAberta ? 1 : 0);
        cmd.Parameters.AddWithValue("@Usuario", (usuario ?? "").Trim().ToUpperInvariant());
        await conn.OpenAsync();
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task GarantirAcessoAsync(int codReuniao, bool exigirAberta)
    {
        if (!await ParticipaAsync(codReuniao, CodEmpAtual, UsuarioAtual, exigirAberta))
            throw new UnauthorizedAccessException("Você não faz parte desta reunião.");
    }

    private static async Task InserirParticipanteAsync(SqlConnection conn, SqlTransaction transaction,
        int codReuniao, string usuario, string situacao)
    {
        const string sql = @"
            INSERT INTO ReuniaoHelpParticipante (CodReuniao, Usuario, Situacao, ConvidadoPor, DataConvite)
            VALUES (@CodReuniao, @Usuario, @Situacao, @ConvidadoPor, GETDATE());";
        using var cmd = new SqlCommand(sql, conn, transaction);
        cmd.Parameters.AddWithValue("@CodReuniao", codReuniao);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Situacao", situacao);
        cmd.Parameters.AddWithValue("@ConvidadoPor", situacao == "Organizador" ? usuario : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
