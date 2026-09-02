using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace EvolutCRM.Services
{
    public class EmailService
    {
        private readonly string _baseUrl;
        private readonly string _smtp;
        private readonly int _porta;
        private readonly string _usuario;
        private readonly string _senha;

        public EmailService(IConfiguration config)
        {
            _smtp = config["Email:Smtp"] ?? "smtp.gmail.com";
            _porta = int.Parse(config["Email:Porta"] ?? "587");
            _usuario = config["Email:Usuario"] ?? "";
            _senha = config["Email:Senha"] ?? "";
            _baseUrl = config["App:BaseUrl"] ?? "https://help.evoluttech.com";
        }

        // ─────────────────────────────────────────────
        // Verificação de e-mail ao cadastrar usuário
        // ─────────────────────────────────────────────
        public async Task EnviarVerificacaoEmailAsync(string destinatario, string nomeUsuario, string token)
        {
            var link = $"{_baseUrl}/verificar-email?token={token}";

            var corpo = $@"
                <div style='font-family:Segoe UI,sans-serif;max-width:520px;margin:0 auto;padding:32px 24px;'>
                    <div style='background:linear-gradient(135deg,#0d47a1,#1976d2);border-radius:14px 14px 0 0;padding:28px 32px;'>
                        <img src='https://help.evoluttech.com/images/logo-evoluttech.png'
                             alt='EvolutTech' style='height:40px;display:block;' />
                    </div>
                    <div style='background:#f8fbff;border:1px solid #d7e6f7;border-top:none;border-radius:0 0 14px 14px;padding:32px;'>
                        <h2 style='margin:0 0 8px;color:#172033;font-size:1.3rem;'>Verifique seu e-mail</h2>
                        <p style='color:#64748b;font-size:.95rem;line-height:1.6;margin:0 0 24px;'>
                            Olá, <strong>{nomeUsuario}</strong>! Seu usuário foi cadastrado no <strong>EvolutTech HELP</strong>.
                            Clique no botão abaixo para verificar seu e-mail e ativar o acesso.
                        </p>
                        <a href='{link}'
                           style='display:inline-block;padding:14px 32px;background:linear-gradient(135deg,#0f766e,#0d47a1);
                                  color:#fff;border-radius:10px;text-decoration:none;font-weight:700;font-size:.95rem;'>
                            Verificar e-mail
                        </a>
                        <p style='margin:24px 0 0;color:#94a3b8;font-size:.78rem;line-height:1.5;'>
                            Este link expira em <strong>24 horas</strong>. Se você não solicitou este cadastro, ignore este e-mail.<br/>
                            Ou copie e cole o link no navegador:<br/>
                            <span style='color:#1976d2;word-break:break-all;'>{link}</span>
                        </p>
                    </div>
                    <p style='text-align:center;margin-top:16px;color:#cbd5e1;font-size:.74rem;'>EvolutTech &copy; {DateTime.Now.Year}</p>
                </div>";

            await EnviarAsync(destinatario, "Verifique seu e-mail — EvolutTech HELP", corpo);
        }

        // ─────────────────────────────────────────────
        // Código 2FA a cada login
        // ─────────────────────────────────────────────
        public async Task EnviarCodigo2FAAsync(string destinatario, string nomeUsuario, string codigo)
        {
            var corpo = $@"
                <div style='font-family:Segoe UI,sans-serif;max-width:520px;margin:0 auto;padding:32px 24px;'>
                    <div style='background:linear-gradient(135deg,#0d47a1,#1976d2);border-radius:14px 14px 0 0;padding:28px 32px;'>
                        <img src='https://help.evoluttech.com/images/logo-evoluttech.png'
                             alt='EvolutTech' style='height:40px;display:block;' />
                    </div>
                    <div style='background:#f8fbff;border:1px solid #d7e6f7;border-top:none;border-radius:0 0 14px 14px;padding:32px;'>
                        <h2 style='margin:0 0 8px;color:#172033;font-size:1.3rem;'>Código de verificação</h2>
                        <p style='color:#64748b;font-size:.95rem;line-height:1.6;margin:0 0 24px;'>
                            Olá, <strong>{nomeUsuario}</strong>! Use o código abaixo para concluir o login no <strong>EvolutTech HELP</strong>.
                        </p>
                        <div style='text-align:center;margin:0 0 24px;'>
                            <span style='display:inline-block;padding:18px 40px;background:#fff;border:2px solid #d7e6f7;
                                         border-radius:14px;font-size:2.2rem;font-weight:900;letter-spacing:.3em;
                                         color:#0d47a1;font-family:monospace;'>
                                {codigo}
                            </span>
                        </div>
                        <p style='margin:0;color:#94a3b8;font-size:.78rem;line-height:1.5;'>
                            Este código expira em <strong>10 minutos</strong>.<br/>
                            Se você não tentou fazer login, troque sua senha imediatamente.
                        </p>
                    </div>
                    <p style='text-align:center;margin-top:16px;color:#cbd5e1;font-size:.74rem;'>EvolutTech &copy; {DateTime.Now.Year}</p>
                </div>";

            await EnviarAsync(destinatario, "Seu código de acesso — EvolutTech HELP", corpo);
        }

        // ─────────────────────────────────────────────
        // Método base de envio — reutilizável
        // ─────────────────────────────────────────────
        public async Task EnviarAsync(string destinatario, string assunto, string corpoHtml)
        {
            var mensagem = new MimeMessage();
            mensagem.From.Add(new MailboxAddress("EvolutTech HELP", _usuario));
            mensagem.To.Add(MailboxAddress.Parse(destinatario));
            mensagem.Subject = assunto;

            mensagem.Body = new BodyBuilder { HtmlBody = corpoHtml }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_smtp, _porta, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_usuario, _senha);
            await client.SendAsync(mensagem);
            await client.DisconnectAsync(true);
        }
    }
}