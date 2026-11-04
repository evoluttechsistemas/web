using EvolutCRM.Data;
using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvolutCRM.Services;

/// <summary>
/// Histórico de treinamento do cliente (implantação + orientações do dia a dia).
///  - Treinamento inicial: itens fixos criados como "pendente" no primeiro acesso ao cliente.
///  - Tickets finalizados: a IA lê a conversa (inclusive notas internas e áudios) e registra o que foi ENSINADO.
///  - Manual: o atendente adiciona/edita/exclui (ligação, acesso remoto, presencial).
///  - Resumo em texto para mandar ao cliente pela resposta do ticket.
///
/// appsettings (opcional):
///   "Treinamento": { "ItensIniciais": [ { "Modulo": "pdv", "Titulo": "Abertura de caixa" }, ... ] }
/// Tabelas: Scripts/2026-10-26_ClienteTreinamento.sql
/// </summary>
public sealed class TreinamentoClienteService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiClient _openAi;
    private readonly EmbeddingIndex _index;
    private readonly IServiceScopeFactory _scopeFactory;

    public TreinamentoClienteService(IConfiguration config, IHttpClientFactory httpFactory, OpenAiClient openAi,
        EmbeddingIndex index, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
        _openAi = openAi;
        _index = index;
        _scopeFactory = scopeFactory;
    }

    private string ConnStr => _config.GetConnectionString("Connection")!;

    private static readonly ClienteTreinamentoItemInicial[] ItensIniciaisPadrao =
    {
        new() { Modulo = "cadastro_produto", Titulo = "Cadastro de produto (código de barras, preço e margem)" },
        new() { Modulo = "cadastro_produto", Titulo = "Cadastro de produto de balança" },
        new() { Modulo = "pdv", Titulo = "Abertura de caixa" },
        new() { Modulo = "pdv", Titulo = "Venda no PDV e formas de pagamento" },
        new() { Modulo = "pdv", Titulo = "Cancelamento de item e de venda" },
        new() { Modulo = "pdv", Titulo = "Sangria e suprimento" },
        new() { Modulo = "fechamento_caixa", Titulo = "Fechamento de caixa e conferência" },
    };

    public IReadOnlyList<ClienteTreinamentoItemInicial> ItensIniciais()
    {
        var cfg = _config.GetSection("Treinamento:ItensIniciais").GetChildren()
            .Select(s => new ClienteTreinamentoItemInicial
            {
                Modulo = ClienteTreinamentoModulos.Normalizar(s["Modulo"]),
                Titulo = (s["Titulo"] ?? "").Trim()
            })
            .Where(i => i.Titulo.Length > 0)
            .ToList();
        return cfg.Count > 0 ? cfg : ItensIniciaisPadrao;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Lista / edição
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Itens do cliente. Na primeira vez cria os itens do treinamento inicial como "pendente".</summary>
    public async Task<List<ClienteTreinamentoItem>> ListarAsync(int codEmp, int codCliente, bool garantirIniciais = true,
        string? usuario = null)
    {
        var lista = new List<ClienteTreinamentoItem>();
        if (codCliente <= 0) return lista;

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        if (garantirIniciais)
            await GarantirIniciaisAsync(conn, codEmp, codCliente, usuario);

        await using var cmd = new SqlCommand($@"
SELECT {Colunas}
  FROM dbo.ClienteTreinamento C
 WHERE CodEmp = @emp AND CodCliente = @cli AND Excluido = 'N'
 ORDER BY Inicial DESC,
          CASE WHEN Inicial = 1 THEN ISNULL((SELECT T.Ordem FROM dbo.TreinamentoTipo T WHERE T.Codigo = C.CodTreinamentoTipo), 99999) END,
          CASE WHEN Inicial = 1 THEN Codigo END,
          ISNULL(UltimaData, DataCriacao) DESC", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@cli", codCliente);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) lista.Add(Ler(rd));
        return lista;
    }

    private async Task GarantirIniciaisAsync(SqlConnection conn, int codEmp, int codCliente, string? usuario)
    {
        await using (var chk = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.ClienteTreinamento WHERE CodEmp = @emp AND CodCliente = @cli AND Inicial = 1", conn))
        {
            chk.Parameters.AddWithValue("@emp", codEmp);
            chk.Parameters.AddWithValue("@cli", codCliente);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync()) > 0) return;   // já criou (mesmo que tenha excluído algum)
        }

        foreach (var t in (await ListarTiposAsync(codEmp, incluirInativos: false, usuario)).Where(t => t.Ativo))
        {
            await using var ins = new SqlCommand(@"
INSERT INTO dbo.ClienteTreinamento
    (CodEmp, CodCliente, Titulo, Modulo, Status, Inicial, Origem, Vezes, UsuarioCriacao,
     CodTreinamentoTipo, CodDoc, CodEmpDoc, SlugDoc, TituloDoc)
VALUES (@emp, @cli, @t, @m, 'pendente', 1, 'inicial', 0, @u, @tipo, @doc, @empdoc, @slug, @tdoc);", conn);
            ins.Parameters.AddWithValue("@emp", codEmp);
            ins.Parameters.AddWithValue("@cli", codCliente);
            ins.Parameters.AddWithValue("@t", Cortar(t.Titulo, 200)!);
            ins.Parameters.AddWithValue("@m", t.Modulo);
            ins.Parameters.AddWithValue("@u", (object?)Cortar(usuario, 100) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@tipo", t.Codigo);
            ins.Parameters.AddWithValue("@doc", (object?)t.CodDoc ?? DBNull.Value);
            ins.Parameters.AddWithValue("@empdoc", (object?)t.CodEmpDoc ?? DBNull.Value);
            ins.Parameters.AddWithValue("@slug", (object?)t.SlugDoc ?? DBNull.Value);
            ins.Parameters.AddWithValue("@tdoc", (object?)t.TituloDoc ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Cadastro dos treinamentos (tela Cadastros → Treinamentos)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Treinamentos da empresa. Só no primeiro acesso da empresa preenche com a lista padrão para editar.</summary>
    public async Task<List<TreinamentoTipo>> ListarTiposAsync(int codEmp, bool incluirInativos = true, string? usuario = null)
    {
        var lista = new List<TreinamentoTipo>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        // A lista padrão é criada uma única vez por empresa. A marcação fica em
        // TreinamentoTipoListaPadrao, assim excluir todos os itens não traz a lista de volta.
        await using (var chk = new SqlCommand(@"
IF OBJECT_ID('dbo.TreinamentoTipoListaPadrao', 'U') IS NULL
    CREATE TABLE dbo.TreinamentoTipoListaPadrao
    (
        CodEmp      INT      NOT NULL CONSTRAINT PK_TreinamentoTipoListaPadrao PRIMARY KEY,
        DataCriacao DATETIME NOT NULL CONSTRAINT DF_TreinamentoTipoListaPadrao_Data DEFAULT (GETDATE())
    );

DECLARE @criar BIT = 0;
IF NOT EXISTS (SELECT 1 FROM dbo.TreinamentoTipoListaPadrao WHERE CodEmp = @emp)
BEGIN
    -- Empresa que já tinha cadastro antes desta marcação: só marca, não recria.
    IF NOT EXISTS (SELECT 1 FROM dbo.TreinamentoTipo WHERE CodEmp = @emp) SET @criar = 1;
    INSERT INTO dbo.TreinamentoTipoListaPadrao (CodEmp) VALUES (@emp);
END
SELECT @criar;", conn))
        {
            chk.Parameters.AddWithValue("@emp", codEmp);
            if (Convert.ToBoolean(await chk.ExecuteScalarAsync()))
            {
                int ordem = 0;
                foreach (var i in ItensIniciais())
                {
                    ordem += 10;
                    await using var ins = new SqlCommand(@"
INSERT INTO dbo.TreinamentoTipo (CodEmp, Titulo, Modulo, Ordem, Ativo, UsuarioGravacao)
VALUES (@emp, @t, @m, @o, 1, @u);", conn);
                    ins.Parameters.AddWithValue("@emp", codEmp);
                    ins.Parameters.AddWithValue("@t", Cortar(i.Titulo, 200)!);
                    ins.Parameters.AddWithValue("@m", i.Modulo);
                    ins.Parameters.AddWithValue("@o", ordem);
                    ins.Parameters.AddWithValue("@u", (object?)Cortar(usuario, 100) ?? DBNull.Value);
                    await ins.ExecuteNonQueryAsync();
                }
            }
        }

        await using var cmd = new SqlCommand(@"
SELECT Codigo, CodEmp, Titulo, Modulo, Ordem, Ativo, CodDoc, CodEmpDoc, SlugDoc, TituloDoc, DataGravacao, UsuarioGravacao
  FROM dbo.TreinamentoTipo
 WHERE CodEmp = @emp AND (@todos = 1 OR Ativo = 1)
 ORDER BY Ativo DESC, Ordem, Titulo;", conn);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@todos", incluirInativos);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new TreinamentoTipo
            {
                Codigo = rd.GetInt32(0),
                CodEmp = rd.GetInt32(1),
                Titulo = rd.GetString(2),
                Modulo = rd.GetString(3),
                Ordem = rd.GetInt32(4),
                Ativo = rd.GetBoolean(5),
                CodDoc = rd.IsDBNull(6) ? null : rd.GetInt32(6),
                CodEmpDoc = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                SlugDoc = rd.IsDBNull(8) ? null : rd.GetString(8),
                TituloDoc = rd.IsDBNull(9) ? null : rd.GetString(9),
                DataGravacao = rd.GetDateTime(10),
                UsuarioGravacao = rd.IsDBNull(11) ? null : rd.GetString(11)
            });
        }
        return lista;
    }

    public async Task<int> SalvarTipoAsync(TreinamentoTipo t, string usuario)
    {
        if (string.IsNullOrWhiteSpace(t.Titulo)) throw new InvalidOperationException("Informe o nome do treinamento.");
        t.Modulo = ClienteTreinamentoModulos.Normalizar(t.Modulo);

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(t.Codigo <= 0
            ? @"
INSERT INTO dbo.TreinamentoTipo (CodEmp, Titulo, Modulo, Ordem, Ativo, CodDoc, CodEmpDoc, SlugDoc, TituloDoc, UsuarioGravacao)
OUTPUT INSERTED.Codigo
VALUES (@emp, @t, @m, @o, @a, @doc, @empdoc, @slug, @tdoc, @u);"
            : @"
UPDATE dbo.TreinamentoTipo
   SET Titulo = @t, Modulo = @m, Ordem = @o, Ativo = @a, CodDoc = @doc, CodEmpDoc = @empdoc, SlugDoc = @slug, TituloDoc = @tdoc,
       DataGravacao = GETDATE(), UsuarioGravacao = @u
OUTPUT INSERTED.Codigo
 WHERE Codigo = @c AND CodEmp = @emp;", conn);
        cmd.Parameters.AddWithValue("@c", t.Codigo);
        cmd.Parameters.AddWithValue("@emp", t.CodEmp);
        cmd.Parameters.AddWithValue("@t", Cortar(t.Titulo.Trim(), 200)!);
        cmd.Parameters.AddWithValue("@m", t.Modulo);
        cmd.Parameters.AddWithValue("@o", t.Ordem);
        cmd.Parameters.AddWithValue("@a", t.Ativo);
        cmd.Parameters.AddWithValue("@doc", (object?)(t.CodDoc > 0 ? t.CodDoc : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@empdoc", (object?)(t.CodEmpDoc > 0 ? t.CodEmpDoc : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@slug", (object?)Vazio(t.SlugDoc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tdoc", (object?)Vazio(t.TituloDoc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
        var r = await cmd.ExecuteScalarAsync();
        var codigo = r == null || r == DBNull.Value ? t.Codigo : Convert.ToInt32(r);

        // Salvo como inativo (desmarcou "Ativo"): sai da lista dos clientes onde está pendente
        if (!t.Ativo && codigo > 0)
            await TirarPendentesDoTipoAsync(conn, codigo, t.CodEmp, usuario);
        return codigo;
    }

    /// <summary>
    /// Ativa/inativa. Ao inativar, o treinamento sai da lista dos clientes onde ainda está PENDENTE
    /// (onde já foi marcado como treinado, continua). Devolve quantos itens pendentes saíram.
    /// </summary>
    public async Task<int> AlterarAtivoTipoAsync(int codigo, int codEmp, bool ativo, string usuario)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using (var cmd = new SqlCommand(@"
UPDATE dbo.TreinamentoTipo SET Ativo = @a, DataGravacao = GETDATE(), UsuarioGravacao = @u
 WHERE Codigo = @c AND CodEmp = @emp;", conn))
        {
            cmd.Parameters.AddWithValue("@a", ativo);
            cmd.Parameters.AddWithValue("@c", codigo);
            cmd.Parameters.AddWithValue("@emp", codEmp);
            cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
            await cmd.ExecuteNonQueryAsync();
        }
        return ativo ? 0 : await TirarPendentesDoTipoAsync(conn, codigo, codEmp, usuario);
    }

    /// <summary>
    /// Exclui o treinamento do cadastro e tira dos clientes onde ainda está pendente.
    /// Onde já foi treinado, o item continua no histórico do cliente.
    /// </summary>
    public async Task<int> ExcluirTipoAsync(int codigo, int codEmp, string usuario)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        var removidos = await TirarPendentesDoTipoAsync(conn, codigo, codEmp, usuario);
        await using var cmd = new SqlCommand(
            "DELETE FROM dbo.TreinamentoTipo WHERE Codigo = @c AND CodEmp = @emp;", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        await cmd.ExecuteNonQueryAsync();
        return removidos;
    }

    private static async Task<int> TirarPendentesDoTipoAsync(SqlConnection conn, int codigo, int codEmp, string usuario)
    {
        await using var cmd = new SqlCommand(@"
UPDATE C SET Excluido = 'S', DataAlteracao = GETDATE(), UsuarioAlteracao = @u
  FROM dbo.ClienteTreinamento C
 WHERE C.CodEmp = @emp AND C.Inicial = 1 AND C.Status = 'pendente' AND C.Excluido = 'N'
   AND (C.CodTreinamentoTipo = @c
        OR (C.CodTreinamentoTipo IS NULL AND C.Titulo = (SELECT T.Titulo FROM dbo.TreinamentoTipo T WHERE T.Codigo = @c)));", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Leva a lista atual para os clientes que já têm o treinamento inicial criado:
    ///  - inclui como pendente os treinamentos novos/ativados;
    ///  - atualiza nome, módulo e tutorial dos itens ainda pendentes;
    ///  - tira os itens pendentes de treinamentos inativados ou que não estão no cadastro.
    /// O que já foi marcado como treinado nunca é mexido.
    /// </summary>
    public async Task<TreinamentoTipoAplicacao> AplicarTiposAosClientesAsync(int codEmp, string usuario)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            async Task<int> Exec(string sql)
            {
                await using var c = new SqlCommand(sql, conn, tx);
                c.Parameters.AddWithValue("@emp", codEmp);
                c.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
                return await c.ExecuteNonQueryAsync();
            }
            async Task<int> Escalar(string sql)
            {
                await using var c = new SqlCommand(sql, conn, tx);
                c.Parameters.AddWithValue("@emp", codEmp);
                return Convert.ToInt32(await c.ExecuteScalarAsync());
            }

            // 1) itens antigos (criados antes do cadastro) ligados pelo nome
            await Exec(@"
UPDATE C SET CodTreinamentoTipo = T.Codigo
  FROM dbo.ClienteTreinamento C
  JOIN dbo.TreinamentoTipo T ON T.CodEmp = C.CodEmp AND T.Titulo = C.Titulo
 WHERE C.CodEmp = @emp AND C.Inicial = 1 AND C.Excluido = 'N' AND C.CodTreinamentoTipo IS NULL;");

            var clientes = await Escalar(@"
SELECT COUNT(DISTINCT CodCliente) FROM dbo.ClienteTreinamento WHERE CodEmp = @emp AND Inicial = 1;");

            // 2) pendentes acompanham o cadastro (nome, módulo, tutorial)
            var atualizados = await Exec(@"
UPDATE C SET Titulo = T.Titulo, Modulo = T.Modulo,
             CodDoc = T.CodDoc, CodEmpDoc = T.CodEmpDoc, SlugDoc = T.SlugDoc, TituloDoc = T.TituloDoc,
             DataAlteracao = GETDATE(), UsuarioAlteracao = @u
  FROM dbo.ClienteTreinamento C
  JOIN dbo.TreinamentoTipo T ON T.Codigo = C.CodTreinamentoTipo AND T.Ativo = 1
 WHERE C.CodEmp = @emp AND C.Inicial = 1 AND C.Status = 'pendente' AND C.Excluido = 'N'
   AND (C.Titulo <> T.Titulo OR C.Modulo <> T.Modulo OR ISNULL(C.SlugDoc, '') <> ISNULL(T.SlugDoc, ''));");

            // 3) pendentes de treinamentos que vocês não fazem mais saem da lista
            var removidos = await Exec(@"
UPDATE C SET Excluido = 'S', DataAlteracao = GETDATE(), UsuarioAlteracao = @u
  FROM dbo.ClienteTreinamento C
 WHERE C.CodEmp = @emp AND C.Inicial = 1 AND C.Status = 'pendente' AND C.Excluido = 'N'
   AND NOT EXISTS (SELECT 1 FROM dbo.TreinamentoTipo T WHERE T.Codigo = C.CodTreinamentoTipo AND T.Ativo = 1);");

            // 4) treinamentos novos entram como pendentes para quem já tem a lista
            var incluidos = await Exec(@"
INSERT INTO dbo.ClienteTreinamento
    (CodEmp, CodCliente, Titulo, Modulo, Status, Inicial, Origem, Vezes, UsuarioCriacao,
     CodTreinamentoTipo, CodDoc, CodEmpDoc, SlugDoc, TituloDoc)
SELECT @emp, X.CodCliente, T.Titulo, T.Modulo, 'pendente', 1, 'inicial', 0, @u,
       T.Codigo, T.CodDoc, T.CodEmpDoc, T.SlugDoc, T.TituloDoc
  FROM (SELECT DISTINCT CodCliente FROM dbo.ClienteTreinamento WHERE CodEmp = @emp AND Inicial = 1) X
 CROSS JOIN dbo.TreinamentoTipo T
 WHERE T.CodEmp = @emp AND T.Ativo = 1
   AND NOT EXISTS (SELECT 1 FROM dbo.ClienteTreinamento C
                    WHERE C.CodEmp = @emp AND C.CodCliente = X.CodCliente AND C.Excluido = 'N'
                      AND (C.CodTreinamentoTipo = T.Codigo OR (C.Inicial = 1 AND C.Titulo = T.Titulo)));");

            await tx.CommitAsync();
            return new TreinamentoTipoAplicacao(clientes, incluidos, atualizados, removidos);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Inclui (Codigo = 0) ou altera um item.</summary>
    public async Task<int> SalvarAsync(ClienteTreinamentoItem item, string usuario)
    {
        if (item.CodCliente <= 0) throw new InvalidOperationException("Cliente não informado.");
        if (string.IsNullOrWhiteSpace(item.Titulo)) throw new InvalidOperationException("Informe o que foi treinado.");

        item.Modulo = ClienteTreinamentoModulos.Normalizar(item.Modulo);
        item.Status = item.Status == "pendente" ? "pendente" : "treinado";
        if (item.Treinado)
        {
            item.DataTreinamento ??= DateTime.Today;
            item.UltimaData ??= item.DataTreinamento;
            if (item.Vezes <= 0) item.Vezes = 1;
            if (string.IsNullOrWhiteSpace(item.Atendente)) item.Atendente = usuario;
        }

        // Treinado e sem tutorial escolhido: tenta achar o tutorial do assunto
        if (item.Treinado && string.IsNullOrWhiteSpace(item.SlugDoc) && !item.SemTutorial)
            await LigarTutorialAsync(item, item.CodEmp);

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        if (item.Codigo <= 0)
        {
            await using var ins = new SqlCommand(@"
INSERT INTO dbo.ClienteTreinamento
    (CodEmp, CodCliente, Titulo, Modulo, Descricao, Status, Inicial, Origem, CodTicketChamadoC, DataTreinamento, UltimaData,
     Vezes, Atendente, PessoaTreinada, Observacao, CodDoc, CodEmpDoc, SlugDoc, TituloDoc, IncluidoPorIA, UsuarioCriacao)
OUTPUT INSERTED.Codigo
VALUES
    (@emp, @cli, @t, @m, @d, @s, @ini, @o, @tk, @dt, @ud, @v, @at, @pt, @obs, @doc, @empdoc, @slug, @tdoc, @ia, @u);", conn);
            Parametros(ins, item, usuario);
            return Convert.ToInt32(await ins.ExecuteScalarAsync());
        }

        await using var upd = new SqlCommand(@"
UPDATE dbo.ClienteTreinamento
   SET Titulo = @t, Modulo = @m, Descricao = @d, Status = @s, Origem = @o, CodTicketChamadoC = @tk,
       DataTreinamento = @dt, UltimaData = @ud, Vezes = @v, Atendente = @at, PessoaTreinada = @pt, Observacao = @obs,
       CodDoc = @doc, CodEmpDoc = @empdoc, SlugDoc = @slug, TituloDoc = @tdoc,
       DataAlteracao = GETDATE(), UsuarioAlteracao = @u
 WHERE Codigo = @cod AND CodEmp = @emp AND CodCliente = @cli;", conn);
        Parametros(upd, item, usuario);
        upd.Parameters.AddWithValue("@cod", item.Codigo);
        await upd.ExecuteNonQueryAsync();
        return item.Codigo;
    }

    public async Task ExcluirAsync(int codigo, int codEmp, string usuario)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
UPDATE dbo.ClienteTreinamento SET Excluido = 'S', DataAlteracao = GETDATE(), UsuarioAlteracao = @u
 WHERE Codigo = @c AND CodEmp = @emp;", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@emp", codEmp);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Só tutoriais (campo "Tutorial" do formulário).</summary>
    public async Task<List<TreinamentoSugestao>> SugerirTutoriaisAsync(int codEmp, string termo)
        => (await SugerirAsync(codEmp, termo)).Where(s => !string.IsNullOrWhiteSpace(s.SlugDoc) && s.CodDoc > 0).ToList();

    /// <summary>Sugestões para o campo "o que foi treinado": tutoriais e itens já usados com outros clientes.</summary>
    public async Task<List<TreinamentoSugestao>> SugerirAsync(int codEmp, string termo)
    {
        var lista = new List<TreinamentoSugestao>();
        termo = (termo ?? "").Trim();
        if (termo.Length < 2) return lista;

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        try
        {
            await using var cmd = new SqlCommand(@"
SELECT TOP 8 T.Codigo, ISNULL(T.Titulo, ''), ISNULL(T.Slug, ''), ISNULL(T.CodEmp, 0)
  FROM DocumentacaoTutorial T
 WHERE (T.CodEmp = @emp OR ISNULL(T.CodEmp, 0) = 0)
   AND T.Titulo LIKE @q
 ORDER BY T.Titulo;", conn);
            cmd.Parameters.AddWithValue("@emp", codEmp);
            cmd.Parameters.AddWithValue("@q", "%" + termo + "%");
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lista.Add(new TreinamentoSugestao(rd.GetString(1), null, rd.GetInt32(0), rd.GetString(2), rd.GetInt32(3)));
        }
        catch (Exception ex)
        {
            LogHelper.Log("[TREINAMENTO] Sugestão de tutoriais: " + ex.Message);
        }

        await using (var cmd = new SqlCommand(@"
SELECT TOP 8 Titulo, MAX(Modulo), MAX(CodDoc), MAX(SlugDoc), MAX(CodEmpDoc), COUNT(*) AS Qtd
  FROM dbo.ClienteTreinamento
 WHERE CodEmp = @emp AND Excluido = 'N' AND Titulo LIKE @q
 GROUP BY Titulo
 ORDER BY Qtd DESC;", conn))
        {
            cmd.Parameters.AddWithValue("@emp", codEmp);
            cmd.Parameters.AddWithValue("@q", "%" + termo + "%");
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var titulo = rd.GetString(0);
                if (lista.Any(s => Normalizar(s.Titulo) == Normalizar(titulo))) continue;
                lista.Add(new TreinamentoSugestao(titulo,
                    rd.IsDBNull(1) ? null : rd.GetString(1),
                    rd.IsDBNull(2) ? null : rd.GetInt32(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? null : rd.GetInt32(4)));
            }
        }
        return lista.Take(12).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Ticket finalizado → a IA registra o que foi ensinado
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Roda depois da finalização, sem segurar a tela do atendente.</summary>
    public void ProcessarTicketFinalizadoEmSegundoPlano(int codTicket, int codEmp, string usuario)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<TreinamentoClienteService>();
                var n = await svc.ProcessarTicketFinalizadoAsync(codTicket, codEmp, usuario);
                if (n > 0) LogHelper.Log($"[TREINAMENTO] Ticket #{codTicket}: {n} item(ns) de treinamento registrado(s).");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[TREINAMENTO] Ticket #{codTicket}: erro ao registrar treinamento: {ex.Message}");
            }
        });
    }

    /// <returns>Quantos itens foram registrados/atualizados.</returns>
    public async Task<int> ProcessarTicketFinalizadoAsync(int codTicket, int codEmp, string usuario)
    {
        if (codTicket <= 0 || string.IsNullOrWhiteSpace(_config["OpenAI:ApiKey"])) return 0;

        int codCliente, ultimoD;
        await using (var conn = new SqlConnection(ConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
SELECT T.CodCliente, ISNULL(T.CodEmp, 0),
       (SELECT ISNULL(MAX(D.Codigo), 0) FROM TicketChamadoD D WHERE D.CodTicketChamadoC = T.Codigo),
       (SELECT P.UltimoCodigoD FROM dbo.ClienteTreinamentoTicket P WHERE P.CodTicketChamadoC = T.Codigo)
  FROM TicketChamadoC T
 WHERE T.Codigo = @t;", conn);
            cmd.Parameters.AddWithValue("@t", codTicket);
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return 0;
            codCliente = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]);
            if (codEmp <= 0) codEmp = Convert.ToInt32(rd[1]);
            ultimoD = Convert.ToInt32(rd[2]);
            var jaLido = rd.IsDBNull(3) ? -1 : rd.GetInt32(3);
            if (codCliente <= 0) return 0;
            if (jaLido >= ultimoD) return 0;   // nada novo desde a última análise
        }

        // Conversa inteira (Cliente / Atendente / Nota interna / áudio transcrito)
        string texto;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<TicketQueryRepository>();
            texto = (await repo.MontarParaInsightAsync(codTicket, codEmp, 14000, incluirImagens: false)).Texto;
        }
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        var existentes = await ListarAsync(codEmp, codCliente, garantirIniciais: true, usuario);
        var detectados = await DetectarAsync(texto, existentes);

        int registrados = 0;
        foreach (var d in detectados)
        {
            var igual = existentes.FirstOrDefault(e => Normalizar(e.Titulo) == Normalizar(d.Titulo));
            if (igual != null)
            {
                if (igual.CodTicketChamadoC == codTicket && igual.Treinado) continue;   // já contado neste ticket
                await RegistrarRepeticaoAsync(igual.Codigo, codTicket, usuario, d.Descricao);
                registrados++;
                continue;
            }

            var item = new ClienteTreinamentoItem
            {
                CodEmp = codEmp,
                CodCliente = codCliente,
                Titulo = Cortar(d.Titulo, 200)!,
                Modulo = d.Modulo,
                Descricao = Cortar(d.Descricao, 1000),
                Status = "treinado",
                Origem = "ticket",
                CodTicketChamadoC = codTicket,
                DataTreinamento = DateTime.Now,
                UltimaData = DateTime.Now,
                Vezes = 1,
                Atendente = usuario,
                IncluidoPorIA = true
            };
            await LigarTutorialAsync(item, codEmp);
            await SalvarAsync(item, usuario);
            existentes.Add(item);
            registrados++;
        }

        await using (var conn = new SqlConnection(ConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
MERGE dbo.ClienteTreinamentoTicket AS A
USING (SELECT @t AS CodTicketChamadoC) AS B ON A.CodTicketChamadoC = B.CodTicketChamadoC
WHEN MATCHED THEN UPDATE SET UltimoCodigoD = @d, QtdItens = A.QtdItens + @n, DataProcessamento = GETDATE()
WHEN NOT MATCHED THEN INSERT (CodTicketChamadoC, UltimoCodigoD, QtdItens) VALUES (@t, @d, @n);", conn);
            cmd.Parameters.AddWithValue("@t", codTicket);
            cmd.Parameters.AddWithValue("@d", ultimoD);
            cmd.Parameters.AddWithValue("@n", registrados);
            await cmd.ExecuteNonQueryAsync();
        }
        return registrados;
    }

    /// <summary>Mesmo assunto ensinado de novo: atualiza data, conta mais uma vez e marca o item inicial como treinado.</summary>
    private async Task RegistrarRepeticaoAsync(int codigo, int codTicket, string usuario, string? descricao)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
UPDATE dbo.ClienteTreinamento
   SET Vezes = Vezes + 1,
       UltimaData = GETDATE(),
       DataTreinamento = ISNULL(DataTreinamento, GETDATE()),
       Status = 'treinado',
       Origem = CASE WHEN Origem = 'inicial' THEN 'inicial' ELSE 'ticket' END,
       CodTicketChamadoC = @tk,
       Atendente = @u,
       Descricao = CASE WHEN ISNULL(Descricao, '') = '' THEN @d ELSE Descricao END,
       DataAlteracao = GETDATE(), UsuarioAlteracao = @u
 WHERE Codigo = @c;", conn);
        cmd.Parameters.AddWithValue("@c", codigo);
        cmd.Parameters.AddWithValue("@tk", codTicket);
        cmd.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
        cmd.Parameters.AddWithValue("@d", (object?)Cortar(descricao, 1000) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<TreinamentoDetectado>> DetectarAsync(string conversa, List<ClienteTreinamentoItem> existentes)
    {
        var modelo = _config["OpenAI:Model"] ?? "gpt-4.1-mini";
        var jaTem = existentes.Count == 0
            ? "(nenhum)"
            : string.Join("\n", existentes.Select(e => "- " + e.Titulo));
        var modulos = string.Join(", ", ClienteTreinamentoModulos.Lista.Select(m => m.Codigo));

        var body = new
        {
            model = modelo,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = $@"Você analisa um atendimento do suporte da EvolutTech (sistema Evolut, ERP para varejo: PDV, fiscal, estoque, financeiro, etiquetas, balança)
e lista o que foi ENSINADO ao cliente sobre como USAR o sistema.

CONTA COMO ENSINADO
- O atendente explicou como fazer algo no sistema, onde fica uma opção ou como usar uma rotina (por mensagem, áudio ou print).
- Nota interna ou áudio dizendo que ensinou algo (ex.: ""ensinado por ligação: sangria e fechamento de caixa"").
- Explicação de como o próprio cliente resolve algo da próxima vez (ex.: como reenviar uma NF-e rejeitada).

NÃO CONTA
- Erro ou problema corrigido pelo suporte (ajuste no banco, acesso remoto para arrumar, reinstalação, atualização de versão).
- Envio de boleto, arquivo, cobrança, conversa comercial, saudação, agradecimento.
- Assunto só perguntado pelo cliente, sem explicação do atendente.

COMO ESCREVER
- Um item por assunto. Título curto (até 70 caracteres), começando pelo assunto: ""Entrada de mercadoria por XML"", ""Sangria no PDV"", ""Cancelamento de NFC-e"".
- Se o assunto já está na lista do cliente abaixo, use EXATAMENTE o mesmo título de lá.
- modulo: um destes: {modulos}
- descricao: 1 ou 2 frases com o que foi explicado (sem nome do cliente).
- Nada ensinado: lista vazia.

Responda só JSON: {{ ""itens"": [ {{ ""titulo"": """", ""modulo"": """", ""descricao"": """" }} ] }}" },
                new { role = "user", content = "ITENS QUE O CLIENTE JÁ TEM NO HISTÓRICO:\n" + jaTem + "\n\nATENDIMENTO:\n" + conversa }
            }
        };

        var http = _httpFactory.CreateClient("openai");
        using var resp = await http.PostAsync("v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            LogHelper.Log($"[TREINAMENTO] OpenAI HTTP {(int)resp.StatusCode}: {Cortar(raw, 400)}");
            return new();
        }

        var lista = new List<TreinamentoDetectado>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            using var j = JsonDocument.Parse(conteudo);
            if (j.RootElement.TryGetProperty("itens", out var itens) && itens.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in itens.EnumerateArray())
                {
                    var titulo = Str(i, "titulo").Trim().TrimEnd('.');
                    if (titulo.Length < 3) continue;
                    if (lista.Any(x => Normalizar(x.Titulo) == Normalizar(titulo))) continue;
                    lista.Add(new TreinamentoDetectado
                    {
                        Titulo = titulo,
                        Modulo = ClienteTreinamentoModulos.Normalizar(Str(i, "modulo")),
                        Descricao = Str(i, "descricao").Trim()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Log("[TREINAMENTO] Resposta da IA inválida: " + ex.Message);
        }
        return lista.Take(10).ToList();
    }

    /// <summary>
    /// Itens treinados que ainda não têm tutorial: procura o tutorial mais parecido e grava no item.
    /// Usado antes de montar a mensagem para o cliente. Devolve quantos foram ligados.
    /// </summary>
    public async Task<int> LigarTutoriaisFaltantesAsync(int codEmp, IEnumerable<ClienteTreinamentoItem> itens)
    {
        int ligados = 0;
        foreach (var item in itens.Where(i => i.Treinado && string.IsNullOrWhiteSpace(i.SlugDoc)).Take(25))
        {
            await LigarTutorialAsync(item, codEmp);
            if (string.IsNullOrWhiteSpace(item.SlugDoc)) continue;

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
UPDATE dbo.ClienteTreinamento
   SET CodDoc = @doc, CodEmpDoc = @empdoc, SlugDoc = @slug, TituloDoc = @tdoc
 WHERE Codigo = @c AND ISNULL(SlugDoc, '') = '';", conn);
            cmd.Parameters.AddWithValue("@doc", (object?)item.CodDoc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@empdoc", (object?)item.CodEmpDoc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@slug", item.SlugDoc!);
            cmd.Parameters.AddWithValue("@tdoc", (object?)item.TituloDoc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", item.Codigo);
            await cmd.ExecuteNonQueryAsync();
            ligados++;
        }
        return ligados;
    }

    /// <summary>Procura um tutorial parecido com o item (mesma busca da IA) e liga ao item.</summary>
    private async Task LigarTutorialAsync(ClienteTreinamentoItem item, int codEmp)
    {
        try
        {
            var consulta = item.Titulo + ". " + item.Descricao;
            var vec = await _openAi.GetEmbeddingAsync(consulta);
            if (vec.Length == 0) return;
            var melhor = _index.SearchDocs(vec, consulta, 3, codEmp).FirstOrDefault();
            float min = float.TryParse(_config["Treinamento:MinScoreTutorial"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.42f;
            if (melhor.Item == null || melhor.Score < min) return;
            item.CodDoc = melhor.Item.CodDoc;
            item.SlugDoc = melhor.Item.Slug;
            item.TituloDoc = melhor.Item.Titulo;
            item.CodEmpDoc = melhor.Item.CodEmp > 0 ? melhor.Item.CodEmp : null;
        }
        catch (Exception ex)
        {
            LogHelper.Log("[TREINAMENTO] Ligar tutorial: " + ex.Message);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Para o Assistente IA e para o aviso no ticket
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Texto curto com o que o cliente já aprendeu (vazio se nada / sem tabela).</summary>
    public async Task<string> ResumoParaIAAsync(int codEmp, int codCliente)
    {
        if (codCliente <= 0) return "";
        try
        {
            var itens = (await ListarAsync(codEmp, codCliente, garantirIniciais: false))
                .Where(i => i.Treinado).Take(40).ToList();
            if (itens.Count == 0) return "";
            return string.Join("\n", itens.Select(i =>
                $"- {i.Titulo} ({(i.UltimaData ?? i.DataTreinamento):dd/MM/yyyy}{(i.Vezes > 1 ? $", orientado {i.Vezes}x" : "")})"));
        }
        catch
        {
            return "";
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Mensagem para o cliente (vai para a resposta do ticket)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Resumo do que já foi treinado, no formato do WhatsApp (negrito com *).</summary>
    public string MontarMensagemResumo(string? nomeCliente, IEnumerable<ClienteTreinamentoItem> itens, bool incluirTutoriais)
    {
        var baseUrl = (_config["App:BaseUrl"] ?? "").TrimEnd('/');
        var feitos = itens.Where(i => i.Treinado).ToList();
        var iniciais = feitos.Where(i => i.Inicial).ToList();
        var orientacoes = feitos.Where(i => !i.Inicial)
            .OrderBy(i => Array.FindIndex(ClienteTreinamentoModulos.Lista, m => m.Codigo == i.Modulo))
            .ThenByDescending(i => i.UltimaData ?? i.DataTreinamento)
            .ToList();

        string Linha(ClienteTreinamentoItem i)
        {
            var data = (i.UltimaData ?? i.DataTreinamento)?.ToString("dd/MM");
            var l = "✅ " + i.Titulo + (data != null ? $" ({data})" : "");
            if (incluirTutoriais && i.UrlTutorial != null && baseUrl.Length > 0)
                l += "\n    Tutorial: " + baseUrl + i.UrlTutorial;
            return l;
        }

        var sb = new StringBuilder();
        var nome = (nomeCliente ?? "").Trim();
        sb.AppendLine(nome.Length > 0 ? $"Olá, {nome}! Segue o resumo do que já vimos juntos no sistema Evolut:" :
                                        "Olá! Segue o resumo do que já vimos juntos no sistema Evolut:");
        if (iniciais.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("*Treinamento inicial*");
            foreach (var i in iniciais) sb.AppendLine(Linha(i));
        }
        if (orientacoes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("*Orientações*");
            foreach (var i in orientacoes) sb.AppendLine(Linha(i));
        }
        sb.AppendLine();
        sb.Append("Qualquer dúvida sobre algum desses assuntos, é só chamar!");
        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Utilitários
    // ═════════════════════════════════════════════════════════════════════

    private const string Colunas = @"Codigo, CodEmp, CodCliente, Titulo, Modulo, Descricao, Status, Inicial, Origem, CodTicketChamadoC,
       DataTreinamento, UltimaData, Vezes, Atendente, PessoaTreinada, Observacao, CodDoc, CodEmpDoc, SlugDoc, TituloDoc, IncluidoPorIA,
       CodTreinamentoTipo";

    private static ClienteTreinamentoItem Ler(SqlDataReader rd) => new()
    {
        Codigo = rd.GetInt32(0),
        CodEmp = rd.GetInt32(1),
        CodCliente = rd.GetInt32(2),
        Titulo = rd.GetString(3),
        Modulo = rd.GetString(4),
        Descricao = rd.IsDBNull(5) ? null : rd.GetString(5),
        Status = rd.GetString(6),
        Inicial = rd.GetBoolean(7),
        Origem = rd.GetString(8),
        CodTicketChamadoC = rd.IsDBNull(9) ? null : rd.GetInt32(9),
        DataTreinamento = rd.IsDBNull(10) ? null : rd.GetDateTime(10),
        UltimaData = rd.IsDBNull(11) ? null : rd.GetDateTime(11),
        Vezes = rd.GetInt32(12),
        Atendente = rd.IsDBNull(13) ? null : rd.GetString(13),
        PessoaTreinada = rd.IsDBNull(14) ? null : rd.GetString(14),
        Observacao = rd.IsDBNull(15) ? null : rd.GetString(15),
        CodDoc = rd.IsDBNull(16) ? null : rd.GetInt32(16),
        CodEmpDoc = rd.IsDBNull(17) ? null : rd.GetInt32(17),
        SlugDoc = rd.IsDBNull(18) ? null : rd.GetString(18),
        TituloDoc = rd.IsDBNull(19) ? null : rd.GetString(19),
        IncluidoPorIA = rd.GetBoolean(20),
        CodTreinamentoTipo = rd.IsDBNull(21) ? null : rd.GetInt32(21)
    };

    private static void Parametros(SqlCommand c, ClienteTreinamentoItem i, string usuario)
    {
        static object Db(object? v) => v ?? DBNull.Value;
        c.Parameters.AddWithValue("@emp", i.CodEmp);
        c.Parameters.AddWithValue("@cli", i.CodCliente);
        c.Parameters.AddWithValue("@t", Cortar(i.Titulo.Trim(), 200)!);
        c.Parameters.AddWithValue("@m", i.Modulo);
        c.Parameters.AddWithValue("@d", Db(Vazio(Cortar(i.Descricao, 1000))));
        c.Parameters.AddWithValue("@s", i.Status);
        c.Parameters.AddWithValue("@ini", i.Inicial);
        c.Parameters.AddWithValue("@o", string.IsNullOrWhiteSpace(i.Origem) ? "manual" : i.Origem);
        c.Parameters.AddWithValue("@tk", Db(i.CodTicketChamadoC > 0 ? i.CodTicketChamadoC : null));
        c.Parameters.Add("@dt", SqlDbType.DateTime).Value = Db(i.DataTreinamento);
        c.Parameters.Add("@ud", SqlDbType.DateTime).Value = Db(i.UltimaData);
        c.Parameters.AddWithValue("@v", i.Vezes);
        c.Parameters.AddWithValue("@at", Db(Vazio(Cortar(i.Atendente, 100))));
        c.Parameters.AddWithValue("@pt", Db(Vazio(Cortar(i.PessoaTreinada, 100))));
        c.Parameters.AddWithValue("@obs", Db(Vazio(Cortar(i.Observacao, 500))));
        c.Parameters.AddWithValue("@doc", Db(i.CodDoc > 0 ? i.CodDoc : null));
        c.Parameters.AddWithValue("@empdoc", Db(i.CodEmpDoc > 0 ? i.CodEmpDoc : null));
        c.Parameters.AddWithValue("@slug", Db(Vazio(Cortar(i.SlugDoc, 200))));
        c.Parameters.AddWithValue("@tdoc", Db(Vazio(Cortar(i.TituloDoc, 200))));
        c.Parameters.AddWithValue("@ia", i.IncluidoPorIA);
        c.Parameters.AddWithValue("@u", Cortar(usuario, 100) ?? "");
    }

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Cortar(string? s, int max)
        => s == null ? null : (s.Length <= max ? s : s[..max]);

    private static string Str(JsonElement e, string nome)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "") : "";

    /// <summary>Para comparar títulos: sem acento, minúsculo, sem pontuação.</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var semAcento = new string(s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(Regex.Replace(semAcento, @"[^a-z0-9 ]", " "), @"\s+", " ").Trim();
    }
}

/// <summary>Sugestão do campo "o que foi treinado" (tutorial ou item já usado).</summary>
public sealed record TreinamentoSugestao(string Titulo, string? Modulo, int? CodDoc, string? SlugDoc, int? CodEmpDoc);
