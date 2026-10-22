using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using EvolutCRM.Helpers;

namespace EvolutCRM.Services;

/// <summary>
/// Indique e Ganhe: o cliente indica contatos pelo link pessoal; cada indicação
/// "Confirmada" (virou cliente) dá R$ 10 de desconto na mensalidade (ContaReceber.Valor).
/// Métodos públicos recebem CodEmp/CodCliente explícitos (a página do cliente não tem login).
/// </summary>
public sealed class IndicacaoService
{
    // CNPJ/CPF da coluna Cliente.Cpf só com números (o cadastro pode ter pontos, barra e traço)
    private const string SQL_DOC = "REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Cpf, ''), '.', ''), '/', ''), '-', ''), ' ', '')";

    private const int LimitePorDia = 20;

    private readonly string _conn;
    private readonly UserState _userState;

    public IndicacaoService(IConfiguration configuration, UserState userState)
    {
        _conn = configuration.GetConnectionString("Connection")!;
        _userState = userState;
    }

    private int CodEmpAtual => _userState.CurrentCompanyId;
    private string UsuarioAtual => (_userState.CurrentUser ?? "").Trim().ToUpperInvariant();

    // ============================================================
    // ACESSO PELO LINK
    // ============================================================

    public async Task<IndicacaoAcessoModel?> ObterAcessoPorTokenAsync(string? token)
    {
        token = (token ?? "").Trim().ToLowerInvariant();
        if (token.Length < 20 || token.Length > 64 || !token.All(char.IsLetterOrDigit))
            return null;

        const string sql = @"
            SELECT A.CodEmp, A.CodCliente, A.Token,
                   ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)), ''), ISNULL(C.Nome, '')) AS NomeCliente,
                   REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Cpf, ''), '.', ''), '/', ''), '-', ''), ' ', '') AS Documento
            FROM ClienteIndicacaoAcesso A
            INNER JOIN Cliente C ON C.Codigo = A.CodCliente AND C.CodEmp = A.CodEmp
            WHERE A.Token = @Token AND A.Ativo = 'S';

            UPDATE ClienteIndicacaoAcesso SET UltimoAcesso = GETDATE() WHERE Token = @Token;";

        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Token", token);
        await conn.OpenAsync();
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        return new IndicacaoAcessoModel
        {
            CodEmp = Convert.ToInt32(rd["CodEmp"]),
            CodCliente = Convert.ToInt32(rd["CodCliente"]),
            Token = rd["Token"]?.ToString() ?? "",
            NomeCliente = rd["NomeCliente"]?.ToString()?.Trim() ?? "",
            Documento = rd["Documento"]?.ToString() ?? ""
        };
    }

    /// <summary>
    /// Link pelo CNPJ/CPF do cliente (coluna Cliente.Cpf). Retorna null se não achar
    /// ou se o documento existir em mais de um cadastro da empresa.
    /// </summary>
    public async Task<(int CodCliente, string Token)?> ObterTokenPorDocumentoAsync(int codEmp, string documento)
    {
        var doc = IndicacaoFormato.SomenteNumeros(documento);
        if (doc.Length is not (11 or 14)) return null;

        var codigos = new List<int>();
        using (var conn = new SqlConnection(_conn))
        using (var cmd = new SqlCommand($@"
            SELECT C.Codigo FROM Cliente C
            WHERE C.CodEmp = @CodEmp
              AND ISNULL(C.Inativo, 'N') <> 'S'
              AND {SQL_DOC} = @Doc;", conn))
        {
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@Doc", doc);
            await conn.OpenAsync();
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) codigos.Add(Convert.ToInt32(rd[0]));
        }

        if (codigos.Count != 1) return null;
        return (codigos[0], await ObterOuCriarTokenAsync(codEmp, codigos[0]));
    }

    /// <summary>Token do link pessoal (cria se ainda não existir).</summary>
    public async Task<string> ObterOuCriarTokenAsync(int codEmp, int codCliente)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM ClienteIndicacaoAcesso WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente)
                INSERT INTO ClienteIndicacaoAcesso (CodEmp, CodCliente, Token) VALUES (@CodEmp, @CodCliente, @Novo);
            SELECT Token FROM ClienteIndicacaoAcesso WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente;";

        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@CodCliente", codCliente);
        cmd.Parameters.AddWithValue("@Novo", Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant());
        await conn.OpenAsync();
        return (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
    }

    // ============================================================
    // PÁGINA DO CLIENTE
    // ============================================================

    public async Task<List<IndicacaoModel>> ListarDoClienteAsync(int codEmp, int codCliente)
        => await ListarAsync(codEmp, codCliente, null, null);

    public async Task<IndicacaoMensalidadeModel> ObterMensalidadeAsync(int codEmp, int codCliente)
    {
        // Mensalidade = próximo título em aberto; se não houver, o último título do cliente
        const string sql = @"
            SELECT TOP 1 Valor, DataVencimento, CASE WHEN DataPagamento IS NULL THEN 1 ELSE 0 END AS EmAberto
            FROM ContaReceber
            WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente AND ISNULL(Valor, 0) > 0
            ORDER BY CASE WHEN DataPagamento IS NULL THEN 0 ELSE 1 END,
                     CASE WHEN DataPagamento IS NULL THEN DataVencimento END ASC,
                     DataVencimento DESC;

            SELECT COUNT(1) FROM ClienteIndicacao
            WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente AND Status = 'Confirmada';

            -- Valor cheio do plano + se é mensalista (só mensalista ganha desconto)
            SELECT CASE WHEN UPPER(ISNULL(ClienteMensalista, 'N')) = 'S' THEN 1 ELSE 0 END AS Mensalista,
                   ValorMensalidade
            FROM Cliente WHERE Codigo = @CodCliente AND CodEmp = @CodEmp;";

        var m = new IndicacaoMensalidadeModel();
        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@CodCliente", codCliente);
        await conn.OpenAsync();
        using var rd = await cmd.ExecuteReaderAsync();
        if (await rd.ReadAsync())
        {
            m.ValorTitulo = rd["Valor"] is DBNull ? null : Convert.ToDecimal(rd["Valor"]);
            m.Vencimento = rd["DataVencimento"] is DBNull ? null : Convert.ToDateTime(rd["DataVencimento"]);
            m.EmAberto = Convert.ToInt32(rd["EmAberto"]) == 1;
        }
        if (await rd.NextResultAsync() && await rd.ReadAsync())
            m.QtdConfirmadasRegistradas = Convert.ToInt32(rd[0]);
        if (await rd.NextResultAsync() && await rd.ReadAsync())
        {
            m.Mensalista = Convert.ToInt32(rd["Mensalista"]) == 1;
            m.ValorMensalidade = rd["ValorMensalidade"] is DBNull ? null : Convert.ToDecimal(rd["ValorMensalidade"]);
        }

        return m;
    }

    public async Task<int> CriarAsync(int codEmp, int codCliente, IndicacaoNovaRequest req, string origem = "Cliente")
    {
        var nomeContato = Limpar(req.NomeContato, 150);
        var fantasia = Limpar(req.NomeFantasia, 150);
        var segmento = Limpar(req.Segmento, 80);
        var whats = IndicacaoFormato.NormalizarWhatsApp(req.WhatsApp);

        if (nomeContato.Length < 2) throw new InvalidOperationException("Informe o nome do contato.");
        if (fantasia.Length < 2) throw new InvalidOperationException("Informe o nome fantasia da empresa.");
        if (whats.Length == 0) throw new InvalidOperationException("Informe um WhatsApp válido com DDD. Ex.: (17) 99999-9999.");
        if (!IndicacaoFormato.Segmentos.Contains(segmento, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha o segmento da empresa.");

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Mesmo número já indicado (por qualquer cliente) e ainda em andamento ou fechado
        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 CodCliente FROM ClienteIndicacao
            WHERE CodEmp = @CodEmp AND WhatsApp = @WhatsApp AND Status <> 'NaoFechou'", conn))
        {
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@WhatsApp", whats);
            var existente = await cmd.ExecuteScalarAsync();
            if (existente is not null and not DBNull)
                throw new InvalidOperationException(Convert.ToInt32(existente) == codCliente
                    ? "Você já indicou esse WhatsApp. Acompanhe o andamento na lista abaixo."
                    : "Esse contato já foi indicado e está sendo atendido pela nossa equipe.");
        }

        // Proteção contra envio em massa pelo link
        if (origem == "Cliente")
        {
            using var cmd = new SqlCommand(@"
                SELECT COUNT(1) FROM ClienteIndicacao
                WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente AND DataCriacao >= CAST(GETDATE() AS DATE)", conn);
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= LimitePorDia)
                throw new InvalidOperationException($"Você já fez {LimitePorDia} indicações hoje. Amanhã você pode indicar mais.");
        }

        using (var cmd = new SqlCommand(@"
            INSERT INTO ClienteIndicacao (CodEmp, CodCliente, NomeContato, NomeFantasia, WhatsApp, Segmento, Status, Origem)
            OUTPUT INSERTED.Codigo
            VALUES (@CodEmp, @CodCliente, @NomeContato, @NomeFantasia, @WhatsApp, @Segmento, 'Recebida', @Origem);", conn))
        {
            cmd.Parameters.AddWithValue("@CodEmp", codEmp);
            cmd.Parameters.AddWithValue("@CodCliente", codCliente);
            cmd.Parameters.AddWithValue("@NomeContato", nomeContato);
            cmd.Parameters.AddWithValue("@NomeFantasia", fantasia);
            cmd.Parameters.AddWithValue("@WhatsApp", whats);
            cmd.Parameters.AddWithValue("@Segmento", segmento);
            cmd.Parameters.AddWithValue("@Origem", origem);
            var codIndicacao = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            // Abre o card no Comercial. Se falhar, a indicação continua gravada
            // e o comercial pode abrir o card manualmente.
            try
            {
                var codCard = await CriarCardComercialAsync(conn, codEmp, codCliente, nomeContato, fantasia, whats, segmento);
                if (codCard > 0)
                {
                    using var cmdVinc = new SqlCommand(
                        "UPDATE ClienteIndicacao SET CodCRMC = @Card WHERE Codigo = @Codigo AND CodEmp = @CodEmp", conn);
                    cmdVinc.Parameters.AddWithValue("@Card", codCard);
                    cmdVinc.Parameters.AddWithValue("@Codigo", codIndicacao);
                    cmdVinc.Parameters.AddWithValue("@CodEmp", codEmp);
                    await cmdVinc.ExecuteNonQueryAsync();
                }
            }
            catch { /* não impede a indicação */ }

            return codIndicacao;
        }
    }

    /// <summary>
    /// Cria (ou reaproveita) o card do lead indicado no Comercial, igual ao lead que chega do anúncio:
    /// UsuarioCard = NOVO, Novo = S, funil "Novo Lead". Deixa uma nota interna com os dados da indicação.
    /// </summary>
    private static async Task<int> CriarCardComercialAsync(
        SqlConnection conn, int codEmp, int codClienteIndicou,
        string nomeContato, string fantasia, string whats, string segmento)
    {
        var telefone = TelefoneBR.Normalizar(whats);          // mesmo formato que o webhook grava
        var variantes = TelefoneBR.GerarVariantes(whats);
        if (variantes.Count == 0) variantes = new List<string> { telefone };

        var pTel = variantes.Select((_, i) => "@T" + i).ToList();
        var inTel = string.Join(",", pTel);

        const string LIMPA_WA =
            "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(TelefoneWhatsApp,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";
        const string LIMPA_CEL =
            "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(Celular,''),'(',''),')',''),'-',''),' ',''),'+',''),'.','')";

        // Quem indicou (aparece na nota e na observação do card)
        string quemIndicou = "";
        using (var cmd = new SqlCommand(
            "SELECT COALESCE(NULLIF(LTRIM(RTRIM(Apelido)), ''), Nome, '') FROM Cliente WHERE Codigo = @C AND CodEmp = @E", conn))
        {
            cmd.Parameters.AddWithValue("@C", codClienteIndicou);
            cmd.Parameters.AddWithValue("@E", codEmp);
            quemIndicou = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
        }

        // Instância Comercial: padrão primeiro, depois a conectada
        int codInstancia = 0;
        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 Codigo FROM WhatsAppInstancia
            WHERE CodEmp = @E
              AND (UPPER(Nome) LIKE '%COMERCIAL%' OR UPPER(Nome) LIKE '%CRMC%' OR UPPER(Nome) LIKE '%VENDA%')
            ORDER BY CASE WHEN Padrao = 'S' THEN 0 ELSE 1 END,
                     CASE WHEN Status = 'conectado' THEN 0 ELSE 1 END, Codigo", conn))
        {
            cmd.Parameters.AddWithValue("@E", codEmp);
            var r = await cmd.ExecuteScalarAsync();
            if (r is not null and not DBNull) codInstancia = Convert.ToInt32(r);
        }

        // Já existe card aberto desse número? Reaproveita.
        int codCard = 0;
        using (var cmd = new SqlCommand($@"
            SELECT TOP 1 Codigo FROM CRMC
            WHERE CodEmp = @E
              AND ({LIMPA_WA} IN ({inTel}) OR {LIMPA_CEL} IN ({inTel}))
              AND UPPER(ISNULL(Status,'')) NOT IN ('FECHADO','CONCLUIDO','PERDIDO')
            ORDER BY Codigo DESC", conn))
        {
            cmd.Parameters.AddWithValue("@E", codEmp);
            for (int i = 0; i < variantes.Count; i++) cmd.Parameters.AddWithValue(pTel[i], variantes[i]);
            var r = await cmd.ExecuteScalarAsync();
            if (r is not null and not DBNull) codCard = Convert.ToInt32(r);
        }

        var observacao = $"Indicação de {quemIndicou} · Segmento: {segmento}";

        if (codCard > 0)
        {
            using var cmd = new SqlCommand(@"
                UPDATE CRMC SET Novo = 'S', DataHoraUltimaGravacao = GETDATE(), UsuarioUltimaGravacao = 'INDICACAO'
                WHERE Codigo = @Codigo AND CodEmp = @E", conn);
            cmd.Parameters.AddWithValue("@Codigo", codCard);
            cmd.Parameters.AddWithValue("@E", codEmp);
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            using var cmd = new SqlCommand(@"
                INSERT INTO CRMC
                (Descricao, NomeCliente, TelefoneWhatsApp, Celular, Whats,
                 DataCriacao, DataHoraUltimaGravacao, UsuarioUltimaGravacao,
                 Funil, Status, UsuarioCard, Novo, ObservacaoCliente, CodInstanciaWhatsApp, CodEmp)
                OUTPUT INSERTED.Codigo
                VALUES
                (@Descricao, @NomeCliente, @Telefone, @Telefone, 'S',
                 CAST(GETDATE() AS DATE), GETDATE(), 'INDICACAO',
                 'RA', 'ABERTO', 'NOVO', 'S', @Obs, @Inst, @E);", conn);
            cmd.Parameters.AddWithValue("@Descricao", fantasia);
            cmd.Parameters.AddWithValue("@NomeCliente", nomeContato);
            cmd.Parameters.AddWithValue("@Telefone", telefone);
            cmd.Parameters.AddWithValue("@Obs", observacao);
            cmd.Parameters.AddWithValue("@Inst", codInstancia > 0 ? (object)codInstancia : DBNull.Value);
            cmd.Parameters.AddWithValue("@E", codEmp);
            codCard = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // Nota interna no histórico do card (StatusWhatsApp = 'interno' não é enviada ao lead)
        using (var cmd = new SqlCommand(@"
            INSERT INTO CRMAnotacao
            (CodCRMC, DataHora, Anotacao, Funil, Usuario,
             LidoCliente, LidoSuporte, Alterado, MensagemExcluida,
             EnvioCliente, StatusWhatsApp, CodEmp, CodInstancia)
            VALUES
            (@Card, GETDATE(), @Texto, 'RA', 'INDICACAO',
             'N', 'N', 'N', 'N',
             'N', 'interno', @E, @Inst);", conn))
        {
            cmd.Parameters.AddWithValue("@Card", codCard);
            cmd.Parameters.AddWithValue("@Texto",
                $"🤝 Indicação recebida\nContato: {nomeContato}\nEmpresa: {fantasia}\nSegmento: {segmento}\n" +
                $"WhatsApp: {IndicacaoFormato.Telefone(whats)}\nIndicado por: {quemIndicou}");
            cmd.Parameters.AddWithValue("@E", codEmp);
            cmd.Parameters.AddWithValue("@Inst", codInstancia > 0 ? (object)codInstancia : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        return codCard;
    }

    // ============================================================
    // TELA INTERNA (HELP)
    // ============================================================

    public Task<List<IndicacaoModel>> ListarEmpresaAsync(string? status, string? busca)
        => ListarAsync(CodEmpAtual, null, status, busca);

    public async Task<IndicacaoResumoModel> ResumoEmpresaAsync()
    {
        const string sql = @"
            SELECT COUNT(1),
                   SUM(CASE WHEN I.Status = 'Recebida'   THEN 1 ELSE 0 END),
                   SUM(CASE WHEN I.Status = 'EmContato'  THEN 1 ELSE 0 END),
                   SUM(CASE WHEN I.Status = 'Confirmada' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN I.Status = 'NaoFechou'  THEN 1 ELSE 0 END),
                   SUM(CASE WHEN I.Status = 'Confirmada' AND UPPER(ISNULL(C.ClienteMensalista, 'N')) = 'S' THEN 1 ELSE 0 END)
            FROM ClienteIndicacao I
            LEFT JOIN Cliente C ON C.Codigo = I.CodCliente AND C.CodEmp = I.CodEmp
            WHERE I.CodEmp = @CodEmp;";

        var r = new IndicacaoResumoModel();
        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        await conn.OpenAsync();
        using var rd = await cmd.ExecuteReaderAsync();
        if (await rd.ReadAsync())
        {
            int N(int i) => rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd[i]);
            r.Total = N(0); r.Recebidas = N(1); r.EmContato = N(2); r.Confirmadas = N(3); r.NaoFecharam = N(4);
            r.ConfirmadasValendo = N(5);
        }
        return r;
    }

    public async Task AtualizarAsync(int codigo, string status, string? observacao)
    {
        if (!IndicacaoStatus.Todos.Contains(status))
            throw new InvalidOperationException("Status inválido.");

        const string sql = @"
            UPDATE ClienteIndicacao
            SET DataStatus    = CASE WHEN Status <> @Status THEN GETDATE() ELSE DataStatus END,
                UsuarioStatus = CASE WHEN Status <> @Status THEN @Usuario ELSE UsuarioStatus END,
                Status        = @Status,
                Observacao    = @Observacao
            WHERE Codigo = @Codigo AND CodEmp = @CodEmp;";

        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Codigo", codigo);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
        cmd.Parameters.AddWithValue("@Observacao", (object?)Limpar(observacao, 500) ?? DBNull.Value);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<IndicacaoClienteBuscaModel>> BuscarClientesAsync(string termo)
    {
        termo = (termo ?? "").Trim();
        if (termo.Length < 2) return [];

        const string sql = @"
            SELECT TOP 15 C.Codigo, ISNULL(C.Nome, '') AS Nome, ISNULL(C.Apelido, '') AS Apelido,
                   REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Cpf, ''), '.', ''), '/', ''), '-', ''), ' ', '') AS Documento
            FROM Cliente C
            WHERE C.CodEmp = @CodEmp
              AND ISNULL(C.Inativo, 'N') <> 'S'
              AND (C.Nome LIKE @Busca OR C.Apelido LIKE @Busca OR CAST(C.Codigo AS VARCHAR(20)) = @Termo
                   OR (LEN(@Doc) >= 5 AND REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Cpf, ''), '.', ''), '/', ''), '-', ''), ' ', '') LIKE @DocLike))
            ORDER BY ISNULL(NULLIF(C.Apelido, ''), C.Nome);";

        var lista = new List<IndicacaoClienteBuscaModel>();
        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Busca", $"%{termo}%");
        cmd.Parameters.AddWithValue("@Termo", termo);
        var doc = IndicacaoFormato.SomenteNumeros(termo);
        cmd.Parameters.AddWithValue("@Doc", doc);
        cmd.Parameters.AddWithValue("@DocLike", $"%{doc}%");
        await conn.OpenAsync();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IndicacaoClienteBuscaModel
            {
                Codigo = Convert.ToInt32(rd["Codigo"]),
                Nome = rd["Nome"]?.ToString()?.Trim() ?? "",
                Apelido = rd["Apelido"]?.ToString()?.Trim() ?? "",
                Documento = rd["Documento"]?.ToString() ?? ""
            });
        }
        return lista;
    }

    public Task<string> ObterOuCriarTokenEmpresaAtualAsync(int codCliente)
        => ObterOuCriarTokenAsync(CodEmpAtual, codCliente);

    // ============================================================
    // AUXILIARES
    // ============================================================

    private async Task<List<IndicacaoModel>> ListarAsync(int codEmp, int? codCliente, string? status, string? busca)
    {
        busca = (busca ?? "").Trim();
        const string sql = @"
            SELECT I.Codigo, I.CodEmp, I.CodCliente, I.NomeContato, I.NomeFantasia, I.WhatsApp, I.Segmento,
                   I.Status, ISNULL(I.Observacao, '') AS Observacao, I.Origem, I.DataCriacao, I.DataStatus,
                   ISNULL(I.UsuarioStatus, '') AS UsuarioStatus,
                   ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)), ''), ISNULL(C.Nome, '')) AS NomeCliente,
                   CASE WHEN UPPER(ISNULL(C.ClienteMensalista, 'N')) = 'S' THEN 1 ELSE 0 END AS ClienteMensalista
            FROM ClienteIndicacao I
            LEFT JOIN Cliente C ON C.Codigo = I.CodCliente AND C.CodEmp = I.CodEmp
            WHERE I.CodEmp = @CodEmp
              AND (@CodCliente = 0 OR I.CodCliente = @CodCliente)
              AND (@Status = '' OR I.Status = @Status)
              AND (@Busca = '' OR I.NomeContato LIKE @BuscaLike OR I.NomeFantasia LIKE @BuscaLike
                   OR I.WhatsApp LIKE @BuscaNum OR C.Nome LIKE @BuscaLike OR C.Apelido LIKE @BuscaLike)
            ORDER BY CASE I.Status WHEN 'Recebida' THEN 0 WHEN 'EmContato' THEN 1 ELSE 2 END, I.DataCriacao DESC;";

        var digitos = IndicacaoFormato.SomenteNumeros(busca);
        var lista = new List<IndicacaoModel>();
        using var conn = new SqlConnection(_conn);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        cmd.Parameters.AddWithValue("@CodCliente", codCliente ?? 0);
        cmd.Parameters.AddWithValue("@Status", status ?? "");
        cmd.Parameters.AddWithValue("@Busca", busca);
        cmd.Parameters.AddWithValue("@BuscaLike", $"%{busca}%");
        cmd.Parameters.AddWithValue("@BuscaNum", digitos.Length >= 4 ? $"%{digitos}%" : "-");
        await conn.OpenAsync();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new IndicacaoModel
            {
                Codigo = Convert.ToInt32(rd["Codigo"]),
                CodEmp = Convert.ToInt32(rd["CodEmp"]),
                CodCliente = Convert.ToInt32(rd["CodCliente"]),
                NomeCliente = rd["NomeCliente"]?.ToString()?.Trim() ?? "",
                ClienteMensalista = Convert.ToInt32(rd["ClienteMensalista"]) == 1,
                NomeContato = rd["NomeContato"]?.ToString() ?? "",
                NomeFantasia = rd["NomeFantasia"]?.ToString() ?? "",
                WhatsApp = rd["WhatsApp"]?.ToString() ?? "",
                Segmento = rd["Segmento"]?.ToString() ?? "",
                Status = rd["Status"]?.ToString() ?? IndicacaoStatus.Recebida,
                Observacao = rd["Observacao"]?.ToString() ?? "",
                Origem = rd["Origem"]?.ToString() ?? "Cliente",
                DataCriacao = Convert.ToDateTime(rd["DataCriacao"]),
                DataStatus = rd["DataStatus"] is DBNull ? null : Convert.ToDateTime(rd["DataStatus"]),
                UsuarioStatus = rd["UsuarioStatus"]?.ToString() ?? ""
            });
        }
        return lista;
    }

    private static string Limpar(string? valor, int max)
    {
        var v = string.Join(' ', (valor ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return v.Length > max ? v[..max] : v;
    }
}
