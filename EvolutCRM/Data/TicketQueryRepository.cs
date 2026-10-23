using EvolutCRM.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace EvolutCRM.Data;

/// <summary>Texto do ticket (e os prints) que a IA lê para gerar o insight.</summary>
public sealed record TicketParaInsight(string Texto, List<string> Imagens);

public sealed class TicketQueryRepository
{
    private readonly string _cs;

    // Quem não é atendente de verdade (mensagens automáticas)
    private static readonly HashSet<string> UsuariosAutomaticos = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "WHATSAPP", "CLIENTE", "BOT", "IA", "SISTEMA", "NOVO"
    };

    public TicketQueryRepository(IConfiguration config)
    {
        _cs = config.GetConnectionString("Connection")
              ?? throw new InvalidOperationException("ConnectionStrings:Connection não encontrada no appsettings.json");
    }

    /// <summary>Compatibilidade: só o texto.</summary>
    public async Task<string> BuildTicketBaseTextAsync(int ticketId, int maxHistoricoChars = 14000)
        => (await MontarParaInsightAsync(ticketId, 0, maxHistoricoChars, incluirImagens: false)).Texto;

    /// <summary>
    /// Monta o ticket inteiro para a IA:
    /// - cabeçalho com cliente, assunto, situação e versão;
    /// - conversa do início ao fim, marcando Cliente / Atendente / Nota interna;
    /// - áudio pela transcrição; prints numerados ([imagem 1]) e enviados junto (até 3);
    /// - sem mensagens automáticas e apagadas;
    /// - se passar do limite, guarda o começo (onde está o problema) e o fim (onde está a solução).
    /// </summary>
    public async Task<TicketParaInsight> MontarParaInsightAsync(int ticketId, int codEmp, int maxChars = 14000, bool incluirImagens = true)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        // 1) Cabeçalho
        int empresa;
        string assunto, situacao, cliente, versao;
        await using (var cmd = new SqlCommand(@"
SELECT TOP 1 T.CodEmp,
       ISNULL(T.Assunto, ''),
       ISNULL(S.Descricao, ''),
       ISNULL(NULLIF(LTRIM(RTRIM(C.Apelido)), ''), ISNULL(C.Nome, '')),
       ISNULL(T.Versao, '')
FROM TicketChamadoC T
LEFT JOIN TicketSituacaoCRM S ON S.CodEmp = T.CodEmp AND S.Codigo = T.CodSituacao
LEFT JOIN Cliente C          ON C.CodEmp = T.CodEmp AND C.Codigo = T.CodCliente
WHERE T.Codigo = @Id
  AND (@CodEmp = 0 OR T.CodEmp = @CodEmp);", conn))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = ticketId;
            cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = codEmp;
            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await rd.ReadAsync())
                throw new InvalidOperationException($"TicketChamadoC não encontrado. Codigo={ticketId}");
            empresa = rd.GetInt32(0);
            assunto = Normalize(rd.GetString(1));
            situacao = Normalize(rd.GetString(2));
            cliente = Normalize(rd.GetString(3));
            versao = Normalize(rd.GetString(4));
        }

        // 2) Conversa inteira, em ordem
        var msgs = new List<(int Codigo, DateTime Data, string Usuario, string Texto, string EnvioCliente,
                             string Interno, string Transcricao, bool TemImagem, string NomeImagem, bool TemAudio)>();
        await using (var cmd = new SqlCommand(@"
SELECT D.Codigo, D.DataHora,
       LTRIM(RTRIM(ISNULL(D.Usuario, ''))),
       ISNULL(D.Anotacao, ''),
       ISNULL(D.EnvioCliente, 'N'),
       ISNULL(D.Interno, 'N'),
       ISNULL(D.TranscricaoAudio, ''),
       CASE WHEN D.Imagem IS NOT NULL THEN 1 ELSE 0 END,
       ISNULL(D.NomeImagem, ''),
       CASE WHEN D.Audio IS NOT NULL THEN 1 ELSE 0 END
FROM TicketChamadoD D
WHERE D.CodTicketChamadoC = @Id
  AND D.CodEmp = @CodEmp
  AND ISNULL(D.MensagemExcluida, 'N') <> 'S'
  AND ISNULL(D.StatusWhatsApp, '') NOT IN ('ia_processing', 'saudacao_auto', 'aguarde_auto')
ORDER BY D.DataHora, D.Codigo;", conn))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = ticketId;
            cmd.Parameters.Add("@CodEmp", SqlDbType.Int).Value = empresa;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                msgs.Add((rd.GetInt32(0), rd.GetDateTime(1), rd.GetString(2), rd.GetString(3), rd.GetString(4),
                          rd.GetString(5), rd.GetString(6), rd.GetInt32(7) == 1, rd.GetString(8), rd.GetInt32(9) == 1));
        }

        // 3) Quais prints vão junto (os 3 últimos)
        var codigosImagens = incluirImagens
            ? msgs.Where(m => m.TemImagem && ImagemIA.PareceImagem(m.NomeImagem)).Select(m => m.Codigo).TakeLast(3).ToList()
            : new List<int>();

        var imagens = new List<string>();
        var numeroImagem = new Dictionary<int, int>();
        foreach (var cod in codigosImagens)
        {
            await using var cmd = new SqlCommand("SELECT Imagem FROM TicketChamadoD WHERE Codigo = @Cod", conn);
            cmd.Parameters.Add("@Cod", SqlDbType.Int).Value = cod;
            var dataUrl = await ImagemIA.ParaDataUrlAsync(await cmd.ExecuteScalarAsync() as byte[]);
            if (dataUrl == null) continue;
            imagens.Add(dataUrl);
            numeroImagem[cod] = imagens.Count;
        }

        // 4) Linhas da conversa
        var linhas = new List<string>();
        foreach (var m in msgs)
        {
            var ehCliente = m.EnvioCliente == "S";
            var ehNota = !ehCliente && m.Interno == "S";
            if (!ehCliente && !ehNota && UsuariosAutomaticos.Contains(m.Usuario))
                continue; // mensagem automática

            var autor = ehCliente ? "Cliente"
                      : ehNota ? $"Nota interna ({m.Usuario})"
                      : $"Atendente ({m.Usuario})";

            var partes = new List<string>();
            if (m.TemImagem)
            {
                if (numeroImagem.TryGetValue(m.Codigo, out var n)) partes.Add($"[imagem {n} anexada]");
                else if (ImagemIA.PareceImagem(m.NomeImagem)) partes.Add("[imagem]");
                else partes.Add($"[arquivo: {m.NomeImagem}]");
            }
            if (m.TemAudio)
                partes.Add(string.IsNullOrWhiteSpace(m.Transcricao) ? "[áudio sem transcrição]" : "[áudio] " + Normalize(m.Transcricao));
            if (!string.IsNullOrWhiteSpace(m.Texto))
                partes.Add(Normalize(m.Texto));
            else if (!m.TemAudio && !string.IsNullOrWhiteSpace(m.Transcricao))
                partes.Add("[áudio] " + Normalize(m.Transcricao));

            if (partes.Count == 0) continue;
            linhas.Add($"[{m.Data:dd/MM HH:mm}] {autor}: {Trunc(string.Join(" ", partes), 1200)}");
        }

        // 5) Limite: começo (o problema) + fim (a solução)
        var conversa = string.Join("\n", linhas);
        if (conversa.Length > maxChars)
        {
            var orcamentoInicio = (int)(maxChars * 0.35);
            var inicio = new List<string>();
            var usados = 0;
            var i = 0;
            for (; i < linhas.Count && usados + linhas[i].Length < orcamentoInicio; i++)
            {
                inicio.Add(linhas[i]);
                usados += linhas[i].Length + 1;
            }

            var fim = new List<string>();
            var restante = maxChars - usados - 80;
            var j = linhas.Count - 1;
            for (; j >= i && restante - linhas[j].Length > 0; j--)
            {
                fim.Insert(0, linhas[j]);
                restante -= linhas[j].Length + 1;
            }

            var omitidas = j - i + 1;
            conversa = string.Join("\n", inicio)
                     + $"\n[... {omitidas} mensagem(ns) do meio omitida(s) ...]\n"
                     + string.Join("\n", fim);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"TICKET: {ticketId}");
        if (cliente.Length > 0) sb.AppendLine($"CLIENTE: {cliente}");
        if (assunto.Length > 0) sb.AppendLine($"ASSUNTO: {Trunc(assunto, 180)}");
        if (situacao.Length > 0) sb.AppendLine($"SITUAÇÃO: {situacao}");
        if (versao.Length > 0) sb.AppendLine($"VERSÃO DO SISTEMA: {versao}");
        sb.AppendLine();
        sb.AppendLine("CONVERSA (Cliente = quem pediu ajuda; Atendente = suporte EvolutTech; Nota interna = anotação do suporte que o cliente não vê):");
        sb.AppendLine(linhas.Count == 0 ? "(vazio)" : conversa);

        return new TicketParaInsight(sb.ToString(), imagens);
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private static string Trunc(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}
