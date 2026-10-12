using Flare.Api.Errors;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Errors;

// Mirrors the semantics of src/dashboard/src/lib/errors/ownership.ts (ADR-0195, ADR-0196).
public class ErrorOwnershipTests
{
    private static ErrorOwnershipRule Rule(ErrorOwnershipKind kind, string pattern, string owner) =>
        new() { Kind = kind, Pattern = pattern, Owner = owner };

    [Theory]
    [InlineData("payments*", "Payments-Api", true)]
    [InlineData("*timeout*", "System.TimeoutException", true)]
    [InlineData("pay", "payments", false)]
    [InlineData("a.c", "abc", false)]
    [InlineData("*", "", true)]
    public void Glob_is_case_insensitive_with_star_wildcards(string pattern, string value, bool expected) =>
        Assert.Equal(expected, ErrorOwnership.GlobMatches(pattern, value));

    [Theory]
    [InlineData("*.cs", "src/pay/Charge.cs", true)]          // no slash: any depth
    [InlineData("Charge.cs", "src/pay/Charge.cs", true)]
    [InlineData("/src/pay/", "src/pay/Charge.cs", true)]     // directory owns everything beneath
    [InlineData("src/pay", "src/pay/deep/X.cs", true)]
    [InlineData("src/pay/", "vendor/src/pay/X.cs", false)]   // slash anchors at the root
    [InlineData("src/*/X.cs", "src/pay/X.cs", true)]
    [InlineData("src/*/X.cs", "src/a/b/X.cs", false)]        // * stays inside a segment
    [InlineData("src/**/X.cs", "src/a/b/X.cs", true)]
    [InlineData("src/**/X.cs", "src/X.cs", true)]            // **/ is zero or more directories
    [InlineData("**/gen/*", "a/b/gen/X.cs", true)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    [InlineData("/", "anything/at/all.cs", true)]            // "/" owns the whole repo
    [InlineData("", "src/X.cs", false)]
    [InlineData("SRC/PAY/", "src/pay/X.cs", true)]
    public void Codeowners_patterns(string pattern, string path, bool expected) =>
        Assert.Equal(expected, ErrorOwnership.CodeownersMatches(pattern, path));

    [Fact]
    public void PathSuffixes_tries_every_trailing_run_and_drops_windows_drive_letters()
    {
        Assert.Equal(["ci/src/X.cs", "src/X.cs", "X.cs"], ErrorOwnership.PathSuffixes("/ci/src/X.cs"));
        Assert.Equal(["ci/src/X.cs", "src/X.cs", "X.cs"], ErrorOwnership.PathSuffixes(@"C:\ci\src\X.cs"));
        Assert.Empty(ErrorOwnership.PathSuffixes(""));
    }

    [Fact]
    public void Root_anchored_path_rule_matches_a_build_machine_path_through_its_suffixes()
    {
        var rules = new[] { Rule(ErrorOwnershipKind.Path, "src/pay/", "alice") };
        var hit = ErrorOwnership.OwnerFromRules(rules, "T", [], ["/home/ci/build/src/pay/Charge.cs"]);
        Assert.Equal("alice", hit?.Owner);
        Assert.Null(ErrorOwnership.OwnerFromRules(rules, "T", [], ["/home/ci/build/src/other/X.cs"]));
    }

    [Fact]
    public void First_matching_rule_wins_across_kinds()
    {
        var rules = new[]
        {
            Rule(ErrorOwnershipKind.ExceptionType, "*Timeout*", "bob"),
            Rule(ErrorOwnershipKind.Service, "pay*", "alice"),
        };
        Assert.Equal("bob", ErrorOwnership.OwnerFromRules(rules, "System.TimeoutException", ["payments"], [])?.Owner);
        Assert.Equal("alice", ErrorOwnership.OwnerFromRules(rules, "System.InvalidOperationException", ["payments"], [])?.Owner);
        Assert.Null(ErrorOwnership.OwnerFromRules(rules, "System.InvalidOperationException", ["orders"], []));
    }

    [Fact]
    public void Service_rule_matches_any_affected_service()
    {
        var rules = new[] { Rule(ErrorOwnershipKind.Service, "billing", "carol") };
        Assert.Equal("carol", ErrorOwnership.OwnerFromRules(rules, "T", ["web", "Billing"], [])?.Owner);
    }

    [Fact]
    public void Explicit_assignee_beats_a_rule()
    {
        var rules = new[] { Rule(ErrorOwnershipKind.Service, "*", "rule-owner") };
        Assert.Equal(new ErrorGroupOwner("dave", FromRule: false), ErrorOwnership.Resolve("dave", rules, "T", ["s"], []));
        Assert.Equal(new ErrorGroupOwner("rule-owner", FromRule: true), ErrorOwnership.Resolve("", rules, "T", ["s"], []));
        Assert.Null(ErrorOwnership.Resolve(null, [], "T", ["s"], []));
    }
}
