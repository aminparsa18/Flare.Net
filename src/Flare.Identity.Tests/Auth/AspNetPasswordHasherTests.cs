using Flare.Identity.Auth;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class AspNetPasswordHasherTests
{
    private readonly AspNetPasswordHasher _hasher = new();

    [Fact]
    public void VerifyPassword_ReturnsTrue_ForTheCorrectPassword()
    {
        var hash = _hasher.HashPassword("correct horse battery staple");

        Assert.True(_hasher.VerifyPassword(hash, "correct horse battery staple"));
    }

    [Fact]
    public void VerifyPassword_ReturnsFalse_ForTheWrongPassword()
    {
        var hash = _hasher.HashPassword("correct horse battery staple");

        Assert.False(_hasher.VerifyPassword(hash, "wrong password entirely"));
    }

    [Fact]
    public void HashPassword_NeverStoresThePasswordInPlainText()
    {
        var hash = _hasher.HashPassword("correct horse battery staple");

        Assert.DoesNotContain("correct horse battery staple", hash);
    }

    [Fact]
    public void HashPassword_ProducesADifferentHashEachTime_EvenForTheSamePassword()
    {
        // PasswordHasher<T> salts every hash - two hashes of the same password must not
        // be equal, even though both verify successfully against it.
        var first = _hasher.HashPassword("correct horse battery staple");
        var second = _hasher.HashPassword("correct horse battery staple");

        Assert.NotEqual(first, second);
    }
}
