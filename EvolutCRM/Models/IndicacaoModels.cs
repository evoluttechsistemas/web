namespace EvolutCRM.Models;

public static class IndicacaoStatus
{
    public const string Recebida = "Recebida";
    public const string EmContato = "EmContato";
    public const string Confirmada = "Confirmada";   // virou cliente → gera desconto
    public const string NaoFechou = "NaoFechou";

    public static readonly string[] Todos = [Recebida, EmContato, Confirmada, NaoFechou];

    /// <summary>Texto mostrado para o cliente e na tela interna.</summary>
    public static string Rotulo(string? status) => status switch
    {
        EmContato => "Em contato",
        Confirmada => "Virou cliente",
        NaoFechou => "Não fechou",
        _ => "Recebida"
    };
}

public sealed class IndicacaoModel
{
    public int Codigo { get; set; }
    public int CodEmp { get; set; }
    public int CodCliente { get; set; }
    public string NomeCliente { get; set; } = "";     // quem indicou
    public bool ClienteMensalista { get; set; }        // Cliente.ClienteMensalista = 'S' → a indicação dá desconto
    public string NomeContato { get; set; } = "";
    public string NomeFantasia { get; set; } = "";
    public string WhatsApp { get; set; } = "";
    public string Segmento { get; set; } = "";
    public string Status { get; set; } = IndicacaoStatus.Recebida;
    public string Observacao { get; set; } = "";
    public string Origem { get; set; } = "Cliente";
    public DateTime DataCriacao { get; set; }
    public DateTime? DataStatus { get; set; }
    public string UsuarioStatus { get; set; } = "";

    public string WhatsAppFormatado => IndicacaoFormato.Telefone(WhatsApp);
}

public sealed class IndicacaoNovaRequest
{
    public string NomeContato { get; set; } = "";
    public string NomeFantasia { get; set; } = "";
    public string WhatsApp { get; set; } = "";
    public string Segmento { get; set; } = "";
}

/// <summary>Cliente identificado pelo link pessoal.</summary>
public sealed class IndicacaoAcessoModel
{
    public int CodEmp { get; set; }
    public int CodCliente { get; set; }
    public string NomeCliente { get; set; } = "";
    public string Documento { get; set; } = "";       // CNPJ/CPF (Cliente.Cpf), só números
    public string Token { get; set; } = "";

    public string DocumentoFormatado => IndicacaoFormato.Documento(Documento);
}

public sealed class IndicacaoMensalidadeModel
{
    public const decimal DescontoPorIndicacao = 10m;

    /// <summary>Valor cheio do plano (Cliente.ValorMensalidade).</summary>
    public decimal? ValorMensalidade { get; set; }

    /// <summary>Título atual em ContaReceber.Valor (pode já vir com desconto de indicações anteriores).</summary>
    public decimal? ValorTitulo { get; set; }
    public DateTime? Vencimento { get; set; }
    public bool EmAberto { get; set; }

    /// <summary>Indicações com status Confirmada na tabela ClienteIndicacao.</summary>
    public int QtdConfirmadasRegistradas { get; set; }

    /// <summary>Cliente.ClienteMensalista = 'S'. Se 'N', as indicações aprovadas não descontam.</summary>
    public bool Mensalista { get; set; }

    /// <summary>Desconto das indicações aprovadas: R$ 10 por indicação (só mensalista).</summary>
    public decimal Desconto => Mensalista ? QtdConfirmadasRegistradas * DescontoPorIndicacao : 0m;

    /// <summary>
    /// Quanto ele paga: o título atual em ContaReceber, que já vem com o desconto aplicado.
    /// Sem título, cai para Cliente.ValorMensalidade menos o desconto.
    /// </summary>
    public decimal? ValorComDesconto
    {
        get
        {
            if (ValorTitulo is > 0) return ValorTitulo;
            if (ValorMensalidade is not > 0) return null;
            return Math.Max(0, ValorMensalidade.Value - Desconto);
        }
    }

    /// <summary>
    /// Valor cheio (o riscado): o que ele paga + R$ 10 por indicação aprovada.
    /// Ex.: título 137 com 1 aprovada → 147; título 127 com 2 aprovadas → 147.
    /// </summary>
    public decimal? Valor => ValorComDesconto is null ? null : ValorComDesconto.Value + Desconto;

    /// <summary>Indicações aprovadas (status Confirmada).</summary>
    public int QtdConfirmadas => QtdConfirmadasRegistradas;

    /// <summary>Quantas indicações aprovadas ainda faltam para zerar o que ele paga.</summary>
    public int FaltamParaZerar => !Mensalista || ValorComDesconto is null or <= 0
        ? 0
        : (int)Math.Ceiling(ValorComDesconto.Value / DescontoPorIndicacao);

    /// <summary>0–100: quanto do valor cheio já foi coberto pelas indicações.</summary>
    public int PercentualCoberto => Valor is null or <= 0 ? 0 : (int)Math.Round(Desconto / Valor.Value * 100m);
}

public sealed class IndicacaoResumoModel
{
    public int Total { get; set; }
    public int Recebidas { get; set; }
    public int EmContato { get; set; }
    public int Confirmadas { get; set; }
    public int NaoFecharam { get; set; }
    /// <summary>Confirmadas de clientes mensalistas (as que realmente geram desconto).</summary>
    public int ConfirmadasValendo { get; set; }
    public decimal DescontoTotal => ConfirmadasValendo * IndicacaoMensalidadeModel.DescontoPorIndicacao;
}

public sealed class IndicacaoClienteBuscaModel
{
    public int Codigo { get; set; }
    public string Nome { get; set; } = "";
    public string Apelido { get; set; } = "";
    public string Documento { get; set; } = "";
    public string Exibicao => string.IsNullOrWhiteSpace(Apelido) ? Nome : $"{Apelido} — {Nome}";
    public string DocumentoFormatado => IndicacaoFormato.Documento(Documento);
}

public static class IndicacaoFormato
{
    // Mesmos nomes dos cartões de segmento da página do cliente
    public static readonly string[] Segmentos =
    [
        "Supermercados", "Padarias", "Açougues", "Hortifrutis", "Depósitos de Bebidas"
    ];

    public static string SomenteNumeros(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Normaliza para 55 + DDD + número. Retorna "" se inválido.</summary>
    public static string NormalizarWhatsApp(string? valor)
    {
        var n = SomenteNumeros(valor);
        if (n.StartsWith("0")) n = n.TrimStart('0');
        if (n.Length is 10 or 11) n = "55" + n;
        return n.Length is 12 or 13 && n.StartsWith("55") ? n : "";
    }

    public static string Telefone(string? numero)
    {
        var n = SomenteNumeros(numero);
        if (n.StartsWith("55") && n.Length is 12 or 13) n = n[2..];
        return n.Length switch
        {
            11 => $"({n[..2]}) {n.Substring(2, 5)}-{n[7..]}",
            10 => $"({n[..2]}) {n.Substring(2, 4)}-{n[6..]}",
            _ => numero ?? ""
        };
    }

    /// <summary>Formata CNPJ (14) ou CPF (11).</summary>
    public static string Documento(string? valor)
    {
        var n = SomenteNumeros(valor);
        return n.Length switch
        {
            14 => $"{n[..2]}.{n.Substring(2, 3)}.{n.Substring(5, 3)}/{n.Substring(8, 4)}-{n[12..]}",
            11 => $"{n[..3]}.{n.Substring(3, 3)}.{n.Substring(6, 3)}-{n[9..]}",
            _ => valor ?? ""
        };
    }

    public static string Moeda(decimal? valor)
        => valor is null ? "—" : valor.Value.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
}
