using System.Text.Json;
using Flare.Identity.SourceLinks;

namespace Flare.Api.Source;

/// <summary>One commit in a compare range, as the repo host reports it.</summary>
public sealed record HostCommit(string Sha, string Message, string Author, DateTimeOffset? Date);

/// <summary>
/// The commits of a compare range. <see cref="ChangedPaths"/> is the union of files the range changed when the
/// host returned it in full (null = unknown, so every commit has to be inspected).
/// </summary>
public sealed record HostCompare(IReadOnlyList<HostCommit> Commits, int Total, IReadOnlySet<string>? ChangedPaths);

/// <summary>
/// Reads the compare and commit-file responses of the three repo hosts, and ranks commits by how much of the
/// failing stack they touch. Pure (JSON in, records out), so it is unit-tested without HTTP.
/// See docs-internal/adr/0263-suspect-commits.md.
/// </summary>
public static class SuspectCommitParser
{
    /// <summary>A host that returns the range's changed files only up to this many is trusted as complete.</summary>
    private const int CompleteFileListBelow = 100;

    public static HostCompare ParseCompare(SourceLinkProvider provider, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var commits = new List<HostCommit>();

        switch (provider)
        {
            case SourceLinkProvider.GitHub:
            {
                foreach (var c in Array(root, "commits"))
                {
                    var commit = c.TryGetProperty("commit", out var inner) ? inner : default;
                    var author = commit.ValueKind == JsonValueKind.Object && commit.TryGetProperty("author", out var a) ? a : default;
                    commits.Add(new HostCommit(Str(c, "sha"), FirstLine(Str(commit, "message")), Str(author, "name"), Date(author, "date")));
                }

                var files = Array(root, "files").Select(f => Str(f, "filename")).Concat(Array(root, "files").Select(f => Str(f, "previous_filename")));
                var total = root.TryGetProperty("total_commits", out var t) && t.TryGetInt32(out var n) ? n : commits.Count;
                return new HostCompare(commits, total, Complete(Array(root, "files").Count(), files));
            }
            case SourceLinkProvider.GitLab:
            {
                foreach (var c in Array(root, "commits"))
                {
                    commits.Add(new HostCommit(Str(c, "id"), FirstLine(Str(c, "title")), Str(c, "author_name"), Date(c, "authored_date")));
                }

                var diffs = Array(root, "diffs").ToList();
                var truncated = root.TryGetProperty("compare_timeout", out var timeout) && timeout.ValueKind == JsonValueKind.True;
                var files = diffs.Select(d => Str(d, "new_path")).Concat(diffs.Select(d => Str(d, "old_path")));
                return new HostCompare(commits, commits.Count, truncated ? null : Complete(diffs.Count, files));
            }
            default:
            {
                foreach (var c in Array(root, "value"))
                {
                    var author = c.TryGetProperty("author", out var a) ? a : default;
                    commits.Add(new HostCommit(Str(c, "commitId"), FirstLine(Str(c, "comment")), Str(author, "name"), Date(author, "date")));
                }

                // The batch endpoint lists commits only - no per-range file list - so every commit is inspected.
                return new HostCompare(commits, commits.Count, null);
            }
        }
    }

    /// <summary>The repo-relative paths one commit changed (renames contribute both names).</summary>
    public static IReadOnlySet<string> ParseCommitPaths(SourceLinkProvider provider, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (provider)
        {
            case SourceLinkProvider.GitHub:
                foreach (var f in Array(root, "files"))
                {
                    Add(paths, Str(f, "filename"));
                    Add(paths, Str(f, "previous_filename"));
                }

                break;
            case SourceLinkProvider.GitLab:
                foreach (var f in root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : [])
                {
                    Add(paths, Str(f, "new_path"));
                    Add(paths, Str(f, "old_path"));
                }

                break;
            default:
                foreach (var change in Array(root, "changes"))
                {
                    Add(paths, change.TryGetProperty("item", out var item) ? Str(item, "path") : "");
                    Add(paths, Str(change, "sourceServerItem"));
                }

                break;
        }

        return paths;
    }

    /// <summary>
    /// Commits that touched at least one of <paramref name="framePaths"/> (top stack frame first), best first: a
    /// commit scores the sum of its matched frames' weights, the throw site weighing most; ties go to the newest.
    /// </summary>
    public static IReadOnlyList<(HostCommit Commit, IReadOnlyList<string> Matched)> Rank(
        IReadOnlyList<HostCommit> commits,
        IReadOnlyDictionary<string, IReadOnlySet<string>> changedBySha,
        IReadOnlyList<string> framePaths)
    {
        var scored = new List<(HostCommit Commit, IReadOnlyList<string> Matched, int Score)>();
        foreach (var commit in commits)
        {
            if (!changedBySha.TryGetValue(commit.Sha, out var changed))
            {
                continue;
            }

            var matched = new List<string>();
            var score = 0;
            for (var i = 0; i < framePaths.Count; i++)
            {
                if (changed.Contains(framePaths[i]))
                {
                    matched.Add(framePaths[i]);
                    score += framePaths.Count - i;
                }
            }

            if (matched.Count > 0)
            {
                scored.Add((commit, matched, score));
            }
        }

        return [.. scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Commit.Date).Select(s => (s.Commit, s.Matched))];
    }

    private static IReadOnlySet<string>? Complete(int fileCount, IEnumerable<string> files) =>
        fileCount < CompleteFileListBelow ? files.Where(f => f.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

    private static void Add(HashSet<string> paths, string path)
    {
        var trimmed = path.TrimStart('/');
        if (trimmed.Length > 0)
        {
            paths.Add(trimmed);
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Str(element, name), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }
}
