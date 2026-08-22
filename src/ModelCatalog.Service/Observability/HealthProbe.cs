using System.Globalization;

namespace ModelCatalog.Service.Observability;

/// <summary>
/// The container healthcheck, run as <c>dotnet ModelCatalog.Service.dll --health</c>.
///
/// The runtime image carries no curl, and the estate's precedent (the assistant stack) is a flag
/// on the application itself rather than a tool added to the image just to populate a status
/// column.
///
/// This asks the running process over HTTP, which is what makes it a liveness check: reading
/// <c>snapshot.json</c> off disk instead would report healthy for a stopped container, and would
/// miss the failure this deployment is most exposed to — a process serving correctly from memory
/// while failing to persist.
///
/// It deliberately does not fail on staleness. A catalog three days old is still serving
/// correct-if-old data; that is `feed-stale-daily`'s job to report, and flapping the container
/// unhealthy for it would tell the operator something misleading. <c>/health</c> answers 503 only
/// when there is no snapshot to serve at all.
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync(new Uri(ProbeUrl())).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
#pragma warning disable CA1031 // any failure to reach ourselves is an unhealthy container
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Follows whatever the container was actually told to listen on, so the probe cannot drift
    /// from the server. Falls back to the .NET default for these images.
    /// </summary>
    private static string ProbeUrl()
    {
        var port =
            Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0]
            ?? PortFromUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
            ?? "8080";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{port.Trim()}/health"
        );
    }

    private static string? PortFromUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
            return null;

        // "http://+:8080" or "http://localhost:5000;https://localhost:5001" — first binding wins.
        var first = urls.Split(';')[0];
        var colon = first.LastIndexOf(':');
        if (colon < 0)
            return null;

        var candidate = first[(colon + 1)..].TrimEnd('/');
        return candidate.Length > 0 && candidate.All(char.IsAsciiDigit) ? candidate : null;
    }
}
