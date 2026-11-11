using System.Data;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public sealed record SupportContext(int TicketId, int ClientId, string Segment, string Subject, string Version);
public sealed record SupportNote(int Id, int TicketId, DateTime Date, string Text);
public sealed record SupportHistory(List<SupportNote> Notes, bool HasMore);

public sealed class SupportContextService(SecretStore secrets, IConfiguration configuration)
{
    public int CompanyId => configuration.GetValue<int>("Support:CompanyId", 2);
    // Schema/columns verified from sys catalog. Both sides are scoped to the company.
    public const string ContextQuery = """
        SELECT tc.Codigo,tc.CodCliente,cli.Segmento,LEFT(tc.Assunto,500)
        FROM dbo.TicketChamadoC tc
        JOIN dbo.Cliente cli ON cli.Codigo=tc.CodCliente AND cli.CodEmp=tc.CodEmp
        WHERE tc.CodEmp=@CompanyId AND tc.Codigo=@TicketId;
        SELECT DISTINCT cm.Versao
        FROM dbo.ControleMensalista cm
        JOIN dbo.TicketChamadoC tc ON tc.CodCliente=cm.CodCliente AND tc.CodEmp=cm.CodEmp
        JOIN dbo.Cliente cli ON cli.Codigo=tc.CodCliente AND cli.CodEmp=tc.CodEmp
        WHERE tc.CodEmp=@CompanyId AND tc.Codigo=@TicketId AND cm.Versao IS NOT NULL;
        """;
    public const string HistoryQuery = """
        SELECT TOP (41) d.Codigo,h.Codigo,d.DataHora,LEFT(d.Anotacao,6000)
        FROM dbo.TicketChamadoD d
        JOIN dbo.TicketChamadoC h ON h.Codigo=d.CodTicketChamadoC AND h.CodEmp=d.CodEmp
        JOIN dbo.TicketChamadoC origin ON origin.CodCliente=h.CodCliente AND origin.CodEmp=h.CodEmp
        WHERE origin.Codigo=@TicketId AND origin.CodEmp=@CompanyId AND h.CodEmp=@CompanyId
          AND (@BeforeId IS NULL OR d.Codigo<@BeforeId)
        ORDER BY d.Codigo DESC;
        """;
    public async Task<SupportContext?> GetAsync(int ticketId, CancellationToken token = default)
    {
        if (ticketId <= 0) throw new ArgumentOutOfRangeException(nameof(ticketId));
        await using var connection = await OpenAsync(token);
        await using var command = Command(ContextQuery, connection, ticketId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var context = new SupportContext(reader.GetInt32(0), reader.GetInt32(1), Text(reader,2),
            SensitiveText.Redact(Text(reader,3)), "");
        await reader.NextResultAsync(token);
        var versions = new List<string>();
        while (await reader.ReadAsync(token)) versions.Add(Text(reader,0));
        return context with { Version = ErpVersion.Latest(versions) };
    }
    // Últimas mensagens DESTE chamado (sem notas internas), só para entender o assunto antes da busca.
    public const string ConversationQuery = """
        SELECT TOP (8) ISNULL(d.EnvioCliente,'N'),LEFT(d.Anotacao,600)
        FROM dbo.TicketChamadoD d
        JOIN dbo.TicketChamadoC h ON h.Codigo=d.CodTicketChamadoC AND h.CodEmp=d.CodEmp
        WHERE h.Codigo=@TicketId AND h.CodEmp=@CompanyId AND ISNULL(d.Interno,'N')<>'S'
          AND NULLIF(LTRIM(RTRIM(CAST(d.Anotacao AS nvarchar(600)))),'') IS NOT NULL
        ORDER BY d.Codigo DESC;
        """;
    public async Task<string> GetRecentConversationAsync(int ticketId, CancellationToken token = default)
    {
        if (ticketId <= 0) throw new ArgumentOutOfRangeException(nameof(ticketId));
        await using var connection = await OpenAsync(token);
        await using var command = Command(ConversationQuery, connection, ticketId);
        await using var reader = await command.ExecuteReaderAsync(token);
        var lines = new List<string>();
        while (await reader.ReadAsync(token))
            lines.Add((Text(reader, 0) == "S" ? "Cliente: " : "Atendente: ") + SensitiveText.Redact(Text(reader, 1)));
        lines.Reverse();
        return string.Join("\n", lines);
    }
    public async Task<SupportHistory> GetHistoryAsync(int ticketId, int? beforeId = null, CancellationToken token = default)
    {
        if (ticketId <= 0) throw new ArgumentOutOfRangeException(nameof(ticketId));
        await using var connection = await OpenAsync(token);
        await using var command = Command(HistoryQuery, connection, ticketId);
        command.Parameters.Add("@BeforeId",SqlDbType.Int).Value = (object?)beforeId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<SupportNote>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),SensitiveText.Redact(Text(reader,3))));
        return new(rows.Take(40).ToList(),rows.Count > 40);
    }
    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(await secrets.GetSqlAsync() ?? throw new InvalidOperationException("Conexão não configurada."));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private SqlCommand Command(string sql, SqlConnection connection, int ticketId)
    {
        var command = new SqlCommand(sql,connection) { CommandTimeout = 30 };
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=CompanyId;
        command.Parameters.Add("@TicketId",SqlDbType.Int).Value=ticketId;
        return command;
    }
    private static string Text(SqlDataReader reader,int index) => reader.IsDBNull(index) ? "" : reader.GetValue(index).ToString() ?? "";
}
