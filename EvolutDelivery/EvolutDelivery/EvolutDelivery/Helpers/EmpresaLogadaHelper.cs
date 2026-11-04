using System.Security.Claims;

namespace EvolutDelivery.Helpers
{
    /// <summary>
    /// Lê do cookie de login (claims) a empresa do usuário logado.
    /// Todas as páginas do admin devem usar isto, nunca CNPJ/CodEmp fixos no código.
    /// </summary>
    public static class EmpresaLogadaHelper
    {
        public static bool TryObter(ClaimsPrincipal? user, out int codEmp, out string cnpj)
        {
            codEmp = 0;
            cnpj = user?.FindFirst("CNPJ")?.Value?.Trim() ?? "";

            if (user?.Identity?.IsAuthenticated != true)
                return false;

            return int.TryParse(user.FindFirst("CodEmp")?.Value, out codEmp)
                && codEmp > 0
                && !string.IsNullOrWhiteSpace(cnpj);
        }
    }
}
