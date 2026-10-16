using EvolutCRM.Models;
using EvolutCRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EvolutCRM.Mobile
{
    /// <summary>
    /// API do HELP Mobile. Mesmo padrão do BlogEndpoints: só "porta de entrada" para os services
    /// que já existem — a regra de ticket (envio pelo WhatsApp, leitura, histórico) continua no TicketService.
    /// </summary>
    public static class MobileEndpoints
    {
        private const string ItemUsuario = "MobileUsuario";

        private static readonly HashSet<string> UsuariosAutomaticos = new(StringComparer.OrdinalIgnoreCase)
        { "", "NOVO", "WHATSAPP", "CLIENTE", "BOT", "SISTEMA", "IA", "EVOLUTTECH" };

        public static void MapMobileEndpoints(this WebApplication app)
        {
            // ── PÚBLICO: login ───────────────────────────────────────────────
            var publico = app.MapGroup("/api/mobile");

            publico.MapPost("/login", async (
                MobileLoginRequest req,
                LoginService login,
                UserState userState,
                AdminService admin,
                MobileRepository repo,
                MobileTokenService tokens) =>
            {
                if (string.IsNullOrWhiteSpace(req.Usuario) || string.IsNullOrWhiteSpace(req.Senha))
                    return Results.BadRequest(new { mensagem = "Informe usuário e senha." });

                var user = await login.LoginAsync(req.Usuario.Trim(), req.Senha);
                if (user == null)
                    return Results.Json(new { mensagem = "Usuário ou senha inválidos." }, statusCode: 401);

                var usuario = user.Usuario.Trim().ToUpperInvariant();
                var codEmp = user.CodEmp > 0 ? user.CodEmp : 2;

                userState.SetUser(usuario, codEmp, "");

                if (!await admin.UsuarioTemPermissaoAsync(usuario, "TICKETS_VISUALIZAR"))
                    return Results.Json(new { mensagem = "Seu usuário não tem permissão para ver tickets." }, statusCode: 403);

                Console.WriteLine($"[MOBILE] Login -> {usuario} (emp {codEmp})");

                return Results.Ok(new MobileLoginResponse
                {
                    Token = tokens.GerarToken(usuario, codEmp),
                    Usuario = usuario,
                    CodEmp = codEmp,
                    Empresa = await repo.NomeEmpresaAsync(codEmp)
                });
            });

            publico.MapGet("/push/chave", (IConfiguration config) =>
                Results.Ok(new { chave = config["Mobile:VapidPublicKey"] ?? "" }));

            // ── AUTENTICADO ──────────────────────────────────────────────────
            var api = app.MapGroup("/api/mobile")
                .RequireAuthorization()
                .AddEndpointFilter(async (ctx, next) =>
                {
                    var http = ctx.HttpContext;
                    var usuario = http.User.Identity?.Name?.Trim().ToUpperInvariant() ?? "";
                    var codEmpClaim = http.User.FindFirst(MobileTokenService.ClaimCodEmp)?.Value;

                    if (string.IsNullOrWhiteSpace(usuario) || !int.TryParse(codEmpClaim, out var codEmp) || codEmp <= 0)
                        return Results.Unauthorized();

                    var repo = http.RequestServices.GetRequiredService<MobileRepository>();
                    if (!await repo.UsuarioAtivoAsync(usuario, codEmp))
                        return Results.Unauthorized();

                    // TicketService/AdminService usam o UserState para saber a empresa
                    http.RequestServices.GetRequiredService<UserState>().SetUser(usuario, codEmp, "");
                    http.Items[ItemUsuario] = new MobileUsuarioContexto { Usuario = usuario, CodEmp = codEmp };

                    return await next(ctx);
                });

            api.MapGet("/eu", async (HttpContext http, AdminService admin) =>
            {
                var u = Contexto(http);
                return Results.Ok(new MobileEuDto
                {
                    Usuario = u.Usuario,
                    CodEmp = u.CodEmp,
                    PodeResponder = await admin.UsuarioTemPermissaoAsync(u.Usuario, "TICKETS_RESPONDER"),
                    PodeFinanceiro = await MobileFinanceiroEndpoints.PodeFinanceiroAsync(admin, u.Usuario),
                    PodeAlterarStatus = await admin.UsuarioTemPermissaoAsync(u.Usuario, "TICKETS_ALTERAR_STATUS")
                });
            });

            // Lista de conversas
            api.MapGet("/tickets", async (HttpContext http, MobileRepository repo, bool? todos) =>
            {
                // Padrão: meus + fila. ?todos=true: todos os tickets em aberto (aba "Equipe").
                // As notificações não mudam: continuam só meus + fila.
                var u = Contexto(http);
                return Results.Ok(await repo.ListarConversasAsync(u.CodEmp, u.Usuario, todos == true));
            });

            // Cabeçalho do ticket
            api.MapGet("/tickets/{id:int}", async (int id, HttpContext http, TicketService ts, AdminService admin, MobileRepository repo) =>
            {
                var u = Contexto(http);
                var t = await ts.BuscarChamadoAsync(id);
                if (t == null) return Results.NotFound();

                var (podeResponder, podeStatus) = await PermissoesAsync(admin, u.Usuario);
                return Results.Ok(await MontarTicketAsync(t, u, podeResponder, podeStatus, ts, repo));
            });

            // Abrir a conversa numa chamada só: cabeçalho + últimas mensagens + marca como lido
            api.MapGet("/tickets/{id:int}/abrir", async (
                int id, int? limite, HttpContext http,
                TicketService ts, AdminService admin, MobileRepository repo) =>
            {
                var u = Contexto(http);
                var qtd = Math.Clamp(limite ?? LimitePadrao, 10, 500);

                // Cada método do TicketService abre a própria conexão: dá para buscar os dois juntos
                var tTicket = ts.BuscarChamadoAsync(id);
                var tHistorico = ts.BuscarHistoricoAsync(id);
                await Task.WhenAll(tTicket, tHistorico);

                var t = tTicket.Result;
                if (t == null) return Results.NotFound();

                var (podeResponder, podeStatus) = await PermissoesAsync(admin, u.Usuario);
                var ordenado = tHistorico.Result.OrderBy(h => h.Codigo).ToList();

                var dto = new MobileAbrirDto
                {
                    Ticket = await MontarTicketAsync(t, u, podeResponder, podeStatus, ts, repo),
                    Mensagens = ordenado.Skip(Math.Max(0, ordenado.Count - qtd)).Select(ParaDto).ToList(),
                    TemMais = ordenado.Count > qtd
                };

                await MarcarLidoAsync(t, u, ts);
                return Results.Ok(dto);
            });

            // Histórico (ordem de conversa: mais antiga primeiro).
            // ?antes=COD traz as anteriores a essa mensagem (rolar para cima), no máximo ?limite.
            // Sem parâmetros devolve tudo (versões antigas do app).
            api.MapGet("/tickets/{id:int}/mensagens", async (int id, int? antes, int? limite, TicketService ts) =>
            {
                var historico = (await ts.BuscarHistoricoAsync(id)).OrderBy(h => h.Codigo).ToList();

                if (antes is > 0)
                {
                    var qtd = Math.Clamp(limite ?? LimitePadrao, 10, 500);
                    historico = historico.Where(h => h.Codigo < antes.Value).ToList();
                    historico = historico.Skip(Math.Max(0, historico.Count - qtd)).ToList();
                }

                return Results.Ok(historico.Select(ParaDto).ToList());
            });

            // Só o que mudou desde a última mensagem que o app já tem (chamado a cada poucos segundos)
            // ?statusDesde=COD: status de entrega só a partir dessa mensagem (a mais antiga que o app tem)
            api.MapGet("/tickets/{id:int}/atualizacoes", async (
                int id, int depois, int? statusDesde, HttpContext http,
                TicketService ts, AdminService admin, MobileRepository repo) =>
            {
                var u = Contexto(http);
                var resultado = new MobileAtualizacaoDto();

                var resumo = await ts.ResumoHistoricoAsync(id);
                if (resumo.UltimoCodigo > depois)
                {
                    var novas = await ts.BuscarHistoricoNovasAsync(id, depois);
                    resultado.Novas = novas.OrderBy(h => h.Codigo).Select(ParaDto).ToList();

                    var t = await ts.BuscarChamadoAsync(id);
                    if (t != null)
                    {
                        var (podeResponder, podeStatus) = await PermissoesAsync(admin, u.Usuario);
                        resultado.Ticket = await MontarTicketAsync(t, u, podeResponder, podeStatus, ts, repo);
                    }
                }

                resultado.Status = statusDesde.HasValue
                    ? await repo.StatusRecentesAsync(u.CodEmp, id, statusDesde.Value)
                    : await ts.BuscarStatusWhatsAppAsync(id);
                return Results.Ok(resultado);
            });

            // Abriu a conversa: mesma lógica de "lido" da tela Ticket do HELP
            api.MapPost("/tickets/{id:int}/lido", async (int id, HttpContext http, TicketService ts) =>
            {
                var u = Contexto(http);
                var t = await ts.BuscarChamadoAsync(id);
                if (t == null) return Results.NotFound();

                await MarcarLidoAsync(t, u, ts);
                return Results.Ok();
            });

            // Atendentes para transferir o ticket (mesma lista do campo Responsável do HELP)
            api.MapGet("/usuarios", async (TicketService ts) =>
                Results.Ok(await ListarAtendentesAsync(ts)));

            // Finalizar: mesmo caminho do F3 "Finalizar sem insight" da tela Ticket do HELP
            api.MapPost("/tickets/{id:int}/finalizar", async (
                int id, HttpContext http, TicketService ts, AdminService admin) =>
            {
                var u = Contexto(http);
                var (podeResponder, podeStatus) = await PermissoesAsync(admin, u.Usuario);
                if (!podeResponder)
                    return Falha("Você não tem permissão para responder ou alterar este ticket.", 403);
                if (!podeStatus)
                    return Falha("Você não tem permissão para alterar o status do ticket.", 403);

                var t = await ts.BuscarChamadoAsync(id);
                if (t == null) return Falha("Ticket não encontrado.");
                if (t.CodSituacao == 3) return Falha("Este ticket já está finalizado.");
                if (t.CodCliente <= 0) return Falha("Vincule um cliente a este ticket pelo HELP antes de finalizar.");
                if (t.CodSetor == 2 && string.IsNullOrWhiteSpace(t.Versao))
                    return Falha("Informe a versão do sistema pelo HELP para finalizar este ticket.");

                // HELP exige responsável: ticket da fila finalizado pelo celular fica com quem finalizou
                if (UsuariosAutomaticos.Contains((t.Usuario ?? "").Trim()))
                    t.Usuario = u.Usuario;

                t.CodSituacao = 3;

                var result = await ts.SalvarAlteracoesAsync(t, "", u.Usuario);
                if (!result.Sucesso) return Falha(result.Mensagem);

                await ts.SalvarCabecalhoTicketAsync(t, u.Usuario);
                await ts.ResolverAgendasDoTicketAsync(t.Codigo);

                Console.WriteLine($"[MOBILE] Ticket #{id} finalizado por {u.Usuario}");
                return Results.Ok(new MobileEnviarResponse { Sucesso = true, Mensagem = "Ticket finalizado." });
            });

            // Transferir (ou assumir): troca o responsável, igual ao campo Responsável + Salvar do HELP
            api.MapPost("/tickets/{id:int}/transferir", async (
                int id, MobileTransferirRequest req, HttpContext http, TicketService ts, AdminService admin) =>
            {
                var u = Contexto(http);
                if (!await admin.UsuarioTemPermissaoAsync(u.Usuario, "TICKETS_RESPONDER"))
                    return Falha("Você não tem permissão para responder ou alterar este ticket.", 403);

                var destino = (req.Usuario ?? "").Trim().ToUpperInvariant();
                if (destino.Length == 0) return Falha("Escolha para quem transferir.");

                var atendentes = await ListarAtendentesAsync(ts);
                if (!atendentes.Contains(destino)) return Falha("Atendente não encontrado ou inativo.");

                var t = await ts.BuscarChamadoAsync(id);
                if (t == null) return Falha("Ticket não encontrado.");
                if (t.CodSituacao == 3) return Falha("Ticket finalizado. Reabra pelo HELP para transferir.");
                if (t.CodCliente <= 0) return Falha("Vincule um cliente a este ticket pelo HELP antes de transferir.");
                if (string.Equals((t.Usuario ?? "").Trim(), destino, StringComparison.OrdinalIgnoreCase))
                    return Results.Ok(new MobileEnviarResponse { Sucesso = true, Mensagem = "O ticket já está com " + destino + "." });

                var anterior = (t.Usuario ?? "").Trim();
                t.Usuario = destino;

                // Responsável diferente de quem salva → o ticket fica como NOVO para o destino (regra do TicketService)
                var result = await ts.SalvarCabecalhoTicketAsync(t, u.Usuario);
                if (!result.Sucesso) return Falha(result.Mensagem);

                Console.WriteLine($"[MOBILE] Ticket #{id} transferido de {anterior} para {destino} por {u.Usuario}");
                return Results.Ok(new MobileEnviarResponse
                {
                    Sucesso = true,
                    Mensagem = destino == u.Usuario ? "Ticket assumido." : "Ticket transferido para " + destino + "."
                });
            });

            // Enviar mensagem de texto (vai para o WhatsApp pelo mesmo fluxo do HELP)
            api.MapPost("/tickets/{id:int}/mensagens", async (
                int id, MobileEnviarRequest req, HttpContext http,
                TicketService ts, AdminService admin) =>
            {
                var u = Contexto(http);
                if (string.IsNullOrWhiteSpace(req.Texto))
                    return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Digite uma mensagem." });

                TicketChamadoDModel? respondida = null;
                if (req.CodRespondida is > 0)
                {
                    respondida = new TicketChamadoDModel
                    {
                        Codigo = req.CodRespondida.Value,
                        Anotacao = req.TextoRespondida ?? "",
                        Usuario = req.UsuarioRespondida ?? ""
                    };
                }

                return await EnviarAsync(id, u, req.Texto.Trim(), req.Interna, respondida, null, ts, admin);
            });

            // Enviar imagem/arquivo (com legenda opcional)
            api.MapPost("/tickets/{id:int}/arquivo", async (
                int id, HttpContext http,
                [FromForm] IFormFile arquivo, [FromForm] string? legenda,
                TicketService ts, AdminService admin) =>
            {
                var u = Contexto(http);
                if (arquivo == null || arquivo.Length == 0)
                    return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Arquivo vazio." });
                if (arquivo.Length > 10 * 1024 * 1024)
                    return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Arquivo maior que 10 MB." });

                var ms = new MemoryStream();
                await arquivo.CopyToAsync(ms);
                ms.Position = 0;

                var browserFile = new BrowserFileFromStream(ms,
                    string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
                    Path.GetFileName(arquivo.FileName));

                return await EnviarAsync(id, u, (legenda ?? "").Trim(), false, null, browserFile, ts, admin);
            })
            .DisableAntiforgery();

            // Enviar áudio gravado no celular (mesmo formato do gravador da tela Ticket: Opus, salvo como OGG)
            api.MapPost("/tickets/{id:int}/audio", async (
                int id, HttpContext http,
                [FromForm] IFormFile audio, [FromForm] bool? interna,
                TicketService ts, AdminService admin) =>
            {
                var u = Contexto(http);
                if (audio == null || audio.Length == 0)
                    return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Áudio vazio." });
                if (audio.Length > 16 * 1024 * 1024)
                    return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Áudio muito longo." });

                using var ms = new MemoryStream();
                await audio.CopyToAsync(ms);

                return await EnviarAsync(id, u, "", interna == true, null, null, ts, admin, ms.ToArray());
            })
            .DisableAntiforgery();

            // Push: registra/remove o celular para receber notificações
            api.MapPost("/push/inscrever", async (MobilePushInscricaoRequest req, HttpContext http, MobileRepository repo) =>
            {
                var u = Contexto(http);
                if (string.IsNullOrWhiteSpace(req.Endpoint) || string.IsNullOrWhiteSpace(req.P256dh) || string.IsNullOrWhiteSpace(req.Auth))
                    return Results.BadRequest();

                await repo.SalvarInscricaoAsync(u.CodEmp, u.Usuario, req);
                return Results.Ok();
            });

            api.MapPost("/push/cancelar", async (MobilePushCancelarRequest req, MobileRepository repo) =>
            {
                if (!string.IsNullOrWhiteSpace(req.Endpoint))
                    await repo.RemoverInscricaoAsync(req.Endpoint);
                return Results.Ok();
            });

            // Financeiro (Baixa a Receber / Cobrança) — MobileFinanceiroEndpoints.cs
            api.MapMobileFinanceiro();
        }

        // ───────────────────────────── Envio ─────────────────────────────

        private static async Task<IResult> EnviarAsync(
            int id, MobileUsuarioContexto u, string texto, bool interna,
            TicketChamadoDModel? respondida,
            Microsoft.AspNetCore.Components.Forms.IBrowserFile? arquivo,
            TicketService ts, AdminService admin,
            byte[]? audio = null)
        {
            if (!await admin.UsuarioTemPermissaoAsync(u.Usuario, "TICKETS_RESPONDER"))
                return Results.Json(new MobileEnviarResponse { Mensagem = "Você não tem permissão para responder tickets." }, statusCode: 403);

            var ticket = await ts.BuscarChamadoAsync(id);
            if (ticket == null)
                return Results.NotFound(new MobileEnviarResponse { Mensagem = "Ticket não encontrado." });

            if (ticket.CodSituacao == 3)
                return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Ticket finalizado. Reabra pelo HELP para responder." });

            // Mesma regra da tela Ticket: sem cliente vinculado não salva
            if (ticket.CodCliente <= 0)
                return Results.BadRequest(new MobileEnviarResponse { Mensagem = "Vincule um cliente a este ticket pelo HELP antes de responder." });

            // Ticket na fila (NOVO/sem responsável): quem responde pelo celular assume, como no WhatsApp
            if (UsuariosAutomaticos.Contains((ticket.Usuario ?? "").Trim()))
                ticket.Usuario = u.Usuario;

            var result = await ts.SalvarAlteracoesAsync(
                ticket,
                texto,
                u.Usuario,
                arquivo,
                audioBytes: audio,
                audioMimeType: audio == null ? null : "audio/ogg; codecs=opus",
                audioFileName: audio == null ? null : $"audio_mobile_{DateTime.Now:yyyyMMdd_HHmmss}.ogg",
                mensagemRespondida: respondida,
                mensagemInterna: interna);

            if (!result.Sucesso)
                return Results.BadRequest(new MobileEnviarResponse { Mensagem = result.Mensagem });

            await ts.SalvarCabecalhoTicketAsync(ticket, u.Usuario);

            return Results.Ok(new MobileEnviarResponse { Sucesso = true, Mensagem = "OK" });
        }

        // ───────────────────────────── Helpers ─────────────────────────────

        internal static MobileUsuarioContexto Contexto(HttpContext http) =>
            (MobileUsuarioContexto)http.Items[ItemUsuario]!;

        /// <summary>Quantas mensagens a conversa abre (as anteriores vêm ao rolar para cima).</summary>
        private const int LimitePadrao = 50;

        private static IResult Falha(string mensagem, int status = 400) =>
            Results.Json(new MobileEnviarResponse { Sucesso = false, Mensagem = mensagem }, statusCode: status);

        private static async Task<(bool Responder, bool AlterarStatus)> PermissoesAsync(AdminService admin, string usuario)
        {
            var responder = await admin.UsuarioTemPermissaoAsync(usuario, "TICKETS_RESPONDER");
            var status = await admin.UsuarioTemPermissaoAsync(usuario, "TICKETS_ALTERAR_STATUS");
            return (responder, status);
        }

        private static async Task<List<string>> ListarAtendentesAsync(TicketService ts) =>
            (await ts.ListarUsuariosAsync())
                .Select(x => (x ?? "").Trim().ToUpperInvariant())
                .Where(x => x.Length > 0 && !UsuariosAutomaticos.Contains(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

        /// <summary>Mesma lógica de "lido" da tela Ticket do HELP.</summary>
        private static async Task MarcarLidoAsync(TicketChamadoCModel t, MobileUsuarioContexto u, TicketService ts)
        {
            if (t.Novo == "S")
            {
                var temResponsavel = !UsuariosAutomaticos.Contains((t.Usuario ?? "").Trim());
                if (temResponsavel)
                    await ts.MarcarComoLidoSuporteAsync(t.Codigo, u.Usuario);

                if (!string.IsNullOrWhiteSpace(t.TelefoneWhatsApp)
                    && t.CodInstanciaWhatsApp.HasValue && t.CodInstanciaWhatsApp.Value > 0)
                {
                    _ = ts.MarcarLidoNoWhatsAppAsync(t.Codigo, t.CodInstanciaWhatsApp.Value);
                }
            }

            await ts.MarcarComoAbertoAsync(t.Codigo);
        }

        private static async Task<MobileTicketDto> MontarTicketAsync(
            TicketChamadoCModel t, MobileUsuarioContexto u, bool podeResponder, bool podeAlterarStatus,
            TicketService ts, MobileRepository repo)
        {
            var nome = t.CodCliente > 0 ? await ts.BuscarNomeCliente(t.CodCliente) : "";
            if (string.IsNullOrWhiteSpace(nome) || nome == "Cliente não encontrado")
                nome = MobileRepository.FormatarTelefone(t.TelefoneWhatsApp ?? "");

            return new MobileTicketDto
            {
                Codigo = t.Codigo,
                Nome = nome.Trim(),
                Telefone = t.TelefoneWhatsApp ?? "",
                FotoUrl = t.FotoClienteUrl,
                Assunto = t.Assunto ?? "",
                Responsavel = (t.Usuario ?? "").Trim(),
                CodSituacao = t.CodSituacao,
                Situacao = await repo.DescricaoSituacaoAsync(u.CodEmp, t.CodSituacao),
                CodCliente = t.CodCliente,
                SentimentoEmoji = t.SentimentoEmoji ?? "",
                PodeResponder = podeResponder,
                PodeAlterarStatus = podeAlterarStatus,
                Finalizado = t.CodSituacao == 3,
                SemCliente = t.CodCliente <= 0,
                TemWhatsApp = !string.IsNullOrWhiteSpace(t.TelefoneWhatsApp)
            };
        }

        private static readonly string[] ExtensoesImagem = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

        private static MobileMensagemDto ParaDto(TicketChamadoDModel m)
        {
            var status = m.StatusEnvioWhatsApp ?? "";
            var usuario = (m.Usuario ?? "").Trim();
            var texto = m.Anotacao ?? "";

            var nomeArquivo = m.NomeImagem;
            var refArquivo = !string.IsNullOrWhiteSpace(nomeArquivo) ? nomeArquivo! : (m.CaminhoImagem ?? "");
            var ext = Path.GetExtension(refArquivo.Split('?')[0]).ToLowerInvariant();
            var ehImagem = string.IsNullOrEmpty(ext) || ExtensoesImagem.Contains(ext);

            return new MobileMensagemDto
            {
                Codigo = m.Codigo,
                Texto = texto,
                DataHora = m.DataHora,
                Usuario = usuario,
                DoCliente = m.EnvioCliente == "S",
                Interna = m.Interno == "S",
                Automatica = status is "saudacao_auto" or "aguarde_auto"
                             || usuario.Equals("IA", StringComparison.OrdinalIgnoreCase)
                             || usuario.Equals("BOT", StringComparison.OrdinalIgnoreCase),
                Sistema = texto.StartsWith("Assunto alterado de \"")
                          || texto.StartsWith("✅ Ticket aprovado")
                          || texto.StartsWith("❌ Ticket recusado"),
                Excluida = m.FoiExcluida,
                Alterada = m.FoiAlterado,
                Status = status,
                ImagemUrl = string.IsNullOrWhiteSpace(m.CaminhoImagem) ? null : m.CaminhoImagem,
                NomeArquivo = nomeArquivo,
                ArquivoEhImagem = ehImagem,
                AudioUrl = m.AudioUrl,
                Transcricao = string.IsNullOrWhiteSpace(m.TranscricaoAudio) ? null : m.TranscricaoAudio,
                VideoUrl = string.IsNullOrWhiteSpace(m.VideoUrl) ? null : m.VideoUrl,
                CodRespondida = m.CodMensagemRespondida,
                TextoRespondida = string.IsNullOrWhiteSpace(m.TextoMensagemRespondida) ? null : m.TextoMensagemRespondida,
                UsuarioRespondida = string.IsNullOrWhiteSpace(m.UsuarioMensagemRespondida) ? null : m.UsuarioMensagemRespondida
            };
        }
    }
}
