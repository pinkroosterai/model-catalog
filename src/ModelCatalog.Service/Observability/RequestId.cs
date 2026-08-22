namespace ModelCatalog.Service.Observability;

/// <summary>
/// The correlation id that ties the lines of one request — or one sync run — together.
/// </summary>
public static class RequestId
{
    public const string Key = "request_id";

    /// <summary>The header a caller may set to continue an existing correlation id.</summary>
    public const string Header = "X-Request-Id";

    public static IDisposable? Begin(ILogger logger, string id)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.BeginScope(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [Key] = id }
        );
    }

    /// <summary>Short, readable, and not a UUID — these are read in a terminal.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>The longest inbound id accepted. Generous next to the 12 this service mints.</summary>
    private const int MaxLength = 64;

    /// <summary>
    /// Whether a caller-supplied id may be adopted. Bounded and restricted to characters that are
    /// safe in a response header and readable in a log.
    /// </summary>
    public static bool IsWellFormed(string? id) =>
        id is { Length: > 0 and <= MaxLength }
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
