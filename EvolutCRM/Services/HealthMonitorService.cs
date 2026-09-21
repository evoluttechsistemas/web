using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EvolutCRM.Services
{
    // Services/HealthMonitorService.cs

    public enum LogSeverity
    {
        Info,
        Success,
        Warning,
        Error,
        Critical
    }

    public enum LogCategory
    {
        Tickets,
        CRM,
        SQL,
        Baileys,
        Sistema
    }

    public record HealthLogEntry(
        DateTime Timestamp,
        LogSeverity Severity,
        LogCategory Category,
        string Message,
        string? Detail = null,

        // Contexto do atendimento
        int? CodTicketChamadoC = null,
        int? CodTicketChamadoD = null,
        int? CodCliente = null,
        string? Cliente = null,
        string? TelefoneWhatsApp = null,
        string? MessageIdWhatsApp = null,
        string? InstanciaWhatsApp = null,

        // Informações da Exception
        string? ExceptionType = null,
        string? ExceptionMessage = null,
        string? StackTrace = null,

        // Local onde o log foi chamado
        string? SourceFile = null,
        string? SourceMember = null,
        int? SourceLine = null,

        // Local da Exception, quando disponível
        string? ExceptionFile = null,
        string? ExceptionMethod = null,
        int? ExceptionLine = null
    );

    public class HealthMonitorService
    {
        // Ring buffer: máx 500 por categoria, TTL 2 horas
        private const int MaxPerCategory = 500;
        private static readonly TimeSpan LogTtl = TimeSpan.FromHours(2);

        private readonly Dictionary<LogCategory, ConcurrentQueue<HealthLogEntry>> _logs = new();

        // Contadores em memória
        private long _msgsSent;
        private long _sendFailures;
        private long _slowQueries;
        private long _exceptions;

        private readonly Dictionary<string, (string Status, DateTime? ConnectedSince)> _baileysInstancias = new();

        // Evento para notificar o Blazor
        public event Action? OnNewLog;

        public HealthMonitorService()
        {
            foreach (LogCategory cat in Enum.GetValues<LogCategory>())
                _logs[cat] = new ConcurrentQueue<HealthLogEntry>();

            // Limpeza automática de TTL a cada 10min
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    PurgExpired();
                }
            });
        }


        // ============================================================
        // ADICIONA O LOG NA FILA
        // ============================================================

        private void AddEntry(HealthLogEntry entry, bool countAsException = true)
        {
            var queue = _logs[entry.Category];

            queue.Enqueue(entry);

            while (queue.Count > MaxPerCategory)
                queue.TryDequeue(out _);

            if (countAsException &&
    (entry.Severity == LogSeverity.Error ||
     entry.Severity == LogSeverity.Critical))
            {
                Interlocked.Increment(ref _exceptions);
            }

            OnNewLog?.Invoke();
        }


        // ============================================================
        // LOG GENÉRICO
        // ============================================================

        public void Log(
            LogCategory cat,
            LogSeverity sev,
            string message,
            string? detail = null,

            [CallerFilePath] string sourceFile = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLine = 0)
        {
            var entry = new HealthLogEntry(
                Timestamp: DateTime.Now,
                Severity: sev,
                Category: cat,
                Message: message,
                Detail: detail,

                SourceFile: GetFileName(sourceFile),
                SourceMember: sourceMember,
                SourceLine: sourceLine
            );

            AddEntry(entry);
        }


        // ============================================================
        // MENSAGEM ENVIADA
        // ============================================================

        public void LogMsgSent(
            LogCategory cat,
            string ticketOrCardId,
            string jid)
        {
            Interlocked.Increment(ref _msgsSent);

            Log(
                cat,
                LogSeverity.Success,
                $"Mensagem enviada — #{ticketOrCardId}",
                $"JID: {jid}"
            );
        }


        // ============================================================
        // FALHA NO ENVIO
        // ============================================================

        public void LogMsgFailed(
    LogCategory cat,
    string id,
    string reason,

    int? codTicketChamadoC = null,
    int? codTicketChamadoD = null,
    int? codCliente = null,
    string? cliente = null,
    string? telefoneWhatsApp = null,
    string? messageIdWhatsApp = null,
    string? instanciaWhatsApp = null,
    Exception? exception = null,

    [CallerFilePath] string sourceFile = "",
    [CallerMemberName] string sourceMember = "",
    [CallerLineNumber] int sourceLine = 0)
        {
            Interlocked.Increment(ref _sendFailures);

            var exceptionOrigin = GetExceptionOrigin(exception);

            var entry = new HealthLogEntry(
                Timestamp: DateTime.Now,
                Severity: LogSeverity.Error,
                Category: cat,

                Message: $"Envio falhou — #{id}",
                Detail: reason,

                CodTicketChamadoC: codTicketChamadoC,
                CodTicketChamadoD: codTicketChamadoD,
                CodCliente: codCliente,
                Cliente: cliente,
                TelefoneWhatsApp: telefoneWhatsApp,
                MessageIdWhatsApp: messageIdWhatsApp,
                InstanciaWhatsApp: instanciaWhatsApp,

                ExceptionType: exception?.GetType().FullName,
                ExceptionMessage: exception?.Message,
                StackTrace: exception?.ToString(),

                SourceFile: GetFileName(sourceFile),
                SourceMember: sourceMember,
                SourceLine: sourceLine,

                ExceptionFile: exceptionOrigin.File,
                ExceptionMethod: exceptionOrigin.Method,
                ExceptionLine: exceptionOrigin.Line
            );

            AddEntry(entry, countAsException: false); // ← não duplica no contador Exceptions
        }


        // ============================================================
        // QUERY LENTA
        // ============================================================

        public void LogSlowQuery(
            string queryName,
            long ms)
        {
            Interlocked.Increment(ref _slowQueries);

            Log(
                LogCategory.SQL,
                LogSeverity.Warning,
                $"Query lenta: {queryName} ({ms}ms)"
            );
        }


        // ============================================================
        // STATUS BAILEYS
        // ============================================================

        public void LogBaileysStatus(
    string instancia,   // ← ex: "5517991816997" ou "Instância #23"
    string status,
    bool connected,

    [CallerFilePath] string sourceFile = "",
    [CallerMemberName] string sourceMember = "",
    [CallerLineNumber] int sourceLine = 0)
        {
            lock (_baileysInstancias)
            {
                if (!_baileysInstancias.ContainsKey(instancia) || connected)
                {
                    _baileysInstancias[instancia] = (
                        status,
                        connected ? DateTime.Now : _baileysInstancias.GetValueOrDefault(instancia).ConnectedSince
                    );
                }
                else
                {
                    var atual = _baileysInstancias[instancia];
                    _baileysInstancias[instancia] = (status, atual.ConnectedSince);
                }
            }

            var entry = new HealthLogEntry(
                Timestamp: DateTime.Now,
                Severity: connected ? LogSeverity.Success : LogSeverity.Error,
                Category: LogCategory.Baileys,
                Message: $"Gateway [{instancia}]: {status}",

                SourceFile: GetFileName(sourceFile),
                SourceMember: sourceMember,
                SourceLine: sourceLine
            );

            AddEntry(entry);
        }

        public void LogException(
            LogCategory cat,
            Exception ex,
            string context,

            int? codTicketChamadoC = null,
            int? codTicketChamadoD = null,
            int? codCliente = null,
            string? cliente = null,
            string? telefoneWhatsApp = null,
            string? messageIdWhatsApp = null,
            string? instanciaWhatsApp = null,

            [CallerFilePath] string sourceFile = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLine = 0)
        {
            var exceptionOrigin = GetExceptionOrigin(ex);

            var entry = new HealthLogEntry(
                Timestamp: DateTime.Now,
                Severity: LogSeverity.Critical,
                Category: cat,

                Message: $"Exception em {context}: {ex.Message}",
                Detail: ex.Message,

                CodTicketChamadoC: codTicketChamadoC,
                CodTicketChamadoD: codTicketChamadoD,
                CodCliente: codCliente,
                Cliente: cliente,
                TelefoneWhatsApp: telefoneWhatsApp,
                MessageIdWhatsApp: messageIdWhatsApp,
                InstanciaWhatsApp: instanciaWhatsApp,

                ExceptionType: ex.GetType().FullName,
                ExceptionMessage: ex.Message,
                StackTrace: ex.ToString(),

                SourceFile: GetFileName(sourceFile),
                SourceMember: sourceMember,
                SourceLine: sourceLine,

                ExceptionFile: exceptionOrigin.File,
                ExceptionMethod: exceptionOrigin.Method,
                ExceptionLine: exceptionOrigin.Line
            );

            AddEntry(entry);
        }


        // ============================================================
        // BUSCA LOGS
        // ============================================================

        public IReadOnlyList<HealthLogEntry> GetLogs(
            LogCategory cat,
            LogSeverity? filter = null)
        {
            var all = _logs[cat]
                .OrderByDescending(x => x.Timestamp);

            return filter.HasValue
                ? all.Where(x => x.Severity == filter.Value).ToList()
                : all.ToList();
        }


        // ============================================================
        // SNAPSHOT
        // ============================================================

        public HealthSnapshot GetSnapshot()
        {
            List<BaileysInstanciaInfo> instancias;
            lock (_baileysInstancias)
            {
                instancias = _baileysInstancias
                    .Select(kv => new BaileysInstanciaInfo(
                        Numero: kv.Key,
                        Status: kv.Value.Status,
                        Uptime: kv.Value.ConnectedSince.HasValue
                            ? DateTime.Now - kv.Value.ConnectedSince.Value
                            : TimeSpan.Zero
                    ))
                    .ToList();
            }

            return new HealthSnapshot(
                MsgsSent: _msgsSent,
                SendFailures: _sendFailures,
                SlowQueries: _slowQueries,
                Exceptions: _exceptions,
                Instancias: instancias
            );
        }


        // ============================================================
        // LIMPAR
        // ============================================================

        public void ClearAll()
        {
            foreach (var q in _logs.Values)
                while (q.TryDequeue(out _)) { }

            Interlocked.Exchange(ref _msgsSent, 0);
            Interlocked.Exchange(ref _sendFailures, 0);
            Interlocked.Exchange(ref _slowQueries, 0);
            Interlocked.Exchange(ref _exceptions, 0);

            lock (_baileysInstancias)
                _baileysInstancias.Clear();

            OnNewLog?.Invoke();
        }
        private void PurgExpired()
        {
            var cutoff = DateTime.Now - LogTtl;

            foreach (var queue in _logs.Values)
            {
                var expired = queue.Where(x => x.Timestamp < cutoff).Count();

                for (int i = 0; i < expired; i++)
                    queue.TryDequeue(out _);
            }
        }


        // ============================================================
        // AUXILIARES
        // ============================================================

        private static string? GetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return System.IO.Path.GetFileName(path);
        }


        private static (
            string? File,
            string? Method,
            int? Line)
            GetExceptionOrigin(Exception? ex)
        {
            if (ex == null)
                return (null, null, null);

            try
            {
                var trace = new StackTrace(ex, true);
                var frames = trace.GetFrames();

                if (frames == null || frames.Length == 0)
                    return (null, null, null);

                // Prioriza um frame que tenha número da linha
                var frame = frames
                    .FirstOrDefault(x => x.GetFileLineNumber() > 0);

                // Se não houver linha, pega o primeiro frame
                frame ??= frames.FirstOrDefault();

                if (frame == null)
                    return (null, null, null);

                var method = frame.GetMethod();

                string? methodName = null;

                if (method != null)
                {
                    if (method.DeclaringType != null)
                    {
                        methodName =
                            $"{method.DeclaringType.FullName}.{method.Name}";
                    }
                    else
                    {
                        methodName = method.Name;
                    }
                }

                var line = frame.GetFileLineNumber();

                return (
                    File: GetFileName(frame.GetFileName()),
                    Method: methodName,
                    Line: line > 0 ? line : null
                );
            }
            catch
            {
                return (null, null, null);
            }
        }
    }


    public record BaileysInstanciaInfo(
    string Numero,
    string Status,
    TimeSpan Uptime
);

    public record HealthSnapshot(
        long MsgsSent,
        long SendFailures,
        long SlowQueries,
        long Exceptions,
        List<BaileysInstanciaInfo> Instancias
    );

}