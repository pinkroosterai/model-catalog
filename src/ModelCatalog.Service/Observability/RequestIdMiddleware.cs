namespace ModelCatalog.Service.Observability;

/// <summary>
/// Gives every request an id and puts it on a logging scope, so all its lines carry the same
/// <c>request_id</c>. An incoming header wins, which is what lets a correlation id survive a hop
/// from another service — estate consumers call this service directly over <c>edge</c>.
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var id = context.Request.Headers[RequestId.Header].FirstOrDefault() is { Length: > 0 } given
            ? given
            : RequestId.New();

        context.Response.Headers[RequestId.Header] = id;
        using (RequestId.Begin(log, id))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
