namespace EvolutDelivery.Models
{
    public class Cliente
    {
        public int Codigo { get; set; }
        public int CodEmp { get; set; }

        public string Nome { get; set; } = string.Empty;
        public string Apelido { get; set; } = string.Empty;
        public string Telefone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;

        // Endereço principal vindo do ClienteAFV
        public string Rua { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Complemento { get; set; } = string.Empty;
        public string Bairro { get; set; } = string.Empty;
        public string Cidade { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string CEP { get; set; } = string.Empty;
        public string? PontoReferencia { get; set; }

        // Lista para a UI, mesmo que hoje venha só 1 endereço
        public List<Endereco> Enderecos { get; set; } = new();

        // Estatísticas para exibição
        public int TotalPedidos { get; set; }
        public decimal TotalGasto { get; set; }

        public string NomeCompleto =>
            string.IsNullOrWhiteSpace(Apelido)
                ? Nome
                : $"{Nome} ({Apelido})";

        public string EnderecoCompleto =>
            $"{Rua}, {Numero}" +
            (string.IsNullOrWhiteSpace(Complemento) ? "" : $" - {Complemento}") +
            (string.IsNullOrWhiteSpace(Bairro) ? "" : $" - {Bairro}") +
            (string.IsNullOrWhiteSpace(Cidade) ? "" : $", {Cidade}") +
            (string.IsNullOrWhiteSpace(Estado) ? "" : $" - {Estado}");
    }

    public class Endereco
    {
        public int Codigo { get; set; }
        public int CodCliente { get; set; }

        public string Descricao { get; set; } = "Endereço Principal";
        public string Rua { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Complemento { get; set; } = string.Empty;
        public string Bairro { get; set; } = string.Empty;
        public string Cidade { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string CEP { get; set; } = string.Empty;
        public string? PontoReferencia { get; set; }
        public bool EnderecoPrincipal { get; set; } = true;

        public string? Latitude { get; set; }
        public string? Longitude { get; set; }

        public string EnderecoCompleto =>
            $"{Rua}, {Numero}" +
            (string.IsNullOrWhiteSpace(Complemento) ? "" : $" - {Complemento}") +
            (string.IsNullOrWhiteSpace(Bairro) ? "" : $" - {Bairro}") +
            (string.IsNullOrWhiteSpace(Cidade) ? "" : $", {Cidade}") +
            (string.IsNullOrWhiteSpace(Estado) ? "" : $" - {Estado}");
    }
}