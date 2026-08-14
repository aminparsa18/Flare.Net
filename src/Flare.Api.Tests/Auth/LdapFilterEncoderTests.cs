using Flare.Api.Auth;
using Xunit;

namespace Flare.Api.Tests.Auth;

public class LdapFilterEncoderTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("bob.smith", "bob.smith")]
    public void Escape_LeavesOrdinaryUsernamesUnchanged(string input, string expected)
    {
        Assert.Equal(expected, LdapFilterEncoder.Escape(input));
    }

    [Fact]
    public void Escape_EscapesWildcard()
    {
        Assert.Equal(@"alice\2a", LdapFilterEncoder.Escape("alice*"));
    }

    [Fact]
    public void Escape_EscapesParentheses_PreventingFilterInjection()
    {
        // Without escaping, this would inject a second clause into
        // "(&(objectClass=user)(sAMAccountName={0}))" rather than matching literally.
        var input = "*)(uid=*))(|(uid=*";
        var escaped = LdapFilterEncoder.Escape(input);

        Assert.DoesNotContain("(", escaped.Replace(@"\28", ""));
        Assert.DoesNotContain(")", escaped.Replace(@"\29", ""));
        Assert.Equal(@"\2a\29\28uid=\2a\29\29\28|\28uid=\2a", escaped);
    }

    [Fact]
    public void Escape_EscapesBackslash()
    {
        Assert.Equal(@"domain\5cuser", LdapFilterEncoder.Escape(@"domain\user"));
    }

    [Fact]
    public void Escape_EscapesNulByte()
    {
        Assert.Equal(@"a\00b", LdapFilterEncoder.Escape("a\0b"));
    }
}
