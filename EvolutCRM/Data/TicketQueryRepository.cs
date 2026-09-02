using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace EvolutCRM.Data;

public sealed class TicketQueryRepository
{
    private readonly string _cs;

    public TicketQueryRepository(IConfiguration config)
    {
        _cs = config.GetConnectionString("Connection");
        if (string.IsNullOrWhiteSpace(_cs))
            throw new InvalidOperationException("ConnectionStrings:Connection não encontrada no appsettings.json");
    }

    /// <summary>
    /// Monta o texto base do ticket usando TicketChamadoC (cabeçalho) e TicketChamadoD (histórico/anotações).
    /// Ajuste nomes de colunas/tabelas se no seu banco estiver diferente.
    /// </summary>
    public async Task<string> BuildTicketBaseTextAsync(int ticketId, int maxHistoricoChars = 8000)
    {
        // 1) Cabeçalho (TicketChamadoC)
        var (assunto, status, nomeCliente) = await GetTicketHeaderAsync(ticketId);

        // 2) Histórico (TicketChamadoD) - pega últimas N mensagens
        var historico = await GetTicketHistoryTextAsync(ticketId, top: 25, maxChars: maxHistoricoChars);

        // 3) Monta texto final
        var sb = new StringBuilder();
        sb.AppendLine($"TICKET: {ticketId}");

        if (!string.IsNullOrWhiteSpace(assunto))
            sb.AppendLine($"ASSUNTO: {assunto}");

        if (!string.IsNullOrWhiteSpace(status))
            sb.AppendLine($"STATUS: {status}");

        if (!string.IsNullOrWhiteSpace(nomeCliente))
            sb.AppendLine($"CLIENTE: {nomeCliente}");

        sb.AppendLine("HISTORICO:");
        sb.AppendLine(string.IsNullOrWhiteSpace(historico) ? "(vazio)" : historico);

        return sb.ToString();
    }

    private async Task<(string assunto, string status, string nomeCliente)> GetTicketHeaderAsync(int ticketId)
    {
        const string sql = @"
SELECT TOP 1
    Assunto,
    CodSituacao,
    CodCliente
FROM TicketChamadoC
WHERE Codigo = @TicketId;";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@TicketId", SqlDbType.Int).Value = ticketId;

        await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

        if (!await rd.ReadAsync())
            throw new InvalidOperationException($"TicketChamadoC não encontrado. Codigo={ticketId}");

        string assunto = Normalize(rd["Assunto"]?.ToString());
        int codSituacao = rd["CodSituacao"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CodSituacao"]);
        int codCliente = rd["CodCliente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CodCliente"]);

        // Converte o status num texto amigável (igual seu componente faz)
        string status = codSituacao switch
        {
            1 => "Aberto",
            2 => "Em andamento",
            3 => "Finalizado",
            4 => "Atualizar",
            5 => "Pendente Cliente",
            _ => $"Status {codSituacao}"
        };

        // Por enquanto não vamos buscar nome do cliente (você já mostra na tela)
        string nomeCliente = $"CodCliente {codCliente}";

        assunto = Trunc(assunto, 180);
        status = Trunc(status, 30);
        nomeCliente = Trunc(nomeCliente, 120);

        return (assunto, status, nomeCliente);
    }

    private async Task<string> GetTicketHistoryTextAsync(int ticketId, int top, int maxChars)
    {
        // Pega as últimas anotações do TicketChamadoD
        const string sql = @"
SELECT *
FROM (
    SELECT TOP (@Top)
        DataHora,
        Usuario,
        Anotacao
    FROM TicketChamadoD
    WHERE CodTicketChamadoC = @TicketId
    ORDER BY DataHora DESC
) X
ORDER BY DataHora ASC;";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
        cmd.Parameters.Add("@TicketId", SqlDbType.Int).Value = ticketId;

        var sb = new StringBuilder();
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var dataHora = rd["DataHora"]?.ToString() ?? "";
            var usuario = Normalize(rd["Usuario"]?.ToString());
            var anotacao = Normalize(rd["Anotacao"]?.ToString());

            if (string.IsNullOrWhiteSpace(anotacao)) continue;

            // Formato curto e legível
            sb.AppendLine($"- [{dataHora}] {usuario}: {anotacao}");

            if (sb.Length >= maxChars)
                break;
        }

        var text = sb.ToString().Trim();
        if (text.Length > maxChars) text = text.Substring(0, maxChars);

        return text;
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private static string Trunc(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}