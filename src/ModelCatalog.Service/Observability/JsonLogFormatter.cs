using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ModelCatalog.Service.Observability;

/// <summary>
/// The estate's log contract (architecture-guideline.md §7): structured JSON to stdout with
/// <c>ts</c>, <c>level</c>, <c>service</c>, <c>event</c> and <c>request_id</c>. Written here
/// rather than taken from a logging library because the contract names its fields exactly, and
/// no library emits those five spellings without being told to anyway.
///
/// <c>event</c> comes from the <c>EventName</c> on each <c>LoggerMessage.Define</c> site, which
/// is what makes "never invent a second spelling of an existing event" structural rather than a
/// rule someone has to remember.
///
/// Never written here: the refresh API key, or any request header carrying it. This service
/// holds no visitor data — its query parameters are provider and modality filters — so §7's
/// coordinate rule does not bite, and the Caddy site block uses the plain access log for the
/// same reason.
///
/// Copied rather than imported, per §7: a shared package costs a repository, a release and a
/// version pin to distribute a file that has changed once.
/// </summary>
public sealed class JsonLogFormatter(IOptions<JsonLogOptions> options)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "estate-json";

    private static readonly JsonWriterOptions WriterOptions = new() { SkipValidation = true };

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter
    )
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString(
                "ts",
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            );
            json.WriteString("level", Level(logEntry.LogLevel));
            json.WriteString("service", options.Value.Service);

            // The stable identifier queries filter on. Falls back to the logger's category so a
            // line from a framework component is still filterable, just less precisely.
            json.WriteString(
                "event",
                string.IsNullOrEmpty(logEntry.EventId.Name)
                    ? logEntry.Category
                    : logEntry.EventId.Name
            );

            json.WriteString("request_id", FindRequestId(scopeProvider) ?? "-");
            json.WriteString("message", message);

            if (logEntry.Exception is { } exception)
            {
                json.WriteString("error", exception.GetType().FullName);
                json.WriteString("error_message", exception.Message);

                // A sixth field, beyond §7's five. Serilog's console sink rendered {Exception}
                // with the frames, and dropping them made a feed failure undiagnosable from the
                // collector — type and message alone do not say where a fetch died. §7 names the
                // fields a line must carry, not the ones it may not.
                if (exception.StackTrace is { Length: > 0 } stack)
                    json.WriteString("stack", stack);
            }

            json.WriteEndObject();
        }

        textWriter.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>
    /// The correlation id, put on a scope by the request middleware or by the sync job.
    /// Innermost scope wins, so a nested one can narrow it.
    /// </summary>
    private static string? FindRequestId(IExternalScopeProvider? scopeProvider)
    {
        string? found = null;
        scopeProvider?.ForEachScope(
            (scope, _) =>
            {
                if (scope is not IEnumerable<KeyValuePair<string, object?>> pairs)
                    return;

                foreach (
                    var pair in pairs.Where(p =>
                        string.Equals(p.Key, RequestId.Key, StringComparison.Ordinal)
                        && p.Value is not null
                    )
                )
                {
                    found = pair.Value!.ToString();
                }
            },
            state: (object?)null
        );

        return found;
    }

    /// <summary>The contract's four levels; anything finer is reported as debug.</summary>
    private static string Level(LogLevel level) =>
        level switch
        {
            LogLevel.Critical or LogLevel.Error => "error",
            LogLevel.Warning => "warn",
            LogLevel.Information => "info",
            _ => "debug",
        };
}
