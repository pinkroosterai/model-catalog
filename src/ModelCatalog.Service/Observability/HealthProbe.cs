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
    /// Follows whatever the container was actually told to listen on.
    ///
    /// `ASPNETCORE_URLS` is checked first because that is ASP.NET Core's own precedence, and the
    /// aspnet base image ships `ASPNETCORE_HTTP_PORTS=8080` — reading PORTS first meant an
    /// operator who moved the server with URLS got a container that served correctly and was
    /// permanently unhealthy.
    ///
    /// Both are treated as absent when blank. An empty PORTS used to survive the `??` chain as
    /// the empty string, producing `http://127.0.0.1:/health`, which resolves to port 80 — where
    /// on this host Caddy answers 204 and the probe reported *healthy* while the service was
    /// somewhere else entirely. A false healthy is worse than a false unhealthy.
    /// </summary>
    private static string ProbeUrl()
    {
        var port =
            PortFromUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
            ?? FirstPort(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"))
            ?? "8080";

        return string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/health");
    }

    private static string? FirstPort(string? ports) =>
        string.IsNullOrWhiteSpace(ports) ? null : Digits(ports.Split(';')[0]);

    private static string? PortFromUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
            return null;

        // "http://+:8080" or "http://localhost:5000;https://localhost:5001" — first binding wins.
        var first = urls.Split(';')[0];
        var colon = first.LastIndexOf(':');
        return colon < 0 ? null : Digits(first[(colon + 1)..].TrimEnd('/'));
    }

    private static string? Digits(string? candidate)
    {
        candidate = candidate?.Trim();
        return !string.IsNullOrEmpty(candidate) && candidate.All(char.IsAsciiDigit)
            ? candidate
            : null;
    }
}
