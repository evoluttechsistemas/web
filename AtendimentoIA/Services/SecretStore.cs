using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public sealed class SecretStore(PanelStore store, IDataProtectionProvider provider,IConfiguration? configuration=null)
{
    private readonly IDataProtector sql = provider.CreateProtector("SqlConnection.v1");
    private readonly IDataProtector github = provider.CreateProtector("GitHubToken.v1");
    private readonly IDataProtector openAi = provider.CreateProtector("OpenAIKey.v1");
    public bool HasSql => File.Exists(Path.Combine(store.Root, "secrets", "sql.protected"))||!string.IsNullOrWhiteSpace(configuration?.GetConnectionString("Connection"));
    public bool HasGitHub => File.Exists(Path.Combine(store.Root, "secrets", "github.protected"));
    public bool HasOpenAi => File.Exists(Path.Combine(store.Root, "secrets", "openai.protected"));
    public async Task SaveSqlAsync(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog) ||
            !string.IsNullOrEmpty(builder.AttachDBFilename)) throw new ArgumentException("Conexão SQL inválida.");
        builder.PersistSecurityInfo = false;
        builder.ApplicationName = "EvolutTech IA - consulta de catalogos";
        builder.ConnectTimeout = 15;
        builder.Encrypt = true;
        await SaveAsync("sql.protected", sql.Protect(builder.ConnectionString));
    }
    public async Task<string?> GetSqlAsync() => await GetAsync("sql.protected", sql)??configuration?.GetConnectionString("Connection");
    public async Task SaveGitHubAsync(string token)
    {
        if (token.Length is < 10 or > 1000 || token.Any(char.IsControl)) throw new ArgumentException("Token inválido.");
        await SaveAsync("github.protected", github.Protect(token));
    }
    public async Task<string?> GetGitHubAsync() => await GetAsync("github.protected", github);
    public async Task SaveOpenAiAsync(string key)
    {
        if (key.Length is < 20 or > 1000 || key.Any(char.IsWhiteSpace)) throw new ArgumentException("Chave inválida.");
        await SaveAsync("openai.protected", openAi.Protect(key));
    }
    public async Task<string?> GetOpenAiAsync() => await GetAsync("openai.protected", openAi);
    private async Task SaveAsync(string file, string encrypted)
    {
        var directory = Path.Combine(store.Root, "secrets");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, file);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, encrypted);
        File.Move(temporary, destination, overwrite: true);
    }
    private async Task<string?> GetAsync(string file, IDataProtector protector)
    {
        var path = Path.Combine(store.Root, "secrets", file);
        return File.Exists(path) ? protector.Unprotect(await File.ReadAllTextAsync(path)) : null;
    }
}
