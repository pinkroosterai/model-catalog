using Quartz;

namespace ModelCatalog.Service.Jobs;

/// <summary>
/// How far apart two runs of a cron expression are, used to publish
/// <c>feed_expected_interval_seconds</c>.
///
/// Derived from the expression the scheduler itself parses rather than hardcoded, because
/// `ModelRegistry:SyncCron` is configurable and architecture-guideline.md §7 treats the published
/// interval as the feed's contract — "a feed that starts running less often becomes a rule nobody
/// updates" is exactly what a hardcoded 86400 would produce.
///
/// For an irregular schedule — weekdays only, say — consecutive gaps differ, so this is the
/// *next* interval rather than a constant. Still better than a number that contradicts the
/// expression outright.
/// </summary>
public static class CronInterval
{
    public static double? SecondsBetweenRuns(string cron, DateTimeOffset from)
    {
        if (string.IsNullOrWhiteSpace(cron) || !CronExpression.IsValidExpression(cron))
            return null;

        var expression = new CronExpression(cron);
        if (expression.GetNextValidTimeAfter(from) is not { } first)
            return null;
        if (expression.GetNextValidTimeAfter(first) is not { } second)
            return null;

        return (second - first).TotalSeconds;
    }
}
