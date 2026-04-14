using Microsoft.Extensions.Options;

namespace ModelCatalog.Service.Auth;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options, ILogger<ApiKeyMiddleware> logger)
{
    private static readonly string[] OpenPaths = ["/healthz", "/metrics", "/swagger"];
    private static readonly Action<ILogger, string, Exception?> LogAccepted =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "ApiKeyAccepted"),
            "API key accepted for consumer {Consumer}");

    public async Task Invoke(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (OpenPaths.Any(p => ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var supplied) ||
            supplied.Count != 1 ||
            options.Value.ApiKeys.FirstOrDefault(k => string.Equals(k.Key, supplied.ToString(), StringComparison.Ordinal)) is not { } entry)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Invalid or missing X-Api-Key header").ConfigureAwait(false);
            return;
        }

        LogAccepted(logger, entry.Name, null);
        await next(ctx).ConfigureAwait(false);
    }
}
