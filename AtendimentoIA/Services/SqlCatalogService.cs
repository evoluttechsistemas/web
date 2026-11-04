using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public sealed record SqlObject(string Schema, string Name, string Kind);
public sealed record SqlColumn(string Schema, string Table, string Name, string Type, short MaxLength, bool Nullable);
public sealed record SqlIndex(string Schema, string Table, string Name, string Column, bool PrimaryKey, bool Unique);
public sealed record SqlRelation(string Name, string Schema, string Table, string Column, string TargetSchema, string TargetTable, string TargetColumn);
public sealed record SqlSnapshot(DateTimeOffset ReadAt, List<SqlObject> Objects, List<SqlColumn> Columns,
    List<SqlIndex> Indexes, List<SqlRelation> Relations);

public sealed class SqlCatalogService(SecretStore secrets, PanelStore store, ILogger<SqlCatalogService> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    // Fixed catalog-only SQL. No arbitrary SQL endpoint, source scripts or commercial rows.
    public const string CatalogQuery = """
        SELECT s.name, o.name, o.type_desc FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id=o.schema_id
        WHERE o.is_ms_shipped=0 AND o.type IN ('U','V','P','FN','IF','TF','TR') ORDER BY s.name,o.name;
        SELECT s.name,o.name,c.name,t.name,c.max_length,c.is_nullable FROM sys.columns c
        JOIN sys.objects o ON o.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id
        JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE o.is_ms_shipped=0 AND o.type IN ('U','V') ORDER BY s.name,o.name,c.column_id;
        SELECT s.name,t.name,i.name,c.name,i.is_primary_key,i.is_unique FROM sys.indexes i
        JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE t.is_ms_shipped=0 AND i.name IS NOT NULL ORDER BY s.name,t.name,i.name,ic.key_ordinal;
        SELECT fk.name,ps.name,pt.name,pc.name,rs.name,rt.name,rc.name FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
        JOIN sys.tables pt ON pt.object_id=fkc.parent_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
        JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.parent_column_id
        JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
        JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fkc.referenced_column_id;
        """;
    public async Task<SqlSnapshot?> GetSnapshotAsync() => await store.ReadAsync<SqlSnapshot>("sql-schema.json");
    public async Task<SqlSnapshot> ReadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var connectionString = await secrets.GetSqlAsync() ?? throw new InvalidOperationException("Configure a conexão SQL.");
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(token);
            await using var command = new SqlCommand(CatalogQuery, connection) { CommandTimeout = 60 };
            await using var reader = await command.ExecuteReaderAsync(token);
            var objects = new List<SqlObject>(); var columns = new List<SqlColumn>();
            var indexes = new List<SqlIndex>(); var relations = new List<SqlRelation>();
            while (await reader.ReadAsync(token)) objects.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token)) columns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt16(4), reader.GetBoolean(5)));
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token)) indexes.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5)));
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token)) relations.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            var snapshot = new SqlSnapshot(DateTimeOffset.UtcNow, objects, columns, indexes, relations);
            await store.WriteAsync("sql-schema.json", snapshot);
            logger.LogInformation("Catálogos SQL lidos: {Objects} objetos, {Columns} colunas.", objects.Count, columns.Count);
            return snapshot;
        }
        finally { gate.Release(); }
    }
}
