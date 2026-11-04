using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace EvolutTech.IA.Painel.Services;

public sealed record PanelAccount(int Id,string Name,string PasswordHash,string LegacyPassword,
    bool Locked,string PasswordVersion,int CompanyId=2,bool Active=true,bool Help=true);
public interface IPanelAccountStore
{
    Task<PanelAccount?> FindAsync(string username,CancellationToken token);
    Task<PanelAccount?> FindByIdAsync(int id,CancellationToken token);
}
public sealed class SqlPanelAccountStore(SecretStore secrets):IPanelAccountStore
{
    // Existing HELP schema. SELECT only; never migrate hashes or update online/login status.
    public const string Query="""
        SELECT TOP (2) Codigo,LTRIM(RTRIM(Usuario)),SenhaHash,Senha,
            CASE WHEN BloqueadoAte>GETDATE() THEN 1 ELSE 0 END,
            ISNULL(CONVERT(varchar(33),UltimaAlteracaoSenha,126),'')
        FROM dbo.Usuario
        WHERE CodEmp=@CompanyId AND ISNULL(Inativo,'N')='N' AND ISNULL(Help,'N')='S'
          AND ((@Id IS NOT NULL AND Codigo=@Id) OR
               (@Id IS NULL AND UPPER(LTRIM(RTRIM(Usuario)))=UPPER(@Username)))
        ORDER BY Codigo;
        """;
    public Task<PanelAccount?> FindAsync(string username,CancellationToken token)=>ReadAsync(username,null,token);
    public Task<PanelAccount?> FindByIdAsync(int id,CancellationToken token)=>ReadAsync("",id,token);
    private async Task<PanelAccount?> ReadAsync(string username,int? id,CancellationToken token)
    {
        var connectionString=await secrets.GetSqlAsync();
        if(string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Conexão SQL não configurada.");
        await using var connection=new SqlConnection(connectionString);await connection.OpenAsync(token);
        using var command=new SqlCommand(Query,connection) {CommandTimeout=15};
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=2;
        command.Parameters.Add("@Id",SqlDbType.Int).Value=(object?)id??DBNull.Value;
        command.Parameters.Add("@Username",SqlDbType.NVarChar,50).Value=username;
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)) return null;
        string Text(int i)=>reader.IsDBNull(i)?"":reader.GetString(i).TrimEnd();
        // Preserve the exact legacy password, including spaces.
        var account=new PanelAccount(reader.GetInt32(0),Text(1),Text(2),reader.IsDBNull(3)?"":reader.GetString(3),reader.GetInt32(4)==1,Text(5));
        return await reader.ReadAsync(token)?null:account; // Ambiguous usernames must never select an arbitrary user.
    }
}
public sealed record PanelLoginResult(PanelAccount? Account,string Error)
{
    public bool Success=>Account is not null&&Error.Length==0;
}
public sealed class PanelLoginService(IPanelAccountStore accounts,ILogger<PanelLoginService> logger)
{
    public static readonly TimeSpan SessionLifetime=TimeSpan.FromHours(8);
    public async Task<PanelLoginResult> VerifyPasswordAsync(string username,string password,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(username)||username.Trim().Length>50||string.IsNullOrEmpty(password)||password.Length>256)
            return new(null,"invalid");
        try
        {
            var account=await accounts.FindAsync(username.Trim().ToUpperInvariant(),token);
            if(account is null||!BaseEligible(account)||!PasswordMatches(account,password)) return new(null,"invalid");
            if(account.Locked) return new(null,"blocked");
            return new(account,"");
        }
        catch(Exception ex) when(IsStorageFailure(ex))
        {logger.LogWarning("Consulta de login indisponível: {Type}.",ex.GetType().Name);return new(null,"unavailable");}
    }
    public static bool BaseEligible(PanelAccount account)=>account.Id>0&&account.CompanyId==2&&account.Active&&account.Help;
    public static bool PasswordMatches(PanelAccount account,string password)
    {
        if(account.PasswordHash.Length>0)
        {
            try {return BCrypt.Net.BCrypt.Verify(password,account.PasswordHash);}
            catch(Exception ex) when(ex is BCrypt.Net.SaltParseException or ArgumentException or FormatException) {return false;}
        }
        return account.LegacyPassword.Length>0&&CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(password)),SHA256.HashData(Encoding.UTF8.GetBytes(account.LegacyPassword)));
    }
    public static ClaimsPrincipal Principal(PanelAccount account,DateTimeOffset now)=>new(new ClaimsIdentity([
        new(ClaimTypes.NameIdentifier,account.Id.ToString(CultureInfo.InvariantCulture)),new(ClaimTypes.Name,account.Name),
        new("company_id","2"),new("password_version",account.PasswordVersion),
        new("session_expires",now.Add(SessionLifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
    ],Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme));
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal,CancellationToken token)
    {
        if(principal.Identity?.IsAuthenticated!=true||principal.FindFirstValue("company_id")!="2"||
           !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier),out var id)||
           !long.TryParse(principal.FindFirstValue("session_expires"),out var expires)||expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        try
        {
            var account=await accounts.FindByIdAsync(id,token);
            return account is not null&&BaseEligible(account)&&!account.Locked&&
                account.PasswordVersion==principal.FindFirstValue("password_version");
        }
        catch(Exception ex) when(IsStorageFailure(ex))
        {logger.LogWarning("Revalidação de acesso indisponível: {Type}.",ex.GetType().Name);return false;}
    }
    private static bool IsStorageFailure(Exception ex)=>ex is SqlException or IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException;
}
