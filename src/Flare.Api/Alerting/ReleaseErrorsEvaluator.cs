using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Evaluates an <see cref="AlertConditionKind.ReleaseErrors"/> rule: how many exception groups the worst
/// release deployed in the last <see cref="AlertRule.WindowSeconds"/> introduced. Shared by
/// <c>AlertEvaluationWorker</c> and the dry-run test endpoints. See
/// <c>docs-internal/adr/0186-release-error-alerting.md</c>.
/// </summary>
public static class ReleaseErrorsEvaluator
{
    /// <summary>The count the rule's threshold is compared with: 0 when nothing was deployed in the window.</summary>
    public static async Task<ReleaseErrorTally?> EvaluateAsync(IReleaseQueryService releases, ExceptionCountCondition condition, int windowSeconds, CancellationToken cancellationToken) =>
        await releases.FindWorstRecentAsync(condition.Filter.Services ?? [], TimeSpan.FromSeconds(windowSeconds), cancellationToken);
}
