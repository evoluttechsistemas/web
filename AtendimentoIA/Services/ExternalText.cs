using System.Text.RegularExpressions;

namespace EvolutTech.IA.Painel.Services;

public static class ExternalText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    public static string Clean(string? text, int limit = 3200)
    {
        var value = (text ?? "")[..Math.Min(text?.Length ?? 0, 24000)];
        value=Regex.Replace(value,@"[\p{Cc}-[\r\n\t]]","",RegexOptions.None,Timeout);
        value = Regex.Replace(value, @"-----BEGIN [^-]*(?:PRIVATE KEY|CERTIFICATE)-----[\s\S]*?(?:-----END [^-]+-----|$)", "[material criptográfico oculto]", RegexOptions.IgnoreCase, Timeout);
        value = Regex.Replace(value, @"(?i)\b(?:Server|Data Source|Initial Catalog)\s*=\s*[^\r\n\""<>]+", "[conexão oculta]", RegexOptions.None, Timeout);
        value = SensitiveText.Redact(value);
        value = Regex.Replace(value, @"(?im)^.*\b\w*(?:senha|password|token|secret|api_?key)\w*\s*=\s*""[^""]*"".*$", "[atribuição de credencial oculta]", RegexOptions.None, Timeout);
        value = Regex.Replace(value, @"\b(?:sk-[A-Za-z0-9_-]{12,}|github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9_]+)\b", "[credencial oculta]", RegexOptions.None, Timeout);
        value = Regex.Replace(value, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b|\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b", "[documento oculto]", RegexOptions.None, Timeout);
        value = Regex.Replace(value, @"(?<!\d)(?:\+55\s*)?\(?\d{2}\)?[ .-]?9?\d{4}[ .-]?\d{4}(?!\d)", "[telefone oculto]", RegexOptions.None, Timeout);
        value = Regex.Replace(value, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[endereço de rede oculto]", RegexOptions.None, Timeout);
        value = Regex.Replace(value, @"(?i)(?:[A-Z]:\\|\\\\)[^\s\""<>]+", "[caminho interno oculto]", RegexOptions.None, Timeout);
        return value.Length <= limit ? value : value[..limit] + " […]";
    }
    public static string Plain(string text) => Clean(System.Net.WebUtility.HtmlDecode(
        Regex.Replace(text, "<[^>]*>", " ", RegexOptions.None, Timeout)));
    public static string CustomerAnswer(string text)
    {
        var clean = Clean(text, 8000);
        clean = Regex.Replace(clean, @"\b(?:txb|txt|cmb|cbo|chk|btn|lbl|dgv|grd)[A-Z]\w*\b", "campo da tela", RegexOptions.None, Timeout);
        clean = Regex.Replace(clean, @"\b(?:método|metodo|rotina|função|funcao)\s+[`']?[a-z]\w*[A-Z]\w*(?:\(\))?[`']?", "operação do sistema", RegexOptions.None, Timeout);
        clean = Regex.Replace(clean, @"\b[a-z]\w*[A-Z]\w*\s*\(\)", "operação do sistema", RegexOptions.None, Timeout);
        clean = Regex.Replace(clean, @"(?i)\b[\w./\\-]+\.(?:cs|csproj|sln|config)\b", "[referência técnica interna]", RegexOptions.None, Timeout);
        clean = Regex.Replace(clean, @"\b(?:TicketChamadoC|TicketChamadoD|TicketAssistenteIA|CRMAnotacao|CRMC|ParametroDinamico|ControleMensalista|DocumentacaoTutorialBloco|DocumentacaoTutorial)\b", "configuração interna do sistema", RegexOptions.IgnoreCase, Timeout);
        clean = Regex.Replace(clean, @"\b(?:frm[A-Z]\w*|\w+_(?:Click|Load|Shown)|CodEmp|CodCliente)\b", "[referência interna]", RegexOptions.None, Timeout);
        return clean;
    }
}
