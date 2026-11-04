using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services
{
    public class DashboardService
    {
        private readonly string _connection;
        private readonly UserState _userState;

        public DashboardService(IConfiguration config, UserState userState)
        {
            _connection = config.GetConnectionString("Connection");
            _userState = userState;
        }

        private int CodEmpAtual
        {
            get
            {
                if (_userState.CurrentCompanyId <= 0)
                    throw new InvalidOperationException("Empresa do usuário não carregada no UserState.");

                return _userState.CurrentCompanyId;
            }
        }
        private void AddEmpresaParam(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@Empresa"))
                cmd.Parameters.AddWithValue("@Empresa", CodEmpAtual);
        }
        // SUBSTITUIR o método inteiro por:
        public async Task<DashboardData> GetDashboardDataAsync()
        {
            var data = new DashboardData();

            using (var conn = new SqlConnection(_connection))
            {
                await conn.OpenAsync();
                data.KPIs = await GetKPIsAsync(conn);
            }

            await Task.WhenAll(
                Task.Run(async () => data.LeadsPorTemperatura = await GetLeadsPorTemperaturaAsync()),
                Task.Run(async () => data.LeadsPorStatus = await GetLeadsPorStatusAsync()),
                Task.Run(async () => data.EvolucaoMensal = await GetEvolucaoMensalAsync()),
                Task.Run(async () => data.TopUsuarios = await GetTopUsuariosAsync()),
                Task.Run(async () => data.UltimasAtividades = await GetUltimasAtividadesAsync())
            );

            return data;
        }

        private async Task<KPIData> GetKPIsAsync(SqlConnection conn)
        {
            var kpi = new KPIData();
            var empresa = CodEmpAtual;

            var sql = @"
                -- Total de leads
                SELECT COUNT(*) FROM CRMC WHERE CodEmp = @Empresa;
                
                -- Leads abertos
                SELECT COUNT(*) FROM CRMC 
                WHERE CodEmp = @Empresa 
                AND ISNULL(Status, 'ABERTO') NOT IN ('FECHADO', 'PERDIDO', 'CONCLUIDO', 'CANCELADO');
                
                -- Leads fechados
                SELECT COUNT(*) FROM CRMC 
                WHERE CodEmp = @Empresa 
                AND Status IN ('FECHADO', 'CONCLUIDO');
                
                -- Leads perdidos
                SELECT COUNT(*) FROM CRMC 
                WHERE CodEmp = @Empresa 
                AND Status IN ('PERDIDO', 'CANCELADO');
                
                -- Leads este mês
                SELECT COUNT(*) FROM CRMC 
                WHERE CodEmp = @Empresa 
                AND MONTH(DataCriacao) = MONTH(GETDATE()) 
                AND YEAR(DataCriacao) = YEAR(GETDATE());
                
                -- Leads mês anterior
                SELECT COUNT(*) FROM CRMC 
                WHERE CodEmp = @Empresa 
                AND MONTH(DataCriacao) = MONTH(DATEADD(MONTH, -1, GETDATE())) 
                AND YEAR(DataCriacao) = YEAR(DATEADD(MONTH, -1, GETDATE()));
            ";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
                kpi.TotalLeads = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.LeadsAbertos = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.LeadsFechados = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.LeadsPerdidos = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.LeadsEsteMe = reader.GetInt32(0);

            int leadsMesAnterior = 0;
            if (await reader.NextResultAsync() && await reader.ReadAsync())
                leadsMesAnterior = reader.GetInt32(0);

            // Cálculos derivados
            // Cálculos derivados
            if (kpi.TotalLeads > 0)
            {
                kpi.TaxaConversao = Math.Round(
                    (decimal)kpi.LeadsFechados / kpi.TotalLeads * 100,
                    2
                );
            }
            else
            {
                kpi.TaxaConversao = 0;
            }

            if (leadsMesAnterior > 0)
                kpi.CrescimentoMensal = Math.Round(((decimal)kpi.LeadsEsteMe - leadsMesAnterior) / leadsMesAnterior * 100, 2);

            return kpi;
        }

        private async Task<List<LeadPorTemperatura>> GetLeadsPorTemperaturaAsync()
        {
            var lista = new List<LeadPorTemperatura>();
            var empresa = CodEmpAtual;
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    ISNULL(FaixaLead, 'SEM CLASSIFICAÇÃO') as Temperatura,
                    COUNT(*) as Quantidade
                FROM CRMC
                WHERE CodEmp = @Empresa
                GROUP BY FaixaLead
                ORDER BY 
                    CASE FaixaLead
                        WHEN 'QUENTE' THEN 1
                        WHEN 'MORNO' THEN 2
                        WHEN 'FRIO' THEN 3
                        WHEN 'MUITO FRIO' THEN 4
                        ELSE 5
                    END
            ";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            int total = 0;
            var tempList = new List<(string temp, int qtd)>();

            while (await reader.ReadAsync())
            {
                var temp = reader.GetString(0).Trim().ToUpper();
                var qtd = reader.GetInt32(1);
                tempList.Add((temp, qtd));
                total += qtd;
            }

            foreach (var (temp, qtd) in tempList)
            {
                var percentual = total > 0 ? Math.Round((decimal)qtd / total * 100, 2) : 0;
                var cor = temp switch
                {
                    "QUENTE" => "#f44336",
                    "MORNO" => "#ff9800",
                    "FRIO" => "#03a9f4",
                    "MUITO FRIO" => "#5c6bc0",
                    _ => "#9e9e9e"
                };

                lista.Add(new LeadPorTemperatura
                {
                    Temperatura = temp,
                    Quantidade = qtd,
                    Percentual = percentual,
                    Cor = cor
                });
            }

            return lista;
        }

        private async Task<List<LeadPorStatus>> GetLeadsPorStatusAsync()
        {
            var lista = new List<LeadPorStatus>();
            var empresa = CodEmpAtual;
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
                SELECT 
                    ISNULL(Status, 'SEM STATUS') as Status,
                    COUNT(*) as Quantidade
                FROM CRMC
                WHERE CodEmp = @Empresa
                GROUP BY Status
                ORDER BY Quantidade DESC
            ";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            int total = 0;
            var tempList = new List<(string status, int qtd)>();

            while (await reader.ReadAsync())
            {
                var status = reader.GetString(0);
                var qtd = reader.GetInt32(1);
                tempList.Add((status, qtd));
                total += qtd;
            }

            foreach (var (status, qtd) in tempList)
            {
                var percentual = total > 0 ? Math.Round((decimal)qtd / total * 100, 2) : 0;

                lista.Add(new LeadPorStatus
                {
                    Status = status,
                    Quantidade = qtd,
                    Percentual = percentual
                });
            }
            return lista;
        }

        private async Task<List<EvolucaoMensal>> GetEvolucaoMensalAsync()
        {
            var lista = new List<EvolucaoMensal>();
            var empresa = CodEmpAtual;
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
WITH MesesBase AS (
    SELECT 
        FORMAT(C.DataCriacao, 'MM/yyyy') AS Mes,
        YEAR(C.DataCriacao) AS Ano,
        MONTH(C.DataCriacao) AS NumMes,
        COUNT(*) AS LeadsCriados,
        SUM(CASE 
            WHEN ISNULL(C.Status, 'ABERTO') = 'ABERTO'
            THEN 1 ELSE 0 
        END) AS LeadsEmAberto
    FROM CRMC C
    WHERE C.CodEmp = @Empresa
      AND C.DataCriacao IS NOT NULL
      AND C.DataCriacao >= DATEADD(MONTH, -12, GETDATE())
    GROUP BY 
        FORMAT(C.DataCriacao, 'MM/yyyy'),
        YEAR(C.DataCriacao),
        MONTH(C.DataCriacao)
),
FinalizadosMes AS (
    SELECT
        YEAR(C.DataFinalizacao) AS Ano,
        MONTH(C.DataFinalizacao) AS NumMes,
        SUM(CASE WHEN C.Status = 'CONCLUIDO' THEN 1 ELSE 0 END) AS LeadsFechados,
        SUM(CASE WHEN C.Status = 'PERDIDO' THEN 1 ELSE 0 END) AS LeadsPerdidos
    FROM CRMC C
    WHERE C.CodEmp = @Empresa
      AND C.DataFinalizacao IS NOT NULL
      AND C.DataFinalizacao >= DATEADD(MONTH, -12, GETDATE())
      AND C.Status IN ('CONCLUIDO', 'PERDIDO')
    GROUP BY 
        YEAR(C.DataFinalizacao),
        MONTH(C.DataFinalizacao)
)
SELECT 
    M.Mes,
    M.LeadsCriados,
    ISNULL(F.LeadsFechados, 0) AS LeadsFechados,
    ISNULL(F.LeadsPerdidos, 0) AS LeadsPerdidos,
    M.LeadsEmAberto,

    CASE 
    WHEN M.LeadsCriados > 0
    THEN CAST(ISNULL(F.LeadsFechados, 0) AS DECIMAL(10,2)) 
         / M.LeadsCriados * 100
    ELSE 0
END AS TaxaConversao,

CASE 
    WHEN (ISNULL(F.LeadsFechados, 0) + ISNULL(F.LeadsPerdidos, 0)) >= 5
    THEN CAST(ISNULL(F.LeadsPerdidos, 0) AS DECIMAL(10,2)) 
         / (ISNULL(F.LeadsFechados, 0) + ISNULL(F.LeadsPerdidos, 0)) * 100
    ELSE 0
END AS TaxaPerda

FROM MesesBase M
LEFT JOIN FinalizadosMes F 
    ON F.Ano = M.Ano 
   AND F.NumMes = M.NumMes
ORDER BY M.Ano, M.NumMes;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new EvolucaoMensal
                {
                    Mes = reader.GetString(0),
                    LeadsCriados = reader.GetInt32(1),
                    LeadsFechados = reader.GetInt32(2),
                    LeadsPerdidos = reader.GetInt32(3),
                    LeadsEmAberto = reader.GetInt32(4),
                    TaxaConversao = Math.Round(reader.GetDecimal(5), 2),
                    TaxaPerda = Math.Round(reader.GetDecimal(6), 2)
                });
            }

            return lista;
        }

        private async Task<List<TopUsuarios>> GetTopUsuariosAsync()
        {
            var lista = new List<TopUsuarios>();
            var empresa = CodEmpAtual;
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
                SELECT TOP 10
                    UsuarioCard as Usuario,
                    COUNT(*) as LeadsAtivos,
                    SUM(CASE WHEN Status IN ('FECHADO', 'CONCLUIDO') THEN 1 ELSE 0 END) as LeadsFechados
                FROM CRMC
                WHERE CodEmp = @Empresa
                AND UsuarioCard IS NOT NULL
                AND UsuarioCard <> ''
                GROUP BY UsuarioCard
                ORDER BY LeadsFechados DESC, LeadsAtivos DESC
            ";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var leadsAtivos = reader.GetInt32(1);
                var leadsFechados = reader.GetInt32(2);
                var taxaConversao = leadsAtivos > 0 ? Math.Round((decimal)leadsFechados / leadsAtivos * 100, 2) : 0;

                lista.Add(new TopUsuarios
                {
                    Usuario = reader.GetString(0),
                    LeadsAtivos = leadsAtivos,
                    LeadsFechados = leadsFechados,
                    TaxaConversao = taxaConversao
                });
            }

            return lista;
        }

        private async Task<List<UltimaAtividade>> GetUltimasAtividadesAsync()
        {
            var lista = new List<UltimaAtividade>();
            var empresa = CodEmpAtual;
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
                SELECT TOP 15
                    Codigo,
                    Descricao,
                    NomeCliente,
                    UsuarioCard,
                    Status,
                    DataHoraUltimaGravacao,
                    Funil,
                    FaixaLead
                FROM CRMC
                WHERE CodEmp = @Empresa
                ORDER BY DataHoraUltimaGravacao DESC
            ";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new UltimaAtividade
                {
                    CodigoCard = reader.GetInt32(0),
                    Descricao = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    NomeCliente = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Usuario = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Acao = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    DataHora = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5),
                    Funil = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    FaixaLead = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
            return lista;
        }

        // SUBSTITUIR o método inteiro por:
        public async Task<SuporteDashboardData> GetSuporteDashboardDataAsync(
            DateTime dataInicial, DateTime dataFinal)
        {
            var data = new SuporteDashboardData();
            var inicio = dataInicial.Date;
            var fimEx = dataFinal.Date.AddDays(1);

            data.KPIs = await GetSuporteKPIsAsync(inicio, fimEx);

            await Task.WhenAll(
                Task.Run(async () => data.TicketsPorSituacao = await GetTicketsPorSituacaoAsync(inicio, fimEx)),
                Task.Run(async () => data.TicketsPorUsuario = await GetTicketsPorUsuarioAsync(inicio, fimEx)),
                Task.Run(async () => data.TicketsPorDiaUsuario = await GetTicketsPorDiaUsuarioInternoAsync(
                    DateTime.Today, DateTime.Today.AddDays(1)))
            );

            return data;
        }

        private async Task<SuporteKPIData> GetSuporteKPIsAsync(DateTime dataInicial, DateTime dataFinalExclusiva)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();
            var kpi = new SuporteKPIData();

            var sql = @"
SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND ISNULL(CodSituacao, 1) <> 3
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND ISNULL(CodSituacao, 1) = 2
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND ISNULL(CodSituacao, 1) = 5
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND ISNULL(CodSituacao, 1) = 3
  AND ISNULL(DataHoraUltimaGravacao, DataHoraAbertura) >= @DataInicial
  AND ISNULL(DataHoraUltimaGravacao, DataHoraAbertura) < @DataFinalExclusiva
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND DataHoraAbertura >= @DataInicial
AND DataHoraAbertura < @DataFinalExclusiva
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT COUNT(*) 
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND DataHoraAbertura >= @DataInicial
  AND DataHoraAbertura < @DataFinalExclusiva
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;

SELECT
ISNULL(
    AVG(
        CAST(
            DATEDIFF(
                MINUTE,
                DataHoraAbertura,
                CASE
                    WHEN ISNULL(CodSituacao,1) = 3
                        THEN ISNULL(DataHoraUltimaGravacao, GETDATE())
                    ELSE GETDATE()
                END
            ) AS DECIMAL(18,2)
        )
    ),
0)
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND DataHoraAbertura IS NOT NULL
  AND DataHoraAbertura >= @DataInicial
AND DataHoraAbertura < @DataFinalExclusiva
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DataInicial", dataInicial);
            cmd.Parameters.AddWithValue("@DataFinalExclusiva", dataFinalExclusiva);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
                kpi.TotalAbertos = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.EmAndamento = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.PendenteCliente = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.FinalizadosMes = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.AbertosMes = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
                kpi.TicketsHoje = reader.GetInt32(0);

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                kpi.TempoMedioAbertoMinutos = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                kpi.TempoMedioAbertoTexto = FormatarTempoResposta(kpi.TempoMedioAbertoMinutos);
            }

            return kpi;
        }

        private async Task<List<TicketPorSituacao>> GetTicketsPorSituacaoAsync(DateTime dataInicial, DateTime dataFinalExclusiva)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();
            var lista = new List<TicketPorSituacao>();

            var sql = @"
SELECT 
    CASE ISNULL(CodSituacao, 1)
        WHEN 1 THEN 'Suporte'
        WHEN 2 THEN 'Desenvolvimento'
        WHEN 3 THEN 'Finalizado'
        WHEN 4 THEN 'Atualizar'
        WHEN 5 THEN 'Pendente Cliente'
        WHEN 6 THEN 'Aprovação'
        ELSE 'Outros'
    END AS Situacao,
    COUNT(*) AS Quantidade
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1
AND DataHoraAbertura >= @DataInicial
AND DataHoraAbertura < @DataFinalExclusiva
GROUP BY ISNULL(CodSituacao, 1)
ORDER BY Quantidade DESC;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DataInicial", dataInicial);
            cmd.Parameters.AddWithValue("@DataFinalExclusiva", dataFinalExclusiva);
            using var reader = await cmd.ExecuteReaderAsync();

            var temp = new List<(string Situacao, int Quantidade)>();
            var total = 0;

            while (await reader.ReadAsync())
            {
                var situacao = reader.GetString(0);
                var quantidade = reader.GetInt32(1);

                temp.Add((situacao, quantidade));
                total += quantidade;
            }

            foreach (var item in temp)
            {
                lista.Add(new TicketPorSituacao
                {
                    Situacao = item.Situacao,
                    Quantidade = item.Quantidade,
                    Percentual = total > 0
                        ? Math.Round((decimal)item.Quantidade / total * 100, 2)
                        : 0
                });
            }

            return lista;
        }

        private async Task<List<TicketPorUsuario>> GetTicketsPorUsuarioAsync(DateTime dataInicial, DateTime dataFinalExclusiva)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();
            var lista = new List<TicketPorUsuario>();

            var sql = @"
WITH AbertosAgora AS (
    SELECT
        ISNULL(NULLIF(Usuario, ''), 'SEM RESPONSÁVEL') AS Usuario,
        COUNT(*) AS Quantidade
    FROM TicketChamadoC
    WHERE CodEmp = @Empresa
      AND ISNULL(CodSituacao, 1) NOT IN (3)
      AND ISNULL(ChatInterno, 'N') <> 'S'
      AND ISNULL(CodSetor,    0)   = 1
    GROUP BY ISNULL(NULLIF(Usuario, ''), 'SEM RESPONSÁVEL')
)
SELECT TOP 10
    ISNULL(NULLIF(T.Usuario, ''), 'SEM RESPONSÁVEL')               AS Usuario,
    SUM(CASE WHEN ISNULL(T.CodSituacao,1) <> 3 THEN 1 ELSE 0 END) AS Abertos,
    SUM(CASE
        WHEN ISNULL(T.CodSituacao,1) = 3
         AND ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura) >= @DataInicial
         AND ISNULL(T.DataHoraUltimaGravacao, T.DataHoraAbertura)  < @DataFinalExclusiva
        THEN 1 ELSE 0 END)                                          AS Finalizados,
    COUNT(*)                                                        AS Total,
    0                                                               AS QtdRespostasComTempo,
    0                                                               AS TempoMedioRespostaMinutos,
    ISNULL(A.Quantidade, 0)                                         AS AbertosAgora
FROM TicketChamadoC T
INNER JOIN Usuario U
    ON U.Usuario = T.Usuario
   AND U.CodEmp = T.CodEmp
LEFT JOIN AbertosAgora A
    ON A.Usuario = ISNULL(NULLIF(T.Usuario,''),'SEM RESPONSÁVEL')
WHERE T.CodEmp = @Empresa
  AND ISNULL(T.ChatInterno,'N') <> 'S'
  AND ISNULL(T.CodSetor,   0)   = 1
  AND ISNULL(U.Inativo,   'N')  = 'N'
  AND T.DataHoraAbertura >= @DataInicial
  AND T.DataHoraAbertura  < @DataFinalExclusiva
GROUP BY
    ISNULL(NULLIF(T.Usuario,''),'SEM RESPONSÁVEL'),
    A.Quantidade
ORDER BY Abertos DESC, Finalizados DESC;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DataInicial", dataInicial);
            cmd.Parameters.AddWithValue("@DataFinalExclusiva", dataFinalExclusiva);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var minutos = reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetValue(5));

                lista.Add(new TicketPorUsuario
                {
                    Usuario = reader.GetString(0),
                    Abertos = reader.GetInt32(1),
                    Finalizados = reader.GetInt32(2),
                    Total = reader.GetInt32(3),
                    QtdRespostasComTempo = reader.GetInt32(4),
                    TempoMedioRespostaMinutos = minutos,
                    TempoMedioRespostaTexto = FormatarTempoResposta(minutos),
                    AbertosAgora = reader.GetInt32(6)
                });
            }

            return lista;
        }

        private async Task<List<TicketEvolucaoMensal>> GetTicketsEvolucaoMensalAsync(SqlConnection conn)
        {
            var lista = new List<TicketEvolucaoMensal>();

            var sql = @"
WITH Meses AS (
    SELECT 
        FORMAT(DataHoraAbertura, 'MM/yyyy') AS Mes,
        YEAR(DataHoraAbertura) AS Ano,
        MONTH(DataHoraAbertura) AS NumMes,
        COUNT(*) AS Abertos
    FROM TicketChamadoC
    WHERE CodEmp = @Empresa
      AND DataHoraAbertura >= DATEADD(MONTH, -12, GETDATE())
      AND ISNULL(ChatInterno, 'N') <> 'S'
      AND ISNULL(CodSetor, 0) = 1
    GROUP BY 
        FORMAT(DataHoraAbertura, 'MM/yyyy'),
        YEAR(DataHoraAbertura),
        MONTH(DataHoraAbertura)
),
Finalizados AS (
    SELECT 
        YEAR(ISNULL(DataHoraUltimaGravacao, DataHoraAbertura)) AS Ano,
        MONTH(ISNULL(DataHoraUltimaGravacao, DataHoraAbertura)) AS NumMes,
        COUNT(*) AS Finalizados
    FROM TicketChamadoC
    WHERE CodEmp = @Empresa
      AND ISNULL(CodSituacao, 1) = 3
      AND ISNULL(DataHoraUltimaGravacao, DataHoraAbertura) >= DATEADD(MONTH, -12, GETDATE())
      AND ISNULL(ChatInterno, 'N') <> 'S'
      AND ISNULL(CodSetor, 0) = 1
    GROUP BY 
        YEAR(ISNULL(DataHoraUltimaGravacao, DataHoraAbertura)),
        MONTH(ISNULL(DataHoraUltimaGravacao, DataHoraAbertura))
)
SELECT 
    M.Mes,
    M.Abertos,
    ISNULL(F.Finalizados, 0) AS Finalizados
FROM Meses M
LEFT JOIN Finalizados F
    ON F.Ano = M.Ano
   AND F.NumMes = M.NumMes
ORDER BY M.Ano, M.NumMes;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TicketEvolucaoMensal
                {
                    Mes = reader.GetString(0),
                    Abertos = reader.GetInt32(1),
                    Finalizados = reader.GetInt32(2)
                });
            }

            return lista;
        }

        private static string FormatarTempoResposta(decimal minutos)
        {
            if (minutos <= 0)
                return "Sem resposta";

            var totalMinutos = (int)Math.Round(minutos);
            var dias = totalMinutos / 1440;
            var horas = (totalMinutos % 1440) / 60;
            var mins = totalMinutos % 60;

            if (dias > 0)
                return $"{dias}d {horas}h";

            if (horas > 0)
                return $"{horas}h {mins}min";

            return $"{mins}min";
        }

        public async Task<List<TicketsPorDiaUsuario>> GetTicketsPorDiaUsuarioAsync(DateTime dataInicial, DateTime dataFinal)
        {
            return await GetTicketsPorDiaUsuarioInternoAsync(
                dataInicial.Date,
                dataFinal.Date.AddDays(1)
            );
        }

        private async Task<List<TicketsPorDiaUsuario>> GetTicketsPorDiaUsuarioInternoAsync(DateTime dataInicial, DateTime dataFinalExclusiva)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();
            var lista = new List<TicketsPorDiaUsuario>();

            var sql = @"
SELECT
    CAST(DataHoraAbertura AS DATE) AS Data,
    ISNULL(NULLIF(Usuario, ''), 'SEM RESPONSÁVEL') AS Usuario,
    COUNT(*) AS Quantidade
FROM TicketChamadoC
WHERE CodEmp = @Empresa
  AND DataHoraAbertura IS NOT NULL
  AND DataHoraAbertura >= @DataInicial
  AND DataHoraAbertura < @DataFinalExclusiva
  AND ISNULL(ChatInterno, 'N') <> 'S'
  AND ISNULL(CodSetor, 0) = 1
GROUP BY
    CAST(DataHoraAbertura AS DATE),
    ISNULL(NULLIF(Usuario, ''), 'SEM RESPONSÁVEL')
ORDER BY
    Data DESC,
    Quantidade DESC;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DataInicial", dataInicial);
            cmd.Parameters.AddWithValue("@DataFinalExclusiva", dataFinalExclusiva);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TicketsPorDiaUsuario
                {
                    Data = reader.GetDateTime(0),
                    Usuario = reader.GetString(1),
                    Quantidade = reader.GetInt32(2)
                });
            }

            return lista;
        }

        public async Task<List<TicketDetalheUsuarioDia>> GetTicketsDetalheUsuarioDiaAsync(string usuario, DateTime data)
        {
            var lista = new List<TicketDetalheUsuarioDia>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var inicio = data.Date;
            var fim = data.Date.AddDays(1);

            var sql = @"
SELECT
    T.Codigo,
    ISNULL(T.Assunto, '') AS Assunto,
    ISNULL(C.Nome, '') AS Cliente,
    ISNULL(NULLIF(T.Usuario, ''), 'SEM RESPONSÁVEL') AS Usuario,
    CASE ISNULL(T.CodSituacao, 1)
        WHEN 1 THEN 'Suporte'
        WHEN 2 THEN 'Desenvolvimento'
        WHEN 3 THEN 'Finalizado'
        WHEN 4 THEN 'Atualizar'
        WHEN 5 THEN 'Pendente Cliente'
        WHEN 6 THEN 'Aprovação'
        ELSE 'Outros'
    END AS Situacao,
    T.DataHoraAbertura
FROM TicketChamadoC T
LEFT JOIN Cliente C
    ON C.Codigo = T.CodCliente
   AND C.CodEmp = T.CodEmp
WHERE T.CodEmp = @Empresa
  AND T.DataHoraAbertura >= @Inicio
  AND T.DataHoraAbertura < @Fim
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
  AND ISNULL(T.CodSetor, 0) = 1
  AND ISNULL(NULLIF(T.Usuario, ''), 'SEM RESPONSÁVEL') = @Usuario
ORDER BY T.DataHoraAbertura DESC;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.Parameters.AddWithValue("@Inicio", inicio);
            cmd.Parameters.AddWithValue("@Fim", fim);
            cmd.Parameters.AddWithValue("@Usuario", usuario);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new TicketDetalheUsuarioDia
                {
                    Codigo = reader.GetInt32(0),
                    Assunto = reader.GetString(1),
                    Cliente = reader.GetString(2),
                    Usuario = reader.GetString(3),
                    Situacao = reader.GetString(4),
                    DataHoraAbertura = reader.GetDateTime(5)
                });
            }

            return lista;
        }

        // === NOVO: total de vendas por mês/ano (espelha BuscarTotalMesGrafico do painel WPF) ===
        public async Task<Dictionary<(int ano, int mes), double>> GetTotaisVendaAnosAsync(int anoAtual)
        {
            var result = new Dictionary<(int, int), double>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
        SELECT 
            YEAR(DataMovimento)  AS Ano,
            MONTH(DataMovimento) AS Mes,
            ISNULL(SUM(TotalVenda), 0) AS Total
        FROM VendaC
        WHERE CodEmp = @Empresa
          AND YEAR(DataMovimento) IN (@AnoAtual, @AnoAnterior)
        GROUP BY YEAR(DataMovimento), MONTH(DataMovimento)";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.Parameters.AddWithValue("@AnoAtual", anoAtual);
            cmd.Parameters.AddWithValue("@AnoAnterior", anoAtual - 1);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int ano = reader.GetInt32(0);
                int mes = reader.GetInt32(1);
                double total = Convert.ToDouble(reader.GetValue(2));
                result[(ano, mes)] = total;
            }

            return result;
        }

        // === NOVO: metas globais do painel ===
        // Guardadas numa linha dedicada de UsuarioPermissaoCRM (Modulo='PAINEL', Permissao='META').
        // Defaults iguais ao painel WPF: mensal 10%, anual 100%.
        public async Task<(decimal mensal, decimal anual)> GetMetasAsync()
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
                SELECT TOP 1
                    ISNULL(MetaCrescimentoMensal, 10),
                    ISNULL(MetaCrescimentoAnual, 100)
                FROM UsuarioPermissaoCRM
                WHERE CodEmp = @Empresa
                  AND Modulo = 'PAINEL' AND Permissao = 'META'
                ORDER BY Codigo ASC;";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                decimal mensal = reader.IsDBNull(0) ? 10m : reader.GetDecimal(0);
                decimal anual = reader.IsDBNull(1) ? 100m : reader.GetDecimal(1);
                return (mensal, anual);
            }

            // Linha de config ainda não existe -> defaults do WPF
            return (10m, 100m);
        }

        public async Task SalvarMetasAsync(decimal metaMensal, decimal metaAnual)
        {
            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            // UPSERT: atualiza a linha de config; se não existir, cria.
            var sql = @"
                UPDATE UsuarioPermissaoCRM
                SET MetaCrescimentoMensal  = @mensal,
                    MetaCrescimentoAnual   = @anual,
                    UsuarioUltimaGravacao  = @usuario,
                    DataHoraUltimaGravacao = GETDATE()
                WHERE CodEmp = @Empresa AND Modulo = 'PAINEL' AND Permissao = 'META';

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO UsuarioPermissaoCRM
                        (CodEmp, CodUsuario, Modulo, Permissao, Ativo,
                         MetaCrescimentoMensal, MetaCrescimentoAnual,
                         UsuarioUltimaGravacao, DataHoraUltimaGravacao)
                    VALUES
                        (@Empresa, 0, 'PAINEL', 'META', 'S',
                         @mensal, @anual, @usuario, GETDATE());
                END";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.Parameters.AddWithValue("@mensal", metaMensal);
            cmd.Parameters.AddWithValue("@anual", metaAnual);
            cmd.Parameters.AddWithValue("@usuario",
                string.IsNullOrEmpty(_userState.CurrentUser)
                    ? (object)DBNull.Value
                    : _userState.CurrentUser);

            await cmd.ExecuteNonQueryAsync();
        }


        public async Task<PesquisaSatisfacaoData> GetPesquisaSatisfacaoAsync(
    DateTime dataInicial, DateTime dataFinal)
        {
            var result = new PesquisaSatisfacaoData();
            var fimEx = dataFinal.Date.AddDays(1);

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT
    COUNT(*)                                                    AS TotalEnviadas,
    SUM(CASE WHEN ISNULL(Enviado,'N') = 'S' OR ErroEnvio IS NULL THEN 1 ELSE 0 END) AS TotalEnviadas2,
    SUM(CASE WHEN ISNULL(Respondido,'N') = 'S' THEN 1 ELSE 0 END) AS TotalRespondidas,
    SUM(CASE WHEN Nota IS NOT NULL THEN 1 ELSE 0 END)           AS TotalComNota,
    SUM(CASE WHEN ErroEnvio IS NOT NULL THEN 1 ELSE 0 END)      AS TotalErro,
    ISNULL(AVG(CAST(Nota AS DECIMAL(5,2))),0)                   AS MediaNota,
    SUM(CASE WHEN Nota = 1 THEN 1 ELSE 0 END)                   AS Nota1,
    SUM(CASE WHEN Nota = 2 THEN 1 ELSE 0 END)                   AS Nota2,
    SUM(CASE WHEN Nota = 3 THEN 1 ELSE 0 END)                   AS Nota3,
    SUM(CASE WHEN Nota = 4 THEN 1 ELSE 0 END)                   AS Nota4,
    SUM(CASE WHEN Nota = 5 THEN 1 ELSE 0 END)                   AS Nota5
FROM TicketPesquisaSatisfacao
WHERE CodEmp = @Empresa
  AND DataEnvio >= @DataInicial
  AND DataEnvio  < @DataFinal;

SELECT
    P.Codigo,
    P.CodTicketChamadoC,
    P.TelefoneWhatsApp,
    P.DataEnvio,
    ISNULL(P.Respondido, 'N')   AS Respondido,
    P.DataResposta,
    P.Nota,
    ISNULL(P.Observacao, '')    AS Observacao,
    ISNULL(P.Enviado,    'N')   AS Enviado,
    ISNULL(P.ErroEnvio,  '')    AS ErroEnvio
FROM TicketPesquisaSatisfacao P
WHERE P.CodEmp = @Empresa
  AND P.DataEnvio >= @DataInicial
  AND P.DataEnvio  < @DataFinal
ORDER BY P.DataEnvio DESC;
";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@DataInicial", dataInicial.Date);
            cmd.Parameters.AddWithValue("@DataFinal", fimEx);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                result.TotalEnviadas = reader.GetInt32(0);
                result.TotalRespondidas = reader.GetInt32(2);
                result.TotalComNota = reader.GetInt32(3);
                result.TotalErro = reader.GetInt32(4);
                result.MediaNota = reader.GetDecimal(5);
                result.Nota1 = reader.GetInt32(6);
                result.Nota2 = reader.GetInt32(7);
                result.Nota3 = reader.GetInt32(8);
                result.Nota4 = reader.GetInt32(9);
                result.Nota5 = reader.GetInt32(10);
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    result.Detalhes.Add(new PesquisaDetalhe
                    {
                        Codigo = reader.GetInt32(0),
                        CodTicket = reader.GetInt32(1),
                        Telefone = reader.GetString(2),
                        DataEnvio = reader.GetDateTime(3),
                        Respondido = reader.GetString(4) == "S",
                        DataResposta = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                        Nota = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Observacao = reader.GetString(7),
                        Enviado = reader.GetString(8) == "S",
                        ErroEnvio = reader.GetString(9)
                    });
                }
            }

            return result;
        }


        public async Task<List<TicketDetalheUsuarioDia>> GetTicketsAbertosUsuarioAsync(string usuario)
        {
            var lista = new List<TicketDetalheUsuarioDia>();

            using var conn = new SqlConnection(_connection);
            await conn.OpenAsync();

            var sql = @"
SELECT
    T.Codigo,
    ISNULL(T.Assunto, '') AS Assunto,
    ISNULL(C.Nome, '') AS Cliente,
    ISNULL(NULLIF(T.Usuario, ''), 'SEM RESPONSÁVEL') AS Usuario,
    CASE ISNULL(T.CodSituacao, 1)
        WHEN 1 THEN 'Suporte'
        WHEN 2 THEN 'Desenvolvimento'
        WHEN 4 THEN 'Atualizar'
        WHEN 5 THEN 'Pendente Cliente'
        WHEN 6 THEN 'Aprovação'
        ELSE 'Outros'
    END AS Situacao,
    T.DataHoraAbertura
FROM TicketChamadoC T
LEFT JOIN Cliente C ON C.Codigo = T.CodCliente AND C.CodEmp = T.CodEmp AND C.CodEmp = T.CodEmp
WHERE T.CodEmp = @Empresa
  AND ISNULL(T.CodSituacao, 1) <> 3
  AND ISNULL(T.ChatInterno, 'N') <> 'S'
  AND ISNULL(T.CodSetor, 0) = 1
  AND ISNULL(NULLIF(T.Usuario, ''), 'SEM RESPONSÁVEL') = @Usuario
ORDER BY T.DataHoraUltimaGravacao DESC, T.Codigo DESC;";

            using var cmd = new SqlCommand(sql, conn);
            AddEmpresaParam(cmd);
            cmd.Parameters.AddWithValue("@Usuario", usuario);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new TicketDetalheUsuarioDia
                {
                    Codigo = reader.GetInt32(0),
                    Assunto = reader.GetString(1),
                    Cliente = reader.GetString(2),
                    Usuario = reader.GetString(3),
                    Situacao = reader.GetString(4),
                    DataHoraAbertura = reader.GetDateTime(5)
                });
            }

            return lista;
        }
    }
}





