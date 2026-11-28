using System.Text.RegularExpressions;

namespace Flare.Cli.Internal;

/// <summary>
/// The oldest image an instance's compose file can run (ADR-0285). A compose file with a
/// <c>clickhouse-migrate</c> service runs the api image as <c>migrate</c> and gives the other services no
/// <c>default</c> credential. The api image has had that command since 0.7.0. With an older image the job
/// starts the whole API and never exits, so ingest, api and alert-worker never start. Compose files written
/// by an older CLI have no job, migrate at service startup, and keep accepting any tag.
/// </summary>
internal static partial class ImageTagPolicy
{
    public const string MinimumMigrationJobTag = "0.7.0";

    /// <summary>
    /// True for a release tag older than 0.7.0 (<c>0.6.0</c>, <c>0.6</c>, <c>0.5.1-rc1</c>). <c>edge</c>,
    /// <c>latest</c>, <c>sha-…</c> and any other tag that isn't a version number count as new.
    /// </summary>
    public static bool IsOlderThanMigrationJob(string tag)
    {
        var match = ReleaseTag().Match(tag.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !int.TryParse(match.Groups["minor"].Value, out var minor))
        {
            return false;
        }

        return major == 0 && minor < 7;
    }

    /// <summary>True when the compose file at <paramref name="composeFilePath"/> runs migrations in the one-shot job.</summary>
    public static bool UsesMigrationJob(string composeFilePath) =>
        File.Exists(composeFilePath) && MigrateService().IsMatch(File.ReadAllText(composeFilePath));

    /// <summary>The reason <paramref name="tag"/> can't run this instance, or null when it can.</summary>
    public static string? Validate(FlareInstance instance, string tag)
    {
        if (!IsOlderThanMigrationJob(tag) || !UsesMigrationJob(instance.ComposeFilePath))
        {
            return null;
        }

        return $"FLARE_IMAGE_TAG {tag} is older than {MinimumMigrationJobTag}. This instance runs ClickHouse migrations "
            + $"in a clickhouse-migrate job, and the api image only has the `migrate` command from {MinimumMigrationJobTag}. "
            + $"Use `{instance.UpdateHint} --tag {MinimumMigrationJobTag}` (or newer, or edge).";
    }

    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)(\.\d+)?([-+].*)?$")]
    private static partial Regex ReleaseTag();

    [GeneratedRegex(@"(?m)^  clickhouse-migrate:\s*$")]
    private static partial Regex MigrateService();
}
