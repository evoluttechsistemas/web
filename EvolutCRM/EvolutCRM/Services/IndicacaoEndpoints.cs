using System.Security.Cryptography;
using System.Text;

namespace EvolutCRM.Services;

/// <summary>
/// Entrada do botão "Indique e ganhe" do sistema desktop (Windows Forms):
///   GET /indicacao/abrir?cnpj=12345678000190&amp;assinatura=HMAC
/// Confere a assinatura do CNPJ, acha o cliente (Cliente.Cpf) e redireciona para a página dele.
/// A assinatura impede que alguém troque o CNPJ no endereço e veja a mensalidade de outra empresa.
/// </summary>
public static class IndicacaoEndpoints
{
    public static void MapIndicacaoEndpoints(this WebApplication app)
    {
        app.MapGet("/indicacao/abrir", async (
            string? cnpj, string? assinatura, int? emp,
            IndicacaoService service, IConfiguration config) =>
        {
            var chave = config["Indicacao:Chave"] ?? "";
            var codEmp = emp ?? config.GetValue("Indicacao:CodEmp", 2);
            var doc = new string((cnpj ?? "").Where(char.IsDigit).ToArray());

            if (chave.Length < 16 || doc.Length is not (11 or 14) || !AssinaturaValida(doc, assinatura, chave))
                return Results.Redirect("/indicacao/invalido");

            var acesso = await service.ObterTokenPorDocumentoAsync(codEmp, doc);
            return acesso is null
                ? Results.Redirect("/indicacao/invalido")
                : Results.Redirect($"/indicacao/{acesso.Value.Token}");
        });
    }

    /// <summary>HMAC-SHA256 do CNPJ (só números) com a chave, em hexadecimal minúsculo.</summary>
    public static string Assinar(string documento, string chave)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(chave));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(documento))).ToLowerInvariant();
    }

    private static bool AssinaturaValida(string documento, string? assinatura, string chave)
    {
        if (string.IsNullOrWhiteSpace(assinatura)) return false;
        var esperada = Encoding.ASCII.GetBytes(Assinar(documento, chave));
        var recebida = Encoding.ASCII.GetBytes(assinatura.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(esperada, recebida);
    }
}
