using Flare.Identity.Auth;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class PasswordPolicyTests
{
    [Fact]
    public void Default_OnlyEnforcesEightCharacters()
    {
        var policy = new PasswordPolicy();

        Assert.NotNull(policy.Validate("short"));
        Assert.Null(policy.Validate("alllowercase"));
    }

    [Theory]
    [InlineData("alllowercase1!", "uppercase")]
    [InlineData("ALLUPPERCASE1!", "lowercase")]
    [InlineData("NoDigitsHere!!", "digit")]
    [InlineData("NoSymbolsHere12", "symbol")]
    public void RequiredClasses_AreEnforced_AndNamedInTheMessage(string password, string expectedWord)
    {
        var policy = new PasswordPolicy { RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSymbol = true };

        Assert.Contains(expectedWord, policy.Validate(password));
    }

    [Fact]
    public void CompliantPassword_Passes_AndMinLengthBelowOneIsClamped()
    {
        var strict = new PasswordPolicy { MinLength = 12, RequireUppercase = true, RequireDigit = true, RequireSymbol = true };
        Assert.Null(strict.Validate("Correct-Horse-9"));
        Assert.Contains("12", strict.Validate("Sh0rt!"));
        Assert.Null(new PasswordPolicy { MinLength = 0 }.Validate("x"));
    }
}
