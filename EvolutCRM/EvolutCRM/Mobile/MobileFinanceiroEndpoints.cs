using EvolutCRM.Services;
using System.Globalization;
using System.Text;

namespace EvolutCRM.Mobile
{
    /// <summary>
    /// Financeiro do HELP Mobile: Baixa a Receber e Cobrança.
    /// Mesma lógica da tela BaixaReceber.razor do HELP: usa o BxClienteService (consultas, baixa, PIX)
    /// e o LogFinanceiroService (registro de baixa, PIX e mensagens de WhatsApp).
    /// Permissões: as mesmas do HELP desktop para a tela Baixa Receber (que inclui a aba Cobrança):
    ///   • ADMIN_ACESSAR              — a página confere e o grupo "Administração" do menu depende dela
    ///   • ADMIN_ADMINISTRAR_CLIENTES — o item "Baixa Receber" do menu só aparece com ela
    /// </summary>
    public static class MobileFinanceiroEndpoints
    {
        private static readonly CultureInfo Br = new("pt-BR");
        private static string Moeda(decimal v) => v.ToString("C", Br);

        private static readonly string[] FormasPagamento =
            { "Dinheiro", "Cheque à Vista", "Cheque a Prazo", "Cartão", "PIX", "Ticket" };

        public const string PermissaoAdmin = "ADMIN_ACESSAR";
        public const string PermissaoClientes = "ADMIN_ADMINISTRAR_CLIENTES";

        /// <summary>true se o usuário tem as duas permissões (uma consulta só, igual ao MainMenu do HELP).</summary>
        public static async Task<bool> PodeFinanceiroAsync(AdminService admin, string usuario)
        {
            var perms = await admin.GetPermissoesUsuarioAsync(usuario);
            return perms.Contains(PermissaoAdmin) && perms.Contains(PermissaoClientes);
        }

        public static void MapMobileFinanceiro(this RouteGroupBuilder api)
        {
            var fin = api.MapGroup("/financeiro")
                .AddEndpointFilter(async (ctx, next) =>
                {
                    var http = ctx.HttpContext;
                    var u = MobileEndpoints.Contexto(http);
                    var admin = http.RequestServices.GetRequiredService<AdminService>();
                    if (!await PodeFinanceiroAsync(admin, u.Usuario))
                        return Results.Json(new FinResultadoDto { Mensagem = "Você não tem permissão para o financeiro." }, statusCode: 403);
                    return await next(ctx);
                });

            // ───────────── Baixa a Receber ─────────────

            fin.MapGet("/clientes", async (string? busca, BxClienteService bx) =>
            {
                if (string.IsNullOrWhiteSpace(busca) || busca.Trim().Length < 2)
                    return Results.Ok(new List<FinClienteDto>());

                var lista = await bx.BuscarClientesParaBaixaAsync(busca.Trim());
                return Results.Ok(lista.Select(c => new FinClienteDto
                {
                    Codigo = c.Codigo,
                    Nome = c.NomeCliente,
                    Apelido = c.Apelido
                }).ToList());
            });

            fin.MapGet("/clientes/{cod:int}/contas", async (int cod, string? situacao, BxClienteService bx) =>
            {
                var sit = situacao is "pagos" or "todos" ? situacao : "abertos";
                var contas = await bx.ListarContasReceberClienteAsync(cod, sit);
                return Results.Ok(contas.Select(c => new FinContaDto
                {
                    Codigo = c.Codigo,
                    TipoConta = c.NomeTipoConta ?? "",
                    DataMovimento = c.DataMovimento,
                    DataVencimento = c.DataVencimento,
                    Valor = c.Valor ?? 0,
                    ValorComJuros = c.ValorComJuros,
                    DiasAtraso = c.DiasAtraso,
                    Parcela = c.Parcela ?? "",
                    TotalParcela = c.TotalParcela ?? "",
                    Observacao = c.Observacao ?? "",
                    DataPagamento = c.DataPagamento,
                    ValorPago = c.ValorPago,
                    TipoPagamento = c.TipoPagamento ?? "",
                    Aberto = c.DataPagamento == null
                }).ToList());
            });

            fin.MapGet("/contas-bancarias", async (BxClienteService bx) =>
            {
                var contas = await bx.ListarContasBancariasAsync();
                return Results.Ok(contas.Select(c => new FinContaBancariaDto
                {
                    Codigo = c.Codigo,
                    Descricao = c.ExibicaoCombo
                }).ToList());
            });

            fin.MapPost("/baixa", async (
                FinBaixaRequest req, HttpContext http,
                BxClienteService bx, LogFinanceiroService log, MobileRepository repo) =>
            {
                var u = MobileEndpoints.Contexto(http);

                if (req.Codigos == null || req.Codigos.Count == 0)
                    return Results.BadRequest(new FinResultadoDto { Mensagem = "Selecione ao menos um título em aberto." });
                if (!FormasPagamento.Contains(req.FormaPagamento))
                    return Results.BadRequest(new FinResultadoDto { Mensagem = "Forma de pagamento inválida." });
                if (req.Acrescimo < 0)
                    return Results.BadRequest(new FinResultadoDto { Mensagem = "O acréscimo não pode ser negativo." });

                // O valor vem do banco (não do celular): só títulos em aberto deste cliente
                var abertos = await bx.ListarContasReceberClienteAsync(req.CodCliente, "abertos");
                var selecionados = abertos.Where(c => req.Codigos.Contains(c.Codigo)).ToList();
                if (selecionados.Count != req.Codigos.Distinct().Count())
                    return Results.BadRequest(new FinResultadoDto { Mensagem = "Algum título selecionado já foi baixado ou não é deste cliente. Atualize a lista." });

                var valorTotal = selecionados.Sum(c => c.Valor ?? 0);
                var nomeCliente = string.IsNullOrWhiteSpace(req.NomeCliente)
                    ? selecionados.First().NomeCliente ?? ""
                    : req.NomeCliente;

                var input = new BaixaReceberInputDto
                {
                    CodigosContaReceber = selecionados.Select(c => c.Codigo).ToList(),
                    NomeCliente = nomeCliente,
                    DataPagamento = req.DataPagamento.Date,
                    DataCompensacao = req.DataCompensacao.Date,
                    ValorTotal = valorTotal,
                    Acrescimo = Math.Round(req.Acrescimo, 2),
                    FormaPagamento = req.FormaPagamento,
                    CodContaBancaria = req.CodContaBancaria,
                    NumeroCheque = req.FormaPagamento.StartsWith("Cheque") ? (req.NumeroCheque ?? "") : ""
                };

                var resultado = await bx.ExecutarBaixaReceberAsync(input, u.Usuario);
                if (resultado != "ok")
                    return Results.BadRequest(new FinResultadoDto { Mensagem = "Erro: " + resultado });

                var detalhe = $"{req.FormaPagamento} · {selecionados.Count} título(s) · " +
                              $"Valor: {Moeda(valorTotal)} · Juros: {Moeda(input.Acrescimo)} · " +
                              $"Cliente: {nomeCliente} · via HELP Mobile";
                await RegistrarLogAsync(http, log, repo, u, LogFinanceiroService.BAIXA_TITULO, detalhe);

                return Results.Ok(new FinResultadoDto { Sucesso = true, Mensagem = "Baixa realizada com sucesso!" });
            });

            // ───────────── Cobrança ─────────────

            fin.MapGet("/cobranca", async (BxClienteService bx) =>
            {
                var lista = await bx.ListarClientesCobrancaAsync();
                return Results.Ok(lista.Select(c => new FinCobrancaClienteDto
                {
                    CodCliente = c.CodCliente,
                    Nome = c.NomeCliente,
                    Apelido = c.Apelido,
                    Celular = c.Celular,
                    ValorTotal = c.ValorTotal
                }).ToList());
            });

            fin.MapGet("/cobranca/{cod:int}", async (int cod, BxClienteService bx) =>
            {
                var d = await MontarDetalheAsync(cod, bx);
                return d == null ? Results.NotFound() : Results.Ok(d);
            });

            // Registra no Log Financeiro quando o usuário abre o WhatsApp (cobrança / lembrete / com PIX)
            fin.MapPost("/cobranca/{cod:int}/log", async (
                int cod, FinLogRequest req, HttpContext http,
                BxClienteService bx, LogFinanceiroService log, MobileRepository repo) =>
            {
                var u = MobileEndpoints.Contexto(http);
                var d = await MontarDetalheAsync(cod, bx);
                if (d == null) return Results.NotFound();

                var titulos = string.Join(", ", d.Titulos.Select(t => t.Codigo));
                var (tipo, detalhe) = req.Tipo switch
                {
                    "lembrete" => (LogFinanceiroService.WHATSAPP_LEMBRETE,
                        $"Lembrete · Títulos: {titulos} · Total: {Moeda(d.TotalComJuros)} · Cliente: {d.Nome}"),
                    "pix" => (LogFinanceiroService.WHATSAPP_PIX,
                        $"WhatsApp c/ PIX · Valor: {Moeda(d.TotalComJuros)} · Cliente: {d.Nome}"),
                    _ => (LogFinanceiroService.WHATSAPP_COBRANCA,
                        $"Cobrança · Títulos: {titulos} · Total: {Moeda(d.TotalComJuros)} · Cliente: {d.Nome}")
                };

                await RegistrarLogAsync(http, log, repo, u, tipo, detalhe + " · via HELP Mobile");
                return Results.Ok();
            });

            // Gera o link PIX do Mercado Pago (mesmo fluxo do botão "Gerar PIX" do HELP)
            fin.MapPost("/cobranca/{cod:int}/pix", async (
                int cod, HttpContext http,
                BxClienteService bx, LogFinanceiroService log, MobileRepository repo) =>
            {
                var u = MobileEndpoints.Contexto(http);
                var d = await MontarDetalheAsync(cod, bx);
                if (d == null) return Results.NotFound();
                if (d.Titulos.Count == 0)
                    return Results.Ok(new FinPixDto { Mensagem = "Este cliente não tem títulos em atraso." });

                var config = await bx.ObterConfigMercadoPagoAsync();
                if (config == null || string.IsNullOrWhiteSpace(config.TokenMercadoPago))
                    return Results.Ok(new FinPixDto { Mensagem = "Configuração do Mercado Pago não encontrada. Verifique o cadastro." });

                var listaTitulos = string.Join(", ", d.Titulos.Select(t => t.Codigo));
                var resultado = await bx.GerarLinkPagamentoMercadoPagoAsync(
                    valor: d.TotalComJuros,
                    descricao: $"Mensalidade cliente: {d.Nome} Títulos: {listaTitulos}",
                    emailPagador: config.EmailMercadoPago,
                    cpfPagador: config.CPFMercadoPago,
                    nomePagador: d.Nome,
                    token: config.TokenMercadoPago);

                if (string.IsNullOrWhiteSpace(resultado) || resultado.StartsWith("ERRO:"))
                    return Results.Ok(new FinPixDto
                    {
                        Mensagem = string.IsNullOrWhiteSpace(resultado)
                            ? "O Mercado Pago não retornou o link."
                            : resultado.Replace("ERRO:", "Erro ao gerar PIX: ")
                    });

                await RegistrarLogAsync(http, log, repo, u, LogFinanceiroService.PIX_GERADO,
                    $"Títulos: {listaTitulos} · Valor: {Moeda(d.TotalComJuros)} · Cliente: {d.Nome} · via HELP Mobile");

                var mensagem = d.MensagemCobranca + $"\n\n🔗 Link para pagamento PIX:\n{resultado}";
                return Results.Ok(new FinPixDto
                {
                    Sucesso = true,
                    Link = resultado,
                    UrlWhatsApp = LinkWhatsApp(d.Celular, mensagem)
                });
            });
        }

        // ───────────── Helpers ─────────────

        /// <summary>Títulos vencidos do cliente + mensagens (mesmo texto da tela BaixaReceber do HELP).</summary>
        private static async Task<FinCobrancaDetalheDto?> MontarDetalheAsync(int cod, BxClienteService bx)
        {
            var clientes = await bx.ListarClientesCobrancaAsync();
            var cli = clientes.FirstOrDefault(c => c.CodCliente == cod);
            if (cli == null) return null;

            var titulos = await bx.ListarTitulosCobrancaClienteAsync(cod);
            var nome = !string.IsNullOrWhiteSpace(cli.Apelido) ? cli.Apelido : cli.NomeCliente;

            var d = new FinCobrancaDetalheDto
            {
                CodCliente = cod,
                Nome = nome,
                Celular = cli.Celular ?? "",
                Titulos = titulos.Select(t => new FinCobrancaTituloDto
                {
                    Codigo = t.Codigo,
                    Valor = t.Valor,
                    ValorComJuros = t.ValorComJuros,
                    DataMovimento = t.DataMovimento,
                    DataVencimento = t.DataVencimento,
                    DiasAtraso = t.DiasAtraso,
                    EhBoleto = t.EhBoleto
                }).ToList(),
                Total = titulos.Sum(t => t.Valor),
                TotalComJuros = titulos.Sum(t => t.ValorComJuros)
            };

            // Cobrança
            var sb = new StringBuilder();
            sb.AppendLine($"Olá, {nome} tudo bem?");
            sb.AppendLine("Ainda não identificamos o pagamento dos títulos a seguir:");
            sb.AppendLine();
            foreach (var t in titulos)
                sb.AppendLine($"Valor: {Moeda(t.ValorComJuros)} | Vencimento: {t.DataVencimento:dd/MM/yyyy} | {t.DiasAtraso} dia(s) em atraso");
            sb.AppendLine();
            sb.AppendLine($"Total: {Moeda(d.TotalComJuros)}");
            sb.AppendLine();
            sb.AppendLine("Qualquer dúvida, estou à disposição!");
            d.MensagemCobranca = sb.ToString();

            // Lembrete
            sb.Clear();
            sb.AppendLine($"Olá, {nome} tudo bem?");
            sb.AppendLine("Passando apenas para lembrar dos títulos a seguir:");
            sb.AppendLine();
            foreach (var t in titulos)
                sb.AppendLine($"Valor: {Moeda(t.ValorComJuros)} | Vencimento: {t.DataVencimento:dd/MM/yyyy}");
            sb.AppendLine();
            sb.AppendLine($"Total: {Moeda(d.TotalComJuros)}");
            sb.AppendLine();
            sb.AppendLine("Caso já tenha sido pago, favor desconsiderar!");
            sb.AppendLine("Qualquer dúvida, estou à disposição!");
            d.MensagemLembrete = sb.ToString();

            d.UrlCobranca = LinkWhatsApp(d.Celular, d.MensagemCobranca);
            d.UrlLembrete = LinkWhatsApp(d.Celular, d.MensagemLembrete);
            return d;
        }

        private static string LinkWhatsApp(string celular, string mensagem)
        {
            var numero = new string((celular ?? "").Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(numero)) return "";
            if (!numero.StartsWith("55")) numero = "55" + numero;
            return $"https://wa.me/{numero}?text={Uri.EscapeDataString(mensagem)}";
        }

        private static async Task RegistrarLogAsync(
            HttpContext http, LogFinanceiroService log, MobileRepository repo,
            MobileUsuarioContexto u, string tipo, string detalhe)
        {
            try
            {
                await log.RegistrarAsync(
                    usuario: u.Usuario,
                    codEmp: u.CodEmp,
                    nomeEmpresa: await repo.NomeEmpresaAsync(u.CodEmp),
                    tipoEvento: tipo,
                    detalhe: detalhe,
                    ip: http.Connection.RemoteIpAddress?.ToString());
            }
            catch { /* log não pode impedir a operação */ }
        }
    }
}
