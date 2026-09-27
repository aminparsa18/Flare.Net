using System.Globalization;

namespace Flare.Api.Updates;

/// <summary>
/// A <c>MAJOR.MINOR.PATCH[-prerelease][+build]</c> version, optionally <c>v</c>-prefixed the
/// way Flare's release tags are (<c>v0.5.1</c>) - just enough of SemVer 2.0.0 to order a
/// running build against GitHub releases for the update notice (ADR-0068). Build metadata
/// is accepted and ignored for ordering, per the spec.
/// </summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemanticVersion>
{
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Parses <paramref name="text"/>, or returns false for anything that isn't a version -
    /// <c>dev</c>/<c>edge</c> builds, and other tag families like <c>flare-cli-v0.1.6</c>
    /// (only a bare <c>v</c> prefix is allowed).
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s[0] is 'v' or 'V')
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+');
        if (plus >= 0)
        {
            s = s[..plus];
        }

        string? prerelease = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = s[(dash + 1)..];
            s = s[..dash];
            if (prerelease.Length == 0 || prerelease.Split('.').Any(id => id.Length == 0))
            {
                return false;
            }
        }

        var parts = s.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>
    /// The highest stable (non-prerelease) version among <paramref name="tags"/>, with the tag
    /// it came from - the fallback when the repository has tags but no published GitHub
    /// Release. Tags that aren't versions are skipped.
    /// </summary>
    public static (string Tag, SemanticVersion Version)? HighestStableTag(IEnumerable<string> tags)
    {
        (string Tag, SemanticVersion Version)? best = null;
        foreach (var tag in tags)
        {
            if (TryParse(tag, out var version) && !version.IsPrerelease
                && (best is null || version.CompareTo(best.Value.Version) > 0))
            {
                best = (tag, version);
            }
        }

        return best;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A release outranks any prerelease of the same MAJOR.MINOR.PATCH.
        if (Prerelease is null || other.Prerelease is null)
        {
            return (Prerelease is null).CompareTo(other.Prerelease is null);
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    // SemVer 11.4: dot-separated identifiers left to right; numeric ones compare numerically
    // and rank below alphanumeric ones; a shorter prefix-equal list ranks lower.
    private static int ComparePrerelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftNumeric = TryParseNumber(left[i], out var l);
            var rightNumeric = TryParseNumber(right[i], out var r);
            var c = (leftNumeric, rightNumeric) switch
            {
                (true, true) => l.CompareTo(r),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i])
            };
            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static bool TryParseNumber(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
