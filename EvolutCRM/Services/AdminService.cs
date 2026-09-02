using System.Data.SqlClient;

namespace EvolutCRM.Services
{
    public class AdminService
    {
        private readonly IConfiguration _config;
        private readonly UserState? _userState;

        public AdminService(IConfiguration config)
        {
            _config = config;
        }

        public AdminService(IConfiguration config, UserState userState)
        {
            _config = config;
            _userState = userState;
        }

        private string ConnectionString =>
            _config.GetConnectionString("Connection")
            ?? _config["Connection"]
            ?? throw new Exception("String de conexão 'Connection' não encontrada.");

        private int CodEmpAtual
        {
            get
            {
                if (_userState is not null && _userState.CurrentCompanyId > 0)
                    return _userState.CurrentCompanyId;

                return 2;
            }
        }

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        public async Task<List<AdminUsuarioCrmDto>> ListarUsuariosCrmAsync()
        {
            var lista = new List<AdminUsuarioCrmDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT 
    Codigo,
    Usuario,
    ISNULL(Inativo, 'N') AS Inativo
FROM Usuario
WHERE CodEmp = @CodEmp
  AND ISNULL(Inativo, 'N') = 'N'
ORDER BY Usuario;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new AdminUsuarioCrmDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    Usuario = reader["Usuario"]?.ToString() ?? "",
                    Inativo = reader["Inativo"]?.ToString() ?? "N"
                });
            }

            return lista;
        }

        public async Task<List<AdminPermissaoUsuarioDto>> ListarPermissoesUsuarioAsync(int codUsuario)
        {
            var lista = new List<AdminPermissaoUsuarioDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT 
    Modulo,
    Permissao,
    ISNULL(Ativo, 'N') AS Ativo
FROM UsuarioPermissaoCRM
WHERE CodEmp = @CodEmp
  AND CodUsuario = @CodUsuario;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodUsuario", codUsuario);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new AdminPermissaoUsuarioDto
                {
                    Modulo = reader["Modulo"]?.ToString() ?? "",
                    Permissao = reader["Permissao"]?.ToString() ?? "",
                    Ativo = (reader["Ativo"]?.ToString() ?? "N") == "S"
                });
            }

            return lista;
        }

        public async Task SalvarPermissaoUsuarioAsync(
            int codUsuario,
            string modulo,
            string permissao,
            bool ativo,
            string usuarioLogado)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
IF EXISTS (
    SELECT 1 
    FROM UsuarioPermissaoCRM 
    WHERE CodEmp = @CodEmp
      AND CodUsuario = @CodUsuario 
      AND Modulo = @Modulo 
      AND Permissao = @Permissao
)
BEGIN
    UPDATE UsuarioPermissaoCRM
    SET 
        Ativo = @Ativo,
        UsuarioUltimaGravacao = @UsuarioUltimaGravacao,
        DataHoraUltimaGravacao = GETDATE()
    WHERE CodEmp = @CodEmp
      AND CodUsuario = @CodUsuario
      AND Modulo = @Modulo
      AND Permissao = @Permissao;
END
ELSE
BEGIN
    INSERT INTO UsuarioPermissaoCRM
    (
        CodEmp,
        CodUsuario,
        Modulo,
        Permissao,
        Ativo,
        UsuarioUltimaGravacao,
        DataHoraUltimaGravacao
    )
    VALUES
    (
        @CodEmp,
        @CodUsuario,
        @Modulo,
        @Permissao,
        @Ativo,
        @UsuarioUltimaGravacao,
        GETDATE()
    );
END";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@CodUsuario", codUsuario);
            cmd.Parameters.AddWithValue("@Modulo", modulo);
            cmd.Parameters.AddWithValue("@Permissao", permissao);
            cmd.Parameters.AddWithValue("@Ativo", ativo ? "S" : "N");
            cmd.Parameters.AddWithValue("@UsuarioUltimaGravacao", usuarioLogado ?? "");

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<AdminClienteVersaoDto>> ListarVersoesClientesAsync()
        {
            var clientes = new Dictionary<int, AdminClienteVersaoDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    C.Codigo AS CodCliente,
    ISNULL(C.Nome, '') AS RazaoSocial,
    ISNULL(C.Apelido, '') AS NomeFantasia,
    ISNULL(C.Telefone, '') AS Telefone,
    ISNULL(CM.NomeComputador, '') AS NomeComputador,
    ISNULL(CM.[Versao], '') AS Versao,
    CM.DataHoraUltimoAcesso
FROM Cliente C
LEFT JOIN ControleMensalista CM 
    ON CM.CodCliente = C.Codigo
   AND CM.CodEmp = C.CodEmp
   AND ISNULL(CM.Ativo, 'N') = 'S'
WHERE C.CodEmp = @CodEmp
  AND ISNULL(C.ClienteMensalista, 'N') = 'S'
ORDER BY C.Apelido, CM.NomeComputador;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var codCliente = Convert.ToInt32(reader["CodCliente"]);

                if (!clientes.ContainsKey(codCliente))
                {
                    clientes[codCliente] = new AdminClienteVersaoDto
                    {
                        CodCliente = codCliente,
                        RazaoSocial = reader["RazaoSocial"]?.ToString() ?? "",
                        NomeFantasia = reader["NomeFantasia"]?.ToString() ?? "",
                        Telefone = reader["Telefone"]?.ToString() ?? "",
                        Computadores = new()
                    };
                }

                clientes[codCliente].Computadores.Add(new AdminComputadorVersaoDto
                {
                    NomeComputador = reader["NomeComputador"]?.ToString() ?? "",
                    Versao = reader["Versao"]?.ToString() ?? "",
                    DataHoraUltimoAcesso = reader["DataHoraUltimoAcesso"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["DataHoraUltimoAcesso"])
                });
            }

            return clientes.Values.ToList();
        }

        public async Task<List<AdminClienteTarefaDto>> ListarClientesSemAberturaTarefasAsync()
        {
            var lista = new List<AdminClienteTarefaDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    Codigo,
    ISNULL(Nome, '') AS Nome,
    ISNULL(Apelido, '') AS Apelido,
    DataHoraUltimaAberturaTarefas
FROM Cliente
WHERE CodEmp = @CodEmp
    AND ClienteMensalista = 'S'
    AND (
        DataHoraUltimaAberturaTarefas IS NULL
        OR DataHoraUltimaAberturaTarefas <= DATEADD(DAY, -7, GETDATE())
    )
ORDER BY
    DataHoraUltimaAberturaTarefas ASC,
    Apelido,
    Nome;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new AdminClienteTarefaDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    Nome = reader["Nome"]?.ToString() ?? "",
                    Apelido = reader["Apelido"]?.ToString() ?? "",
                    DataHoraUltimaAberturaTarefas = reader["DataHoraUltimaAberturaTarefas"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["DataHoraUltimaAberturaTarefas"])
                });
            }

            return lista;
        }

        public async Task<List<AdminComissaoDto>> ListarComissoesAsync()
        {
            var lista = new List<AdminComissaoDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
WITH PrimeiroPagamento AS
(
    SELECT
        CR.CodCliente,
        CR.DataPagamento,
        CR.ValorPago,
        CR.Observacao,
        ROW_NUMBER() OVER
        (
            PARTITION BY CR.CodCliente
            ORDER BY CR.DataPagamento ASC
        ) AS OrdemPagamento
    FROM ContaReceber CR
    WHERE CR.CodEmp = @CodEmp
        AND CR.DataPagamento IS NOT NULL
        AND ISNULL(CR.ValorPago, 0) > 0
),
ComissoesPagas AS
(
    SELECT
        C.Codigo AS CodCliente,
        ISNULL(C.Nome, '') AS Nome,
        ISNULL(C.Apelido, '') AS Apelido,
        ISNULL(C.Observacao, '') AS ObservacaoCliente,
        PP.DataPagamento,
        ISNULL(PP.ValorPago, 0) AS ValorPago,
        ISNULL(PP.Observacao, '') AS ObservacaoContaReceber,
        0 AS EstaTestando
    FROM Cliente C
    INNER JOIN PrimeiroPagamento PP
        ON PP.CodCliente = C.Codigo
    WHERE C.CodEmp = @CodEmp
),
ClientesTestando AS
(
    SELECT
        CRM.Codigo AS CodCliente,
        ISNULL(NULLIF(CRM.NomeCliente, ''), CRM.Descricao) AS Nome,
        '' AS Apelido,
        ISNULL(C.Observacao, '') AS ObservacaoCliente,
        NULL AS DataPagamento,
        0 AS ValorPago,
        '' AS ObservacaoContaReceber,
        1 AS EstaTestando
    FROM CRMC CRM
    OUTER APPLY
    (
        SELECT TOP 1
            C.Observacao
        FROM Cliente C
        WHERE C.CodEmp = @CodEmp
          AND (
            REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Telefone, ''), '+', ''), ' ', ''), '-', ''), '(', '') 
                LIKE '%' + RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(CRM.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), 9) + '%'
            OR
            REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Celular, ''), '+', ''), ' ', ''), '-', ''), '(', '') 
                LIKE '%' + RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(CRM.TelefoneWhatsApp, ''), '+', ''), ' ', ''), '-', ''), '(', ''), 9) + '%'
            OR
            REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Telefone, ''), '+', ''), ' ', ''), '-', ''), '(', '') 
                LIKE '%' + RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(CRM.Celular, ''), '+', ''), ' ', ''), '-', ''), '(', ''), 9) + '%'
            OR
            REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Celular, ''), '+', ''), ' ', ''), '-', ''), '(', '') 
                LIKE '%' + RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(CRM.Celular, ''), '+', ''), ' ', ''), '-', ''), '(', ''), 9) + '%'
          )
        ORDER BY C.Codigo
    ) C
    WHERE CRM.CodEmp = @CodEmp
      AND CRM.Status = 'ABERTO'
      AND CRM.Funil = 'IM'
)

SELECT *
FROM ComissoesPagas

UNION ALL

SELECT *
FROM ClientesTestando

ORDER BY
    EstaTestando ASC,
    DataPagamento DESC,
    Nome;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var observacaoCliente = reader["ObservacaoCliente"]?.ToString() ?? "";
                var observacaoContaReceber = reader["ObservacaoContaReceber"]?.ToString() ?? "";

                var estaTestando = Convert.ToInt32(reader["EstaTestando"]) == 1;

                var dataInstalacao = ExtrairDataInstalacao(observacaoCliente);

                DateTime? dataPagamento = reader["DataPagamento"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(reader["DataPagamento"]);

                if (!estaTestando && (dataInstalacao == null || dataPagamento == null))
                    continue;

                var dataFimTeste = dataInstalacao?.AddDays(10);

                lista.Add(new AdminComissaoDto
                {
                    CodCliente = Convert.ToInt32(reader["CodCliente"]),
                    Nome = reader["Nome"]?.ToString() ?? "",
                    Apelido = reader["Apelido"]?.ToString() ?? "",
                    Observacao = observacaoCliente,
                    ObservacaoContaReceber = observacaoContaReceber,
                    DataInstalacao = dataInstalacao,
                    DataFimTeste = dataFimTeste,
                    DataPagamento = dataPagamento,
                    EstaTestando = estaTestando
                });
            }

            return lista
                .OrderByDescending(x => x.DataPagamento)
                .ThenBy(x => x.ClienteExibicao)
                .ToList();
        }

        private DateTime? ExtrairDataInstalacao(string observacao)
        {
            if (string.IsNullOrWhiteSpace(observacao))
                return null;

            var index = observacao.LastIndexOf(':');

            if (index < 0)
                return null;

            var dataTexto = observacao.Substring(index + 1).Trim();

            if (DateTime.TryParseExact(
                    dataTexto,
                    "dd/MM/yyyy",
                    System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                    System.Globalization.DateTimeStyles.None,
                    out var data))
            {
                return data;
            }

            return null;
        }

        public async Task<bool> UsuarioTemPermissaoAsync(string usuario, string permissao)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT COUNT(1)
FROM Usuario U
INNER JOIN UsuarioPermissaoCRM P
    ON P.CodUsuario = U.Codigo
   AND P.CodEmp = U.CodEmp
WHERE U.CodEmp = @CodEmp
  AND ISNULL(U.Inativo, 'N') = 'N'
  AND UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
  AND UPPER(LTRIM(RTRIM(P.Permissao))) = UPPER(LTRIM(RTRIM(@Permissao)))
  AND ISNULL(P.Ativo, 'N') = 'S';";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@Permissao", permissao?.Trim() ?? "");

            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return result > 0;
        }

        public async Task<(decimal mensal, decimal anual)> ObterMetasPainelAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 1
    ISNULL(MetaCrescimentoMensal, 10),
    ISNULL(MetaCrescimentoAnual, 100)
FROM UsuarioPermissaoCRM
WHERE CodEmp = @CodEmp
  AND Modulo = 'PAINEL'
  AND Permissao = 'META'
ORDER BY Codigo ASC;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                decimal mensal = reader.IsDBNull(0) ? 10m : Convert.ToDecimal(reader.GetValue(0));
                decimal anual = reader.IsDBNull(1) ? 100m : Convert.ToDecimal(reader.GetValue(1));
                return (mensal, anual);
            }

            return (10m, 100m);
        }

        public async Task SalvarMetasPainelAsync(decimal metaMensal, decimal metaAnual, string usuarioLogado)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
IF EXISTS (
    SELECT 1
    FROM UsuarioPermissaoCRM
    WHERE CodEmp = @CodEmp
      AND Modulo = 'PAINEL'
      AND Permissao = 'META'
)
BEGIN
    UPDATE UsuarioPermissaoCRM
    SET MetaCrescimentoMensal  = @Mensal,
        MetaCrescimentoAnual   = @Anual,
        UsuarioUltimaGravacao  = @Usuario,
        DataHoraUltimaGravacao = GETDATE()
    WHERE CodEmp = @CodEmp
      AND Modulo = 'PAINEL'
      AND Permissao = 'META';
END
ELSE
BEGIN
    INSERT INTO UsuarioPermissaoCRM
        (CodEmp, CodUsuario, Modulo, Permissao, Ativo,
         MetaCrescimentoMensal, MetaCrescimentoAnual,
         UsuarioUltimaGravacao, DataHoraUltimaGravacao)
    VALUES
        (@CodEmp, 0, 'PAINEL', 'META', 'S',
         @Mensal, @Anual, @Usuario, GETDATE());
END";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Mensal", metaMensal);
            cmd.Parameters.AddWithValue("@Anual", metaAnual);
            cmd.Parameters.AddWithValue("@Usuario", usuarioLogado ?? "ADMIN");

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RegistrarLogRemotoAsync(
    string nomeCliente,
    string nomeComputador,
    string usuario,
    string ipOrigem,
    int codCliente)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
INSERT INTO ParametrosHelp
(
    CodEmp,
    Tipo, Sistema, Versao, Titulo, Descricao,
    Icone, TipoAlteracao, Usuario, Referencia,
    Detalhe, IpOrigem, DataHora, Ativo, Destaque
)
VALUES
(
    @CodEmp,
    'LOG_REMOTO',
    @NomeComputador,
    NULL,
    @Titulo,
    @Descricao,
    '🖥️',
    'ACESSO',
    @Usuario,
    @Referencia,
    NULL,
    @IpOrigem,
    GETDATE(),
    'S',
    'N'
);";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@NomeComputador", nomeComputador ?? "");
            cmd.Parameters.AddWithValue("@Titulo", $"Acesso Remoto — {nomeCliente}");
            cmd.Parameters.AddWithValue("@Descricao", $"Atendente {usuario} acessou o computador {nomeComputador} do cliente {nomeCliente}.");
            cmd.Parameters.AddWithValue("@Usuario", usuario ?? "");
            cmd.Parameters.AddWithValue("@Referencia", codCliente.ToString());
            cmd.Parameters.AddWithValue("@IpOrigem", ipOrigem ?? "");

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<LogRemotoDto>> ListarLogsRemotosAsync(
            string filtro = "",
            DateTime? dataInicio = null,
            DateTime? dataFim = null)
        {
            var lista = new List<LogRemotoDto>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT
    P.Codigo,
    P.Titulo,
    P.Descricao,
    P.Sistema       AS NomeComputador,
    P.Usuario       AS Atendente,
    P.Referencia    AS CodCliente,
    P.IpOrigem,
    P.DataHora
FROM ParametrosHelp P
WHERE P.CodEmp = @CodEmp
  AND P.Tipo = 'LOG_REMOTO'
  AND P.Ativo = 'S'
  AND (@Filtro = ''
       OR P.Titulo    LIKE '%' + @Filtro + '%'
       OR P.Usuario   LIKE '%' + @Filtro + '%'
       OR P.Sistema   LIKE '%' + @Filtro + '%'
       OR P.Referencia LIKE '%' + @Filtro + '%')
  AND (@DataInicio IS NULL OR P.DataHora >= @DataInicio)
  AND (@DataFim    IS NULL OR P.DataHora <  DATEADD(DAY,1,@DataFim))
ORDER BY P.DataHora DESC;";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Filtro", filtro?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@DataInicio", (object?)dataInicio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DataFim", (object?)dataFim ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new LogRemotoDto
                {
                    Codigo = Convert.ToInt32(reader["Codigo"]),
                    Titulo = reader["Titulo"]?.ToString() ?? "",
                    Descricao = reader["Descricao"]?.ToString() ?? "",
                    NomeComputador = reader["NomeComputador"]?.ToString() ?? "",
                    Atendente = reader["Atendente"]?.ToString() ?? "",
                    CodCliente = reader["CodCliente"]?.ToString() ?? "",
                    IpOrigem = reader["IpOrigem"]?.ToString() ?? "",
                    DataHora = Convert.ToDateTime(reader["DataHora"])
                });
            }

            return lista;
        }

        public async Task<HashSet<string>> GetPermissoesUsuarioAsync(string usuario)
        {
            var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var sql = @"
SELECT UPPER(LTRIM(RTRIM(P.Permissao))) AS Permissao
FROM Usuario U
INNER JOIN UsuarioPermissaoCRM P
    ON P.CodUsuario = U.Codigo
   AND P.CodEmp = U.CodEmp
WHERE U.CodEmp = @CodEmp
  AND ISNULL(U.Inativo, 'N') = 'N'
  AND UPPER(LTRIM(RTRIM(U.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
  AND ISNULL(P.Ativo, 'N') = 'S';";

            using var cmd = new SqlCommand(sql, conn);
            AddCodEmp(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario?.Trim() ?? "");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var p = reader["Permissao"]?.ToString();
                if (!string.IsNullOrWhiteSpace(p))
                    perms.Add(p);
            }

            return perms;
        }
    }
}
