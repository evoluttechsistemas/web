namespace EvolutDelivery.Helpers
{
    /// <summary>
    /// Mascara dados pessoais exibidos em telas públicas (checkout, pedido confirmado, meus pedidos).
    /// </summary>
    public static class PrivacidadeHelper
    {
        private const string EnderecoGenerico = "Endereço cadastrado";

        /// <summary>"Rua das Flores" -> "Rua das Flores, nº ***"</summary>
        public static string MascararEndereco(string? rua)
        {
            if (string.IsNullOrWhiteSpace(rua))
                return EnderecoGenerico;

            return $"{rua.Trim()}, nº ***";
        }

        /// <summary>
        /// Recebe o endereço completo no formato "Rua, Número - Compl - Bairro, Cidade"
        /// e devolve só a rua: "Rua das Flores, nº ***".
        /// </summary>
        public static string MascararEnderecoCompleto(string? enderecoCompleto)
        {
            if (string.IsNullOrWhiteSpace(enderecoCompleto))
                return EnderecoGenerico;

            int virgula = enderecoCompleto.IndexOf(',');
            if (virgula <= 0)
                return EnderecoGenerico;

            return MascararEndereco(enderecoCompleto[..virgula]);
        }

        /// <summary>"(11) 98765-4321" -> "(**) *****-4321"</summary>
        public static string MascararTelefone(string? telefone)
        {
            var digitos = new string((telefone ?? "").Where(char.IsDigit).ToArray());

            if (digitos.Length < 4)
                return "****";

            return $"(**) *****-{digitos[^4..]}";
        }
    }
}
