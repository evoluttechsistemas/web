using System.Data;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using EvolutCRM.Models;

namespace EvolutCRM.Services;

public sealed class BoletoInterService(
    IConfiguration configuration, UserState userState, AdminService admin,
    LogFinanceiroService financeiroLog, IHttpClientFactory httpFactory,
    ILogger<BoletoInterService> logger)
{
    private SqlConnection Conexao() => new(configuration.GetConnectionString("Connection")
        ?? configuration["Connection"] ?? throw new InvalidOperationException("Conexão Connection não configurada."));

    private sealed record Contexto(int Empresa, string Usuario, string NomeEmpresa);

    private async Task<Contexto> AutorizarAsync()
    {
        if (!userState.IsReady || userState.CurrentCompanyId <= 0 || string.IsNullOrWhiteSpace(userState.CurrentUser))
            throw new InvalidOperationException("Aguarde a identificação do usuário e da empresa ou entre novamente no HELP.");
        var ctx = new Contexto(userState.CurrentCompanyId, userState.CurrentUser.Trim(), userState.CurrentCompany);
        if (!await admin.UsuarioTemPermissaoAsync(ctx.Usuario, "ADMIN_ACESSAR")
            || !await admin.UsuarioTemPermissaoAsync(ctx.Usuario, "ADMIN_ADMINISTRAR_CLIENTES"))
            throw new UnauthorizedAccessException("Usuário sem permissão para operar o financeiro.");
        if (userState.CurrentCompanyId != ctx.Empresa || userState.CurrentUser.Trim() != ctx.Usuario)
            throw new InvalidOperationException("A empresa ou o usuário mudou. Atualize a tela.");
        return ctx;
    }

    public async Task<List<BoletoInterOpcao>> ListarConfiguracoesAsync()
    {
        var ctx = await AutorizarAsync();
        using var conn = Conexao();
        return (await conn.QueryAsync<BoletoInterOpcao>("""
            SELECT bb.Codigo, bb.Descricao
            FROM BoletoBancario bb
            INNER JOIN ContaBancaria cb ON cb.Codigo = bb.CodConta
            WHERE cb.CodEmp = @Empresa AND bb.Banco = 'cobBancoInter'
              AND ISNULL(bb.Inativo, 'N') = 'N'
            ORDER BY bb.Descricao;
            """, new { ctx.Empresa })).ToList();
    }

    public async Task<List<BoletoInterTitulo>> ListarTitulosAsync(DateTime inicio, DateTime fim, string busca, string situacao)
    {
        var ctx = await AutorizarAsync();
        if (inicio.Date > fim.Date || (fim.Date - inicio.Date).TotalDays > 366)
            throw new InvalidOperationException("Informe um período válido de até 366 dias.");
        using var conn = Conexao();
        return (await conn.QueryAsync<BoletoInterTitulo>("""
            SELECT TOP (500) cr.Codigo, ISNULL(cr.NomeCliente, '') Cliente,
              ISNULL(c.Celular, '') Telefone, ISNULL(cr.Valor, 0) Valor, cr.DataVencimento Vencimento,
              CAST(CASE WHEN cr.DataPagamento IS NOT NULL OR cr.ValorPago IS NOT NULL THEN 1 ELSE 0 END AS bit) Pago,
              COALESCE(NULLIF(cr.BoletoInterCodigoSolicitacao, ''),
                CASE WHEN bb.Banco = 'cobBancoInter' THEN cr.NossoNumero END, '') CodigoSolicitacao,
              ISNULL(cr.BoletoInterEstado, '') Estado,
              ISNULL(cr.BoletoInterSituacaoBanco, '') SituacaoBanco,
              ISNULL(cr.BoletoInterLinhaDigitavel, '') LinhaDigitavel,
              CAST(CASE WHEN DATALENGTH(cr.BoletoPdf) > 0 THEN 1 ELSE 0 END AS bit) TemPdf,
              CAST(CASE WHEN NULLIF(NULLIF(LTRIM(RTRIM(CAST(cr.CodBoletoBancario AS varchar(50)))), '0'), '') IS NOT NULL
                OR NULLIF(LTRIM(RTRIM(cr.NossoNumero)), '') IS NOT NULL
                OR NULLIF(LTRIM(RTRIM(cr.PixCopiaECola)), '') IS NOT NULL
                OR NULLIF(LTRIM(RTRIM(CAST(cr.IDTransacaoPIX AS varchar(200)))), '') IS NOT NULL
                OR NULLIF(cr.BoletoInterCodigoSolicitacao, '') IS NOT NULL THEN 1 ELSE 0 END AS bit) TemVinculo,
              ISNULL(cr.BoletoInterErro, '') Erro
            FROM dbo.ContaReceber cr
            LEFT JOIN Cliente c ON c.Codigo = cr.CodCliente AND c.CodEmp = cr.CodEmp
            LEFT JOIN BoletoBancario bb ON bb.Codigo = cr.CodBoletoBancario
            WHERE cr.CodEmp = @Empresa AND cr.TipoPagamento = 'CRD'
            AND ISNULL(c.EmiteBoleto, 'N') = 'S'
              AND cr.DataVencimento >= @Inicio AND cr.DataVencimento < @Fim
              AND (@Busca = '' OR cr.NomeCliente LIKE @Termo OR CAST(cr.Codigo AS varchar(30)) = @Busca)
              AND (@Situacao = 'todos'
                OR (@Situacao = 'abertos' AND cr.DataPagamento IS NULL AND cr.ValorPago IS NULL)
                OR (@Situacao = 'pdf-pendente' AND cr.BoletoPdf IS NULL
                  AND (cr.BoletoInterEstado IS NOT NULL OR bb.Banco = 'cobBancoInter')))
            ORDER BY cr.DataVencimento, cr.Codigo;
            """, new { ctx.Empresa, Inicio = inicio.Date, Fim = fim.Date.AddDays(1),
                Busca = busca.Trim(), Termo = "%" + busca.Trim() + "%", Situacao = situacao })).ToList();
    }

    public async Task<BoletoInterResultado> EmitirAsync(int titulo, int configuracao)
    {
        var ctx = await AutorizarAsync();
        return await ComBloqueioAsync(ctx, titulo, async conn =>
        {
            var dados = await LerTituloAsync(conn, ctx, titulo);
            if (dados.CodigoSolicitacao.Length > 0 || dados.NossoNumero.Length > 0)
                return await RecuperarInternoAsync(conn, ctx, dados, configuracao, null);
            if (dados.Estado.Length > 0 || dados.Configuracao > 0 || dados.Pix.Length > 0
                || await TemOutroPixAsync(conn, ctx, titulo))
                throw new InvalidOperationException("Este título já possui vínculo ou tentativa de emissão. Confira a cobrança no Inter e use Recuperar/Vincular solicitação.");
            ValidarTitulo(dados);
            var cfg = await LerConfiguracaoAsync(conn, ctx, configuracao, exigirAtivo: true);
            var json = CriarJsonEmissao(dados, cfg);
            using var banco = new BancoInterClient(cfg, configuration);
            await banco.AutenticarAsync();

            var alterados = await conn.ExecuteAsync("""
                UPDATE dbo.ContaReceber SET BoletoInterEstado = 'EMISSAO_INCERTA',
                  CodBoletoBancario = @Configuracao, BoletoInterUsuario = @Usuario,
                  BoletoInterJsonEnvio = @Json, BoletoInterErro = NULL
                WHERE Codigo = @Titulo AND CodEmp = @Empresa
                  AND DataPagamento IS NULL AND ValorPago IS NULL
                  AND TipoPagamento = 'CRD'
                  AND NULLIF(BoletoInterEstado, '') IS NULL
                  AND NULLIF(BoletoInterCodigoSolicitacao, '') IS NULL
                  AND ISNULL(CAST(CodBoletoBancario AS varchar(50)), '0') IN ('', '0')
                  AND ISNULL(LTRIM(RTRIM(NossoNumero)), '') = ''
                  AND ISNULL(LTRIM(RTRIM(PixCopiaECola)), '') = ''
                  AND ISNULL(LTRIM(RTRIM(CAST(IDTransacaoPIX AS varchar(200)))), '') = ''
                  AND CodCliente = @CodCliente AND Valor = @Valor AND DataVencimento = @Vencimento;
                """, new { Configuracao = cfg.Codigo, ctx.Usuario, Json = json, Titulo = titulo,
                    ctx.Empresa, dados.CodCliente, dados.Valor, Vencimento = dados.Vencimento });
            if (alterados != 1) throw new InvalidOperationException("O título mudou ou já está em emissão. Atualize a lista.");

            string codigo;
            try { codigo = await banco.EmitirAsync(json); }
            catch (Exception ex)
            {
                await SalvarErroAsync(conn, ctx, titulo, "Emissão sem confirmação. Confira no Inter antes de emitir novamente.");
                logger.LogWarning("Emissão Inter sem confirmação. Empresa {Empresa}, título {Titulo}, tipo {Tipo}.", ctx.Empresa, titulo, ex.GetType().Name);
                throw new InvalidOperationException("A emissão ficou sem confirmação. Confira no Inter pelo seu número "
                    + titulo + ". Se existir cobrança, use Vincular solicitação. O HELP não repetirá o pedido automaticamente.");
            }
            try { await GravarIdentificadorAsync(conn, ctx, titulo, cfg.Codigo, codigo, novaEmissao: true); }
            catch (Exception)
            {
                logger.LogError("Inter registrou {Codigo}, mas houve falha de persistência. Empresa {Empresa}, título {Titulo}.", codigo, ctx.Empresa, titulo);
                throw new InvalidOperationException($"O Inter registrou a cobrança {codigo}, mas não foi possível salvar o identificador. Guarde esse código e use Vincular solicitação; não emita novamente.");
            }
            await LogAsync(ctx, "BOLETO_INTER_EMITIDO", $"Título {titulo}; solicitação {codigo}.");
            return await SalvarDocumentoAsync(conn, ctx, dados, codigo, banco);
        });
    }

    public async Task<BoletoInterResultado> RecuperarAsync(int titulo, int configuracao, string? codigoInformado = null)
    {
        var ctx = await AutorizarAsync();
        return await ComBloqueioAsync(ctx, titulo, async conn =>
            await RecuperarInternoAsync(conn, ctx, await LerTituloAsync(conn, ctx, titulo), configuracao, codigoInformado));
    }

    private async Task<BoletoInterResultado> RecuperarInternoAsync(SqlConnection conn, Contexto ctx,
        BoletoInterDados dados, int configuracao, string? codigoInformado)
    {
        // Boleto antigo (emitido pelo ERP na API v2): só tem o Nosso Número, sem o código da solicitação (GUID) da v3.
        string? nossoNumeroAntigo = EhFormatoAntigo(dados) ? dados.NossoNumero.Trim() : null;
        string existente = dados.CodigoSolicitacao.Length > 0 ? dados.CodigoSolicitacao : nossoNumeroAntigo is null ? dados.NossoNumero : "";
        string codigo = string.IsNullOrWhiteSpace(codigoInformado) ? existente : codigoInformado.Trim();
        if (codigo.Length > 0 && !Guid.TryParse(codigo, out _)) throw new InvalidOperationException("Informe o código da solicitação válido, obtido no Inter.");
        if (existente.Length > 0 && !string.Equals(existente, codigo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Este título já possui outra solicitação. Não é permitido substituir o boleto por esta tela.");
        // Se já está vinculado, utiliza sempre a configuração original, inclusive quando inativa.
        var cfg = await LerConfiguracaoAsync(conn, ctx, dados.Configuracao > 0 ? dados.Configuracao : configuracao, exigirAtivo: false);
        using var banco = new BancoInterClient(cfg, configuration);
        await banco.AutenticarAsync();
        if (codigo.Length == 0)
        {
            if (nossoNumeroAntigo is null) throw new InvalidOperationException("Informe o código da solicitação válido, obtido no Inter.");
            codigo = await LocalizarPorNossoNumeroAsync(banco, dados, nossoNumeroAntigo) ?? "";
            if (codigo.Length == 0)
            {
                var msg = $"Boleto emitido pelo ERP (formato antigo): o Inter não retornou cobrança com o Nosso Número {nossoNumeroAntigo} "
                    + "para este CPF/CNPJ perto do vencimento. Confira no Internet Banking do Inter e use Vincular solicitação, se encontrar.";
                await SalvarErroAsync(conn, ctx, dados.Codigo, msg);
                throw new InvalidOperationException(msg);
            }
        }
        using var consulta = await banco.ConsultarAsync(codigo);
        if (nossoNumeroAntigo is not null)
        {
            // Para o boleto antigo, o Nosso Número precisa bater; o "seu número" pode ter sido gerado pelo ERP de outro jeito.
            consulta.RootElement.TryGetProperty("boleto", out var bol);
            if (!MesmoNossoNumero(BancoInterClient.Texto(bol, "nossoNumero"), nossoNumeroAntigo))
                throw new InvalidOperationException($"A cobrança informada não tem o Nosso Número {nossoNumeroAntigo} deste título. Confira antes de vincular.");
            ValidarCorrespondencia(consulta.RootElement, dados, exigirSeuNumero: false);
        }
        else ValidarCorrespondencia(consulta.RootElement, dados);
        await GravarIdentificadorAsync(conn, ctx, dados.Codigo, cfg.Codigo, codigo, nossoNumeroAntigo: nossoNumeroAntigo);
        await GravarConsultaAsync(conn, ctx, dados.Codigo, consulta.RootElement);
        await LogAsync(ctx, "BOLETO_INTER_RECUPERADO", $"Título {dados.Codigo}; solicitação {codigo}.");
        return await SalvarDocumentoAsync(conn, ctx, dados, codigo, banco);
    }

    private async Task<BoletoInterResultado> SalvarDocumentoAsync(SqlConnection conn, Contexto ctx,
        BoletoInterDados dados, string codigo, BancoInterClient banco)
    {
        try
        {
            var pdf = await banco.BaixarPdfAsync(codigo);
            var nome = $"boleto-{ctx.Empresa}-{dados.Codigo}-{codigo}.pdf";
            var alterados = await conn.ExecuteAsync("""
                UPDATE dbo.ContaReceber SET BoletoPdf = @Pdf, BoletoPdfNome = @Nome,
                  BoletoPdfSalvoEm = SYSUTCDATETIME(), BoletoInterEstado = 'PDF_SALVO', BoletoInterErro = NULL
                WHERE Codigo = @Titulo AND CodEmp = @Empresa AND BoletoInterCodigoSolicitacao = @Codigo;
                """, new { Pdf = pdf, Nome = nome, Titulo = dados.Codigo, ctx.Empresa, Codigo = codigo });
            if (alterados != 1) throw new InvalidOperationException("O vínculo da cobrança foi alterado durante a recuperação.");
            // A falha de consulta dos detalhes não invalida o PDF já persistido.
            try
            {
                using var consulta = await banco.ConsultarAsync(codigo);
                await GravarConsultaAsync(conn, ctx, dados.Codigo, consulta.RootElement);
            }
            catch (Exception ex) { logger.LogWarning("PDF salvo, detalhes pendentes para título {Titulo}: {Tipo}.", dados.Codigo, ex.GetType().Name); }
            return new(true, "PDF salvo no banco de dados. Pode abrir, baixar, imprimir ou enviar por WhatsApp.");
        }
        catch (Exception ex)
        {
            await SalvarErroAsync(conn, ctx, dados.Codigo, "Cobrança registrada; recuperação do PDF pendente.");
            logger.LogWarning("PDF Inter pendente. Empresa {Empresa}, título {Titulo}, tipo {Tipo}.", ctx.Empresa, dados.Codigo, ex.GetType().Name);
            return new(false, "A cobrança está registrada, mas o PDF ainda não pôde ser salvo. Use Recuperar PDF; não é necessário emitir novamente.");
        }
    }

    public async Task<BoletoPdfArquivo> ObterPdfAsync(int titulo)
    {
        var ctx = await AutorizarAsync();
        return await ObterPdfInternoAsync(ctx, titulo);
    }

    private async Task<BoletoPdfArquivo> ObterPdfInternoAsync(Contexto ctx, int titulo)
    {
        using var conn = Conexao();
        var arquivo = await conn.QuerySingleOrDefaultAsync<BoletoPdfArquivo>("""
            SELECT BoletoPdf Conteudo, BoletoPdfNome Nome FROM dbo.ContaReceber
            WHERE Codigo = @Titulo AND CodEmp = @Empresa AND DATALENGTH(BoletoPdf) > 0;
            """, new { Titulo = titulo, ctx.Empresa });
        if (arquivo is null) throw new InvalidOperationException("PDF ainda não salvo. Use Recuperar PDF.");
        BancoInterClient.ValidarPdf(arquivo.Conteudo);
        return arquivo;
    }

    public async Task<List<BoletoWhatsAppOpcao>> ListarInstanciasAsync()
    {
        var ctx = await AutorizarAsync();
        using var conn = Conexao();
        return (await conn.QueryAsync<BoletoWhatsAppOpcao>("""
            SELECT Codigo, Nome FROM WhatsAppInstancia
            WHERE CodEmp = @Empresa AND Status = 'conectado' ORDER BY Nome;
            """, new { ctx.Empresa })).ToList();
    }

    // Chamada explícita pelo usuário. Nunca há envio automático ao emitir ou consultar.
    public async Task EnviarWhatsAppAsync(int titulo, int instancia, string telefone)
    {
        var ctx = await AutorizarAsync();
        var numero = SomenteNumeros(telefone);
        if (numero.Length is 10 or 11) numero = "55" + numero;
        if (numero.Length is < 12 or > 15) throw new InvalidOperationException("Informe o telefone com DDI e DDD.");
        using var conn = Conexao();
        var conectada = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM WhatsAppInstancia WHERE CodEmp = @Empresa
              AND Codigo = @Instancia AND Status = 'conectado';
            """, new { ctx.Empresa, Instancia = instancia });
        if (conectada != 1) throw new InvalidOperationException("Selecione uma instância conectada da empresa atual.");
        var pdf = await ObterPdfInternoAsync(ctx, titulo);
        using var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        var baseUrl = configuration["BoletoInter:WhatsAppBaseUrl"] ?? "http://localhost:3333";
        using var response = await client.PostAsJsonAsync(baseUrl.TrimEnd('/') + "/enviar-documento", new
        {
            contact_phone_number = numero, base64 = Convert.ToBase64String(pdf.Conteudo),
            mimetype = "application/pdf", filename = pdf.Nome,
            caption = $"Segue o boleto referente ao título {titulo}.", cod_instancia = instancia
        });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Envio sem confirmação (HTTP {(int)response.StatusCode}). Confira o WhatsApp antes de reenviar.");
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sucesso = result.RootElement.EnumerateObject().FirstOrDefault(
            p => p.Name.Equals("sucesso", StringComparison.OrdinalIgnoreCase)).Value;
        if (sucesso.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("A integração WhatsApp não confirmou o envio. Confira a conversa antes de reenviar.");
        await LogAsync(ctx, "BOLETO_INTER_WHATSAPP", $"Título {titulo}; instância {instancia}; destino {numero}.");
    }

    private async Task<T> ComBloqueioAsync<T>(Contexto ctx, int titulo, Func<SqlConnection, Task<T>> action)
    {
        if (titulo <= 0) throw new InvalidOperationException("Título inválido.");
        await using var conn = Conexao();
        await conn.OpenAsync();
        var recurso = $"HELP:BOLETO_INTER:{ctx.Empresa}:{titulo}";
        var status = await conn.QuerySingleAsync<int>("""
            DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource=@Recurso, @LockMode='Exclusive',
                @LockOwner='Session', @LockTimeout=0;
            SELECT @r;
            """, new { Recurso = recurso });
        if (status < 0) throw new InvalidOperationException("Este título já está sendo processado. Aguarde e atualize a lista.");
        try { return await action(conn); }
        finally
        {
            if (conn.State == ConnectionState.Open)
            {
                try { await conn.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource=@Recurso, @LockOwner='Session';", new { Recurso = recurso }); }
                catch (Exception ex)
                {
                    // Não devolve uma sessão com lock para o pool se a liberação falhar.
                    SqlConnection.ClearPool(conn);
                    logger.LogWarning("Falha ao liberar lock do título {Titulo}: {Tipo}.", titulo, ex.GetType().Name);
                }
            }
        }
    }

    private static async Task<BoletoInterDados> LerTituloAsync(SqlConnection conn, Contexto ctx, int titulo)
    {
        var dados = await conn.QuerySingleOrDefaultAsync<BoletoInterDados>("""
            SELECT cr.Codigo, cr.CodCliente, cr.CodEmp, ISNULL(cr.CodBoletoBancario, 0) Configuracao,
              ISNULL(cr.Valor, 0) Valor, cr.DataVencimento Vencimento,
              CAST(CASE WHEN cr.DataPagamento IS NOT NULL OR cr.ValorPago IS NOT NULL THEN 1 ELSE 0 END AS bit) Pago,
              ISNULL(cr.TipoPagamento, '') TipoPagamento,
              ISNULL(cr.BoletoInterCodigoSolicitacao, '') CodigoSolicitacao,
              ISNULL(cr.BoletoInterEstado, '') Estado, ISNULL(cr.NossoNumero, '') NossoNumero,
              ISNULL(cr.PixCopiaECola, '') Pix, c.Nome, c.CPF Cpf, c.Email,
              c.Endereco, c.NumeroEndereco Numero, c.ComplementoEndereco Complemento,
              c.Bairro, c.NomeCidade Cidade, c.UF Uf, c.CEP Cep,
            ISNULL(c.EmiteBoleto, 'N') EmiteBoleto
            FROM dbo.ContaReceber cr
            INNER JOIN Cliente c ON c.Codigo = cr.CodCliente AND c.CodEmp = cr.CodEmp
            WHERE cr.Codigo = @Titulo AND cr.CodEmp = @Empresa;
            """, new { Titulo = titulo, ctx.Empresa });
        return dados ?? throw new InvalidOperationException("Título ou cliente não encontrado na empresa atual.");
    }

    private static Task<bool> TemOutroPixAsync(SqlConnection conn, Contexto ctx, int titulo) =>
        conn.ExecuteScalarAsync<bool>("""
            SELECT CAST(CASE WHEN NULLIF(LTRIM(RTRIM(CAST(IDTransacaoPIX AS varchar(200)))), '') IS NOT NULL
                THEN 1 ELSE 0 END AS bit) FROM dbo.ContaReceber WHERE Codigo=@Titulo AND CodEmp=@Empresa;
            """, new { Titulo = titulo, ctx.Empresa });

    private async Task<BoletoInterConfig> LerConfiguracaoAsync(SqlConnection conn, Contexto ctx, int codigo, bool exigirAtivo)
    {
        // Mesmos campos e JOIN do Forms; credenciais podem estar em BoletoBancario ou ContaBancaria.
        var row = await conn.QuerySingleOrDefaultAsync("""
            SELECT bb.Codigo, ClienteID, ClienteSecret, Scope, ArquivoCRT, ArquivoKEY,
              Instrucao, Mora, Multa
            FROM BoletoBancario bb INNER JOIN ContaBancaria cb ON cb.Codigo = bb.CodConta
            WHERE bb.Codigo = @Codigo AND cb.CodEmp = @Empresa AND bb.Banco = 'cobBancoInter'
              AND (@Ativo = 0 OR ISNULL(bb.Inativo, 'N') = 'N');
            """, new { Codigo = codigo, ctx.Empresa, Ativo = exigirAtivo });
        if (row is null) throw new InvalidOperationException("Configuração Banco Inter não encontrada para a empresa atual.");
        var map = (IDictionary<string, object>)row;
        string V(string k) => Convert.ToString(map[k], CultureInfo.InvariantCulture)?.Trim() ?? "";
        decimal Taxa(string k)
        {
            var value = map[k];
            if (value is null || value is DBNull || V(k).Length == 0) return 0;
            if (value is decimal dec) return dec;
            return decimal.TryParse(V(k).Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var number) ? number
                : throw new InvalidOperationException($"Taxa {k} inválida na configuração bancária.");
        }
        if (V("ClienteID").Length == 0 || V("ClienteSecret").Length == 0)
            throw new InvalidOperationException("Client ID ou Client Secret não configurado para o Inter.");
        return new(codigo, V("ClienteID"), V("ClienteSecret"),
            V("Scope").Length > 0 ? V("Scope") : "boleto-cobranca.write boleto-cobranca.read",
            V("ArquivoCRT"), V("ArquivoKEY"), V("Instrucao"), Taxa("Mora"), Taxa("Multa"),
            configuration[$"BoletoInter:ContasCorrentes:{codigo}"] ?? "");
    }

    internal static void ValidarTitulo(BoletoInterDados d)
    {
        if (!string.Equals(d.EmiteBoleto?.Trim(), "S", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Este cliente não está marcado para emitir boleto (cadastro do cliente: Emite boleto = S).");

        if (d.Pago || d.TipoPagamento != "CRD" || d.Valor <= 0 || d.Vencimento.Date < DateTime.Today)
            throw new InvalidOperationException("Emita somente títulos CRD em aberto, com valor positivo e vencimento de hoje em diante.");
        if (SomenteNumeros(d.Cpf).Length is not (11 or 14) || SomenteNumeros(d.Cep).Length != 8
            || string.IsNullOrWhiteSpace(d.Nome) || string.IsNullOrWhiteSpace(d.Endereco)
            || string.IsNullOrWhiteSpace(d.Bairro) || string.IsNullOrWhiteSpace(d.Cidade)
            || (d.Uf?.Trim().Length ?? 0) != 2)
            throw new InvalidOperationException("Complete o cadastro do cliente: nome, CPF/CNPJ, endereço, bairro, cidade, UF e CEP.");
    }

    internal static string CriarJsonEmissao(BoletoInterDados d, BoletoInterConfig cfg)
    {
        if (cfg.Mora < 0 || cfg.Multa < 0) throw new InvalidOperationException("Juros e multa não podem ser negativos.");
        return JsonSerializer.Serialize(new
        {
            seuNumero = d.Codigo.ToString(CultureInfo.InvariantCulture), valorNominal = d.Valor,
            dataVencimento = d.Vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), numDiasAgenda = 30,
            pagador = new
            {
                cpfCnpj = SomenteNumeros(d.Cpf), tipoPessoa = SomenteNumeros(d.Cpf).Length == 11 ? "FISICA" : "JURIDICA",
                nome = d.Nome.Trim(), email = string.IsNullOrWhiteSpace(d.Email) ? null : d.Email.Trim(),
                endereco = d.Endereco.Trim(), numero = string.IsNullOrWhiteSpace(d.Numero) ? "S/N" : d.Numero.Trim(),
                complemento = string.IsNullOrWhiteSpace(d.Complemento) ? null : d.Complemento.Trim(),
                bairro = d.Bairro.Trim(), cidade = d.Cidade.Trim(), uf = d.Uf.Trim().ToUpperInvariant(), cep = SomenteNumeros(d.Cep)
            },
            mensagem = new { linha1 = "Referente ao título " + d.Codigo, linha2 = string.IsNullOrWhiteSpace(cfg.Instrucao) ? null : cfg.Instrucao },
            multa = cfg.Multa > 0 ? new { codigo = "PERCENTUAL", taxa = cfg.Multa } : null,
            mora = cfg.Mora > 0 ? new { codigo = "TAXAMENSAL", taxa = cfg.Mora } : null
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    internal static bool EhFormatoAntigo(BoletoInterDados d) =>
        d.CodigoSolicitacao.Length == 0 && d.NossoNumero.Trim().Length > 0 && !Guid.TryParse(d.NossoNumero.Trim(), out _);

    internal static bool MesmoNossoNumero(string a, string b)
    {
        string na = SomenteNumeros(a).TrimStart('0'), nb = SomenteNumeros(b).TrimStart('0');
        return na.Length > 0 && na == nb;
    }

    // Procura no Inter (v3) a cobrança do cliente com o Nosso Número do boleto antigo, alguns dias em volta do vencimento.
    private static async Task<string?> LocalizarPorNossoNumeroAsync(BancoInterClient banco, BoletoInterDados d, string nossoNumero)
    {
        var cpf = SomenteNumeros(d.Cpf);
        if (cpf.Length is not (11 or 14) || d.Vencimento == default)
            throw new InvalidOperationException("Boleto emitido pelo ERP (formato antigo): o cliente precisa ter CPF/CNPJ e o título precisa ter vencimento para localizar no Inter.");
        var venc = d.Vencimento.Date;
        for (int pagina = 0; pagina < 10; pagina++)
        {
            using var lista = await banco.ListarCobrancasAsync(venc.AddDays(-7), venc.AddDays(7), cpf, pagina);
            var root = lista.RootElement;
            if (!root.TryGetProperty("cobrancas", out var itens) || itens.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in itens.EnumerateArray())
            {
                item.TryGetProperty("cobranca", out var cob);
                item.TryGetProperty("boleto", out var bol);
                var nn = BancoInterClient.Texto(bol, "nossoNumero");
                if (nn.Length == 0) nn = BancoInterClient.Texto(cob, "nossoNumero");
                if (!MesmoNossoNumero(nn, nossoNumero)) continue;
                var cod = BancoInterClient.Texto(cob, "codigoSolicitacao");
                if (Guid.TryParse(cod, out _)) return cod;
            }
            if (itens.GetArrayLength() == 0 || BancoInterClient.Texto(root, "ultimaPagina") == "True"
                || (int.TryParse(BancoInterClient.Texto(root, "totalPaginas"), out var total) && pagina + 1 >= total))
                return null;
        }
        return null;
    }

    internal static void ValidarCorrespondencia(JsonElement root, BoletoInterDados d, bool exigirSeuNumero = true)
    {
        root.TryGetProperty("cobranca", out var cobranca);
        // Aceita o pagador junto aos detalhes da cobrança ou no envelope de consulta.
        var temPagador = cobranca.ValueKind == JsonValueKind.Object && cobranca.TryGetProperty("pagador", out _);
        var pagador = temPagador ? cobranca.GetProperty("pagador")
            : root.TryGetProperty("pagador", out var p) ? p : default;
        if (cobranca.ValueKind != JsonValueKind.Object
            || (exigirSeuNumero && BancoInterClient.Texto(cobranca, "seuNumero") != d.Codigo.ToString(CultureInfo.InvariantCulture))
            || !decimal.TryParse(BancoInterClient.Texto(cobranca, "valorNominal"), NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            || valor != d.Valor || BancoInterClient.Texto(cobranca, "dataVencimento") != d.Vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            || pagador.ValueKind != JsonValueKind.Object
            || SomenteNumeros(BancoInterClient.Texto(pagador, "cpfCnpj")) != SomenteNumeros(d.Cpf))
            throw new InvalidOperationException("A cobrança consultada não corresponde ao título, valor, vencimento e CPF/CNPJ deste cliente. Confira os dados antes de vincular.");
    }

    private static async Task GravarIdentificadorAsync(SqlConnection conn, Contexto ctx, int titulo, int configuracao, string codigo, bool novaEmissao = false, string? nossoNumeroAntigo = null)
    {
        // Boleto antigo: mantém o Nosso Número original do ERP e só grava o código da solicitação da v3.
        var n = await conn.ExecuteAsync("""
            UPDATE dbo.ContaReceber SET BoletoInterCodigoSolicitacao = @Codigo,
              NossoNumero = CASE WHEN @Antigo IS NULL THEN @Codigo ELSE NossoNumero END,
              CodBoletoBancario = @Configuracao,
              BoletoInterEstado = CASE WHEN DATALENGTH(BoletoPdf)>0 THEN 'PDF_SALVO' ELSE 'PDF_PENDENTE' END,
              BoletoInterEmitidoEm = CASE WHEN @NovaEmissao = 1 THEN COALESCE(BoletoInterEmitidoEm, SYSUTCDATETIME()) ELSE BoletoInterEmitidoEm END,
              BoletoInterUsuario = COALESCE(BoletoInterUsuario, @Usuario), BoletoInterErro = NULL
            WHERE Codigo = @Titulo AND CodEmp = @Empresa
              AND (NULLIF(BoletoInterCodigoSolicitacao, '') IS NULL OR BoletoInterCodigoSolicitacao = @Codigo)
              AND (ISNULL(CAST(CodBoletoBancario AS varchar(50)), '0') IN ('', '0') OR CodBoletoBancario = @Configuracao)
              AND (NULLIF(LTRIM(RTRIM(NossoNumero)), '') IS NULL OR NossoNumero = @Codigo
                OR (@Antigo IS NOT NULL AND LTRIM(RTRIM(NossoNumero)) = @Antigo));
            """, new { Codigo = codigo, Configuracao = configuracao, ctx.Usuario, Titulo = titulo, ctx.Empresa, NovaEmissao = novaEmissao,
                Antigo = nossoNumeroAntigo });
        if (n != 1) throw new InvalidOperationException("O vínculo do título mudou. Atualize a tela.");
    }

    private static Task GravarConsultaAsync(SqlConnection conn, Contexto ctx, int titulo, JsonElement root)
    {
        root.TryGetProperty("cobranca", out var cobranca);
        root.TryGetProperty("boleto", out var boleto);
        root.TryGetProperty("pix", out var pix);
        return conn.ExecuteAsync("""
            UPDATE dbo.ContaReceber SET BoletoInterLinhaDigitavel = COALESCE(NULLIF(@Linha, ''), BoletoInterLinhaDigitavel),
              PixCopiaECola = COALESCE(NULLIF(@Pix, ''), PixCopiaECola),
              BoletoInterSituacaoBanco = NULLIF(@Situacao, ''), BoletoInterConsultadoEm = SYSUTCDATETIME()
            WHERE Codigo = @Titulo AND CodEmp = @Empresa;
            """, new { Linha = BancoInterClient.Texto(boleto, "linhaDigitavel"),
                Pix = BancoInterClient.Texto(pix, "pixCopiaECola"),
                Situacao = BancoInterClient.Texto(cobranca, "situacao"), Titulo = titulo, ctx.Empresa });
    }

    private static Task SalvarErroAsync(SqlConnection conn, Contexto ctx, int titulo, string erro) => conn.ExecuteAsync("""
        UPDATE dbo.ContaReceber SET BoletoInterErro = @Erro WHERE Codigo=@Titulo AND CodEmp=@Empresa;
        """, new { Erro = erro, Titulo = titulo, ctx.Empresa });

    private Task LogAsync(Contexto ctx, string tipo, string detalhe) => financeiroLog.RegistrarAsync(
        ctx.Usuario, ctx.Empresa, ctx.NomeEmpresa, tipo, detalhe, null);

    internal static string SomenteNumeros(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
