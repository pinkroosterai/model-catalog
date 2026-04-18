using Quartz;

namespace ModelCatalog.Service.Jobs;

[DisallowConcurrentExecution]
public sealed class SyncJob(SyncPipeline pipeline) : IJob
{
    private static int _running;

    public static bool IsRunning => Volatile.Read(ref _running) != 0;

    public static bool TryBeginRun() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public static void EndRun() => Interlocked.Exchange(ref _running, 0);

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryBeginRun())
            return;
        try
        {
            await pipeline.RunAsync(context.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EndRun();
        }
    }
}
