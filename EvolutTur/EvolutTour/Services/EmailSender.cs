using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

public class EmailSender
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUser;
    private readonly string _smtpPass;
    private readonly string _from;

    public EmailSender(string smtpHost, int smtpPort, string smtpUser, string smtpPass, string from)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _smtpUser = smtpUser;
        _smtpPass = smtpPass;
        _from = from;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        using var client = new SmtpClient(_smtpHost, _smtpPort)
        {
            Credentials = new NetworkCredential(_smtpUser, _smtpPass),
            EnableSsl = true
        };

        var mail = new MailMessage(_from, to, subject, body);

        await client.SendMailAsync(mail);
    }
}