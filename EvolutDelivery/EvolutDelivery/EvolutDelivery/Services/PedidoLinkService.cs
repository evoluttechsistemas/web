using Microsoft.AspNetCore.DataProtection;

namespace EvolutDelivery.Services
{
    /// <summary>
    /// Gera e valida o token do link "pedido confirmado".
    /// O token é assinado/criptografado pelo ASP.NET Data Protection: não dá para
    /// adivinhar nem trocar o número do pedido na URL, e ele expira.
    /// </summary>
    public class PedidoLinkService
    {
        private static readonly TimeSpan Validade = TimeSpan.FromDays(7);
        private readonly ITimeLimitedDataProtector _protector;

        public PedidoLinkService(IDataProtectionProvider provider)
        {
            _protector = provider
                .CreateProtector("EvolutDelivery.PedidoConfirmado.v1")
                .ToTimeLimitedDataProtector();
        }

        public string GerarToken(int codPedido, int codEmp)
        {
            return _protector.Protect($"{codPedido}|{codEmp}", Validade);
        }

        public bool TryLerToken(string? token, out int codPedido, out int codEmp)
        {
            codPedido = 0;
            codEmp = 0;

            if (string.IsNullOrWhiteSpace(token) || token.Length > 1000)
                return false;

            try
            {
                var partes = _protector.Unprotect(token).Split('|');

                return partes.Length == 2
                    && int.TryParse(partes[0], out codPedido)
                    && int.TryParse(partes[1], out codEmp)
                    && codPedido > 0
                    && codEmp > 0;
            }
            catch
            {
                // token alterado, inválido ou expirado
                return false;
            }
        }
    }
}
