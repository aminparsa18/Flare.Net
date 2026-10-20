using Flare.Api.Model;

namespace Flare.Api.Errors;

/// <summary>What re-keying stored issues to a new grouping has to write.</summary>
/// <param name="Writes">Live rows to insert, already under their new Id and message.</param>
/// <param name="Tombstones">Ids of rows whose group no longer exists under that Id.</param>
public sealed record ErrorIssueRekeyPlan(IReadOnlyList<ErrorIssue> Writes, IReadOnlyList<string> Tombstones);

/// <summary>
/// Pure planner for re-keying <c>error_issues</c> when the grouping rules change (ADR-0259; migration 0087 did the same once
/// for the built-in normalization). Each stored issue's message is run through the new grouping; issues that land on the same
/// group collapse to the most recently updated one, which keeps its state under the group's new Id.
/// </summary>
public static class ErrorIssueRekey
{
    /// <param name="newMessages">New group message per issue <see cref="ErrorIssue.Id"/>. An issue with no entry keeps its message.</param>
    public static ErrorIssueRekeyPlan Plan(IReadOnlyList<ErrorIssue> stored, IReadOnlyDictionary<string, string> newMessages, DateTimeOffset now)
    {
        var writes = new List<ErrorIssue>();
        var tombstones = new List<string>();

        foreach (var group in stored.GroupBy(i => (i.ExceptionType, Message: newMessages.GetValueOrDefault(i.Id, i.ExceptionMessage))))
        {
            var newId = ErrorIssueFingerprint.Compute(group.Key.ExceptionType, group.Key.Message);
            var winner = group.OrderByDescending(i => i.UpdatedAt).First();

            tombstones.AddRange(group.Where(i => i.Id != newId).Select(i => i.Id));

            // A winner already stored under the group's Id (its message did not change) needs no write.
            if (winner.Id != newId)
            {
                writes.Add(winner with { Id = newId, ExceptionMessage = group.Key.Message, UpdatedAt = now });
            }
        }

        return new ErrorIssueRekeyPlan(writes, tombstones);
    }
}
