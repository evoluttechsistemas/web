using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EvolutCRM.Services;

public class EmailNotificacaoService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificacaoService> _logger;

    public EmailNotificacaoService(IConfiguration config, ILogger<EmailNotificacaoService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task EnviarNotificacaoNovoCurriculoAsync(string nomeCompleto, string area, string cidade, string estado)
    {
        try
        {
            var smtp = _config["Email:Smtp"]!;
            var porta = int.Parse(_config["Email:Porta"] ?? "587");
            var usuario = _config["Email:Usuario"]!;
            var senha = _config["Email:Senha"]!;
            var remetente = _config["Email:Remetente"] ?? usuario;

            var mensagem = new MimeMessage();
            mensagem.From.Add(new MailboxAddress("HELP – Banco de Talentos", remetente));
            var destinatarios = _config["Email:Destinatarios"] ?? "gabriel.evolut@gmail.com";
            foreach (var email in destinatarios.Split(',', StringSplitOptions.RemoveEmptyEntries))
                mensagem.To.Add(MailboxAddress.Parse(email.Trim()));
            mensagem.Subject = $"📋 Novo currículo recebido – {nomeCompleto}";

            var builder = new BodyBuilder
            {
                HtmlBody = MontarHtml(nomeCompleto, area, cidade, estado),
                TextBody = $"Novo currículo recebido de {nomeCompleto} ({area} · {cidade}/{estado}). Acesse o Banco de Talentos para visualizar."
            };

            mensagem.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtp, porta, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(usuario, senha);
            await client.SendAsync(mensagem);
            await client.DisconnectAsync(true);

            _logger.LogInformation("[BancoTalentos] E-mail de notificação enviado para {Candidato}", nomeCompleto);
        }
        catch (Exception ex)
        {
            // Loga mas não estoura — falha no e-mail não deve impedir o cadastro
            _logger.LogError(ex, "[BancoTalentos] Falha ao enviar e-mail de notificação para currículo de {Candidato}", nomeCompleto);
        }
    }

    private static string MontarHtml(string nome, string area, string cidade, string estado) => $"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head><meta charset="utf-8"/></head>
        <body style="margin:0;padding:0;background:#f4f6fb;font-family:'Segoe UI',Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6fb;padding:32px 16px;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0"
                     style="background:#fff;border-radius:14px;border:1px solid #e6e8f0;
                            box-shadow:0 4px 18px rgba(11,18,32,.07);overflow:hidden;">

                <!-- Cabeçalho -->
                <tr>
                  <td style="background:#2952e3;padding:24px 32px;">
                    <p style="margin:0;color:#a8bcff;font-size:11px;font-weight:700;
                               letter-spacing:.08em;text-transform:uppercase;">Help · Banco de talentos</p>
                    <h1 style="margin:6px 0 0;color:#fff;font-size:20px;font-weight:800;">
                      Novo currículo recebido
                    </h1>
                  </td>
                </tr>

                <!-- Corpo -->
                <tr>
                  <td style="padding:28px 32px 8px;">
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td style="background:#f7f8fc;border-radius:10px;padding:16px 20px;">
                          <p style="margin:0 0 4px;font-size:11px;font-weight:700;color:#8a91a3;
                                     text-transform:uppercase;letter-spacing:.04em;">Candidato</p>
                          <p style="margin:0;font-size:18px;font-weight:800;color:#0b1220;">{nome}</p>
                        </td>
                      </tr>
                    </table>

                    <table width="100%" cellpadding="0" cellspacing="0" style="margin-top:12px;">
                      <tr>
                        <td width="50%" style="padding-right:6px;">
                          <div style="background:#f7f8fc;border-radius:10px;padding:14px 16px;">
                            <p style="margin:0 0 3px;font-size:10.5px;font-weight:700;color:#8a91a3;
                                       text-transform:uppercase;letter-spacing:.04em;">Área de interesse</p>
                            <p style="margin:0;font-size:14px;font-weight:600;color:#0b1220;">{area}</p>
                          </div>
                        </td>
                        <td width="50%" style="padding-left:6px;">
                          <div style="background:#f7f8fc;border-radius:10px;padding:14px 16px;">
                            <p style="margin:0 0 3px;font-size:10.5px;font-weight:700;color:#8a91a3;
                                       text-transform:uppercase;letter-spacing:.04em;">Localização</p>
                            <p style="margin:0;font-size:14px;font-weight:600;color:#0b1220;">{cidade}/{estado}</p>
                          </div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>

                <!-- CTA -->
                <tr>
                  <td style="padding:20px 32px 32px;">
                    <p style="margin:0 0 16px;font-size:14px;color:#5b6478;line-height:1.6;">
                      Um novo currículo foi enviado pelo formulário online e está aguardando avaliação.
                      Acesse o <strong>Banco de Talentos</strong> no HELP para visualizar todos os dados e alterar o status da candidatura.
                    </p>
                    <a href="https://help.evoluttech.com/banco-talentos"
                       style="display:inline-block;background:#2952e3;color:#fff;font-weight:700;
                              font-size:14px;padding:13px 24px;border-radius:10px;text-decoration:none;">
                      Ver no Banco de Talentos →
                    </a>
                  </td>
                </tr>

                <!-- Rodapé -->
                <tr>
                  <td style="border-top:1px solid #f0f1f6;padding:16px 32px;">
                    <p style="margin:0;font-size:11.5px;color:#8a91a3;">
                      Esta mensagem foi gerada automaticamente pelo sistema HELP · EvolutTech.
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}