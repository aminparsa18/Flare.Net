using Flare.Ingest.Patterns;

namespace Flare.Ingest.Tests.Patterns;

/// <summary>
/// Hand-written <see cref="ILogPatternMatcher"/> test double: always returns a fixed
/// <see cref="PatternMatch"/> and records every body it was asked to match. Same
/// plain-fake style as <c>Flare.Ingest.Tests.Pipeline.FakeClickHouseLogEventWriter</c> -
/// this repo has no mocking framework.
/// </summary>
public sealed class FakeLogPatternMatcher : ILogPatternMatcher
{
    private readonly List<string?> matchedBodies = [];

    public PatternMatch Result { get; set; } = new("fake-pattern-id", "fake <*> template");

    public IReadOnlyList<string?> MatchedBodies => matchedBodies;

    public PatternMatch Match(string? body)
    {
        matchedBodies.Add(body);
        return Result;
    }
}
