// Services/MenuCRMService.cs
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EvolutCRM.Models;
using EvolutCRM.Services;

namespace EvolutTech.CRM.Services;

public class MenuCRMService
{
    private readonly string _connection;
    private readonly UserState _state;

    public MenuCRMService(IConfiguration config, UserState state)
    {
        _connection = config.GetConnectionString("Connection") ?? "";
        _state = state;
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

    public async Task<List<UserItem>> LoadUsers()
    {
        const string sql = @"
SELECT Codigo, Usuario
FROM Usuario
WHERE CodEmp = @CodEmp
  AND ISNULL(Inativo,'N') = 'N'
  AND ISNULL(Help,'N') = 'S'
ORDER BY Usuario";

        using var con = new SqlConnection(_connection);
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

        var dt = new DataTable();
        using var da = new SqlDataAdapter(cmd);
        da.Fill(dt);

        return dt.AsEnumerable()
                 .Select(r => new UserItem
                 {
                     Codigo = r.Field<int>("Codigo"),
                     Username = r.Field<string>("Usuario") ?? ""
                 })
                 .ToList();
    }

    public async Task<StageSet> LoadStages()
    {
        const string sql = @"
SELECT Codigo, Descricao
FROM CRMEtapas
WHERE CodEmp = @CodEmp
ORDER BY Codigo";

        var result = new StageSet();

        using var con = new SqlConnection(_connection);
        await con.OpenAsync();

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var cod = rd["Codigo"]?.ToString();
            var desc = rd["Descricao"]?.ToString() ?? "-";
            switch (cod)
            {
                case "1": result.Stage1 = desc; break;
                case "2": result.Stage2 = desc; break;
                case "3": result.Stage3 = desc; break;
                case "4": result.Stage4 = desc; break;
                case "5": result.Stage5 = desc; break;
            }
        }

        return result;
    }

    public async Task<(List<FunnelColumn> Columns, decimal TotalProducts, decimal TotalServices, decimal TotalGeneral)>
BuscaCRM(int companyId, string user)
    {
        user = (user ?? "").Trim().ToUpper();
        var columns = new List<FunnelColumn>
    {
        new("SC","Sem Contato"),
        new("EP","Em Progresso"),
        new("FU","Follow Up"),
        new("FC","Fechamento"),
        new("IM","Implantação"),
        new("NO","Negociação"),
        new("RA","Reunião Agendada"),
    };

        decimal totalProducts = 0m, totalServices = 0m;

        string sql = @"
    SELECT C.Codigo, C.Descricao, C.Funil, C.FaixaLead, ISNULL(C.Novo,'N') AS Novo,
        CONVERT(varchar,
            CASE 
                WHEN A.UltimaDataHora > C.DataHoraUltimaGravacao THEN A.UltimaDataHora
                ELSE C.DataHoraUltimaGravacao
            END,
        121) AS UltimaDataHora,
        ISNULL(C.ScoreTemperatura, 0) AS ScoreTemperatura,
        C.DataCriacao
    FROM CRMC C
    LEFT JOIN (
        SELECT CodCRMC, MAX(DataHora) AS UltimaDataHora 
        FROM CRMAnotacao
        WHERE CodEmp = @CodEmp
          AND ISNULL(MensagemExcluida, 'N') = 'N'
          AND Usuario IS NOT NULL
          AND Usuario <> ''
          AND Anotacao NOT LIKE 'WhatsApp visualizado pelo usuário%'
        GROUP BY CodCRMC
    ) A ON A.CodCRMC = C.Codigo
    WHERE ISNULL(C.Status,'ABERTO') NOT IN ('CONCLUIDO','PERDIDO','CANCELADO')
    AND C.CodEmp = @CodEmp
    AND (
        C.Funil = 'IM'
        OR @Usuario = ''
        OR UPPER(LTRIM(RTRIM(C.UsuarioCard))) = @Usuario
        OR UPPER(LTRIM(RTRIM(C.UsuarioCard))) = 'IMPORTACAO'
        OR UPPER(LTRIM(RTRIM(C.UsuarioCard))) = 'NOVO'
    )
    ORDER BY 
        CASE 
            WHEN A.UltimaDataHora > C.DataHoraUltimaGravacao THEN A.UltimaDataHora
            ELSE C.DataHoraUltimaGravacao
        END DESC";

        using var con = new SqlConnection(_connection);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);
        cmd.Parameters.AddWithValue("@Usuario", user);

        using var rd = await cmd.ExecuteReaderAsync();
        var items = new List<(string funil, FunnelItem item)>();

        while (await rd.ReadAsync())
        {
            var funil = ((rd["Funil"]?.ToString()) ?? "").Trim().ToUpperInvariant();
            var codigo = Convert.ToInt32(rd["Codigo"]);
            var descricao = rd["Descricao"]?.ToString() ?? "";
            var ultima = rd["UltimaDataHora"] == DBNull.Value
                ? ""
                : Convert.ToDateTime(rd["UltimaDataHora"]).ToString("dd/MM/yyyy HH:mm");
            var faixaLead = rd["FaixaLead"]?.ToString() ?? "";
            var novo = rd["Novo"]?.ToString() ?? "N";
            var scoreTemp = Convert.ToInt32(rd["ScoreTemperatura"]);

            items.Add((funil, new FunnelItem
            {
                Codigo = codigo,
                Descricao = descricao,
                UltimaDataHora = ultima,
                TotalLinha = 0,
                FaixaLead = faixaLead,
                Novo = novo,
                ScoreTemperatura = scoreTemp,
                DataCriacao = rd["DataCriacao"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(rd["DataCriacao"])
            }));
        }

        var agendaCounts = await GetAgendaCounts(con, companyId, user);

        foreach (var (funil, item) in items)
        {
            item.AgendaCount = agendaCounts.TryGetValue(item.Codigo, out var c) ? c : 0;

            var col = columns.FirstOrDefault(x => x.FunnelCode == funil);
            if (col != null)
            {
                col.Items.Add(item);
                col.Count++;
            }
        }

        var totalGeneral = totalProducts + totalServices;
        return (columns, totalProducts, totalServices, totalGeneral);
    }

    public async Task<(List<ImplantacaoItem> Items, int Count)> UpdateImplantacao(int companyId)
    {
        var list = new List<ImplantacaoItem>();
        string sql = @"
    SELECT C.Codigo, C.Descricao, C.Funil,
           ISNULL(CONVERT(varchar, A.UltimaDataHora, 103), '01/01/2000') AS UltimaDataHora
    FROM CRMC C
    LEFT JOIN (
        SELECT CodCRMC, MAX(DataHora) AS UltimaDataHora 
        FROM CRMAnotacao
        WHERE CodEmp = @CodEmp
          AND ISNULL(MensagemExcluida, 'N') = 'N'
          AND Usuario IS NOT NULL
          AND Usuario <> ''
          AND Anotacao NOT LIKE 'WhatsApp visualizado pelo usuário%'
        GROUP BY CodCRMC
    ) A ON A.CodCRMC = C.Codigo
    WHERE C.Status = 'ABERTO'
      AND C.CodEmp = @CodEmp
      AND C.Funil = 'IM'
    ORDER BY A.UltimaDataHora";

        using var con = new SqlConnection(_connection);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new ImplantacaoItem
            {
                Codigo = Convert.ToInt32(rd["Codigo"]),
                Descricao = rd["Descricao"]?.ToString() ?? "",
                UltimaDataHora = rd["UltimaDataHora"]?.ToString() ?? ""
            });
        }
        return (list, list.Count);
    }

    public async Task MarcarCardComoLido(int codigo, int companyId)
    {
        string sql = @"
UPDATE CRMC
SET Novo = 'N'
WHERE Codigo = @Codigo
AND CodEmp = @CodEmp";

        using var con = new SqlConnection(_connection);

        await con.OpenAsync();

        using var cmd = new SqlCommand(sql, con);

        cmd.Parameters.AddWithValue("@Codigo", codigo);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AgendaItem>> UpdateAgenda(int companyId, string user, bool showOnlyScheduled)
    {
        user = (user ?? "").Trim().ToUpper();

        var list = new List<AgendaItem>();
        string cond = showOnlyScheduled
          ? "DataAgendamento <= GETDATE() and Status='AGENDADO' and "
          : "CAST(DataAgendamento as date) = CAST(GETDATE() as date) and ";

        string sql = $@"
SELECT Codigo,
       Descricao,
       DataAgendamento,
       HoraAgendamento,
       Status,
       CodCliente AS CodCRMC
FROM Agenda
WHERE {cond} CodEmp=@CodEmp AND UsuarioUltimaGravacao=@Usuario
ORDER BY DataAgendamento, HoraAgendamento";


        using var con = new SqlConnection(_connection);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);
        cmd.Parameters.AddWithValue("@Usuario", user);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new AgendaItem
            {
                Codigo = Convert.ToInt32(rd["Codigo"]),
                Descricao = rd["Descricao"]?.ToString() ?? "",
                DataAgendamento = rd.GetDateTime(rd.GetOrdinal("DataAgendamento")),
                HoraAgendamento = rd["HoraAgendamento"]?.ToString() ?? "00:00",
                Status = rd["Status"]?.ToString() ?? "",
                CodCRMC = Convert.ToInt32(rd["CodCRMC"])
            });
        }
        return list;
    }

    public async Task<(List<CardResumo> Concluidos, List<CardResumo> Perdidos)>
    LoadConcluidosPerdidos(int companyId, string user)
    {
        user = (user ?? "").Trim().ToUpper();

        var concluidos = new List<CardResumo>();
        var perdidos = new List<CardResumo>();

        string sql = @"
SELECT Codigo, Descricao, NomeCliente,
       convert(varchar, DataPrevisaoFechamento, 103) as DataPrevisaoFechamento,
       convert(varchar, DataCriacao, 103) as DataCriacao,
       Status
FROM CRMC
WHERE UsuarioCard = @Usuario
  AND CodEmp = @CodEmp
  AND CAST(DataFinalizacao as date) = CAST(GETDATE() as date)";

        using var con = new SqlConnection(_connection);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Usuario", user);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var item = new CardResumo
            {
                Codigo = Convert.ToInt32(rd["Codigo"]),
                Descricao = rd["Descricao"]?.ToString() ?? "",
                NomeCliente = rd["NomeCliente"]?.ToString() ?? "",
                DataCriacao = rd["DataCriacao"]?.ToString() ?? "",
                DataPrevisaoFechamento = rd["DataPrevisaoFechamento"]?.ToString() ?? ""
            };

            var status = rd["Status"]?.ToString();
            if (status == "CONCLUIDO") concluidos.Add(item);
            else if (status == "PERDIDO") perdidos.Add(item);
        }
        return (concluidos, perdidos);
    }

    // ---- helpers ----
    private static async Task<Dictionary<int, int>> GetAgendaCounts(
    SqlConnection con,
    int companyId,
    string user)
    {
        user = (user ?? "").Trim().ToUpper();

        const string sql = @"
        SELECT CodCRMC, COUNT(1) AS Qt
        FROM Agenda
        WHERE Usuario=@u AND CodEmp=@CodEmp
        GROUP BY CodCRMC";

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@CodEmp", companyId);
        using var rd = await cmd.ExecuteReaderAsync();

        var dict = new Dictionary<int, int>();
        while (await rd.ReadAsync())
        {
            dict[Convert.ToInt32(rd["CodCRMC"])] = Convert.ToInt32(rd["Qt"]);
        }

        return dict;
    }

    public async Task UpdateCardFunnel(int cardId, string newFunnel)
    {
        const string sql = @"
        UPDATE CRMC
        SET Funil = @Funil
        WHERE Codigo = @Codigo
          AND CodEmp = @CodEmp";

        using var con = new SqlConnection(_connection);
        await con.OpenAsync();

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Funil", newFunnel);
        cmd.Parameters.AddWithValue("@Codigo", cardId);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

        await cmd.ExecuteNonQueryAsync();
    }
}
