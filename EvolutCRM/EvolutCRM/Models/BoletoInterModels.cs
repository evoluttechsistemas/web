namespace EvolutCRM.Models;

public sealed record BoletoInterOpcao(int Codigo, string Descricao);
public sealed record BoletoWhatsAppOpcao(int Codigo, string Nome);
public sealed record BoletoPdfArquivo(byte[] Conteudo, string Nome);
public sealed record BoletoInterResultado(bool PdfDisponivel, string Mensagem);

public sealed class BoletoInterTitulo
{
    public int Codigo { get; set; }
    public string Cliente { get; set; } = "";
    public string Telefone { get; set; } = "";
    public decimal Valor { get; set; }
    public DateTime? Vencimento { get; set; }
    public bool Pago { get; set; }
    public string CodigoSolicitacao { get; set; } = "";
    public string Estado { get; set; } = "";
    public string SituacaoBanco { get; set; } = "";
    public string LinhaDigitavel { get; set; } = "";
    public bool TemPdf { get; set; }
    public bool TemVinculo { get; set; }
    public string Erro { get; set; } = "";
    public bool PodeEmitir => !Pago && !TemVinculo && string.IsNullOrEmpty(Estado);
}

internal sealed class BoletoInterDados
{
    public int Codigo { get; set; }
    public int CodCliente { get; set; }
    public int CodEmp { get; set; }
    public int Configuracao { get; set; }
    public decimal Valor { get; set; }
    public DateTime Vencimento { get; set; }
    public bool Pago { get; set; }
    public string TipoPagamento { get; set; } = "";
    public string CodigoSolicitacao { get; set; } = "";
    public string Estado { get; set; } = "";
    public string NossoNumero { get; set; } = "";
    public string Pix { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Email { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Complemento { get; set; } = "";
    public string Bairro { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Uf { get; set; } = "";
    public string Cep { get; set; } = "";
    public string EmiteBoleto { get; set; } = "N";
}

internal sealed record BoletoInterConfig(int Codigo, string ClientId, string ClientSecret,
    string Scope, string Crt, string Key, string Instrucao, decimal Mora, decimal Multa,
    string ContaCorrente);
