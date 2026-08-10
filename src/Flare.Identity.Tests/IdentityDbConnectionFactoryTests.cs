using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Identity.Tests;

public class IdentityDbConnectionFactoryTests
{
    [Fact]
    public async Task Constructor_CreatesMissingParentDirectories_ForTheConfiguredDbPath()
    {
        // Mirrors Flare.AppHost's local-dev .data/identity/ path, which doesn't exist
        // until something creates it - SQLite itself only ever creates the .db file, not
        // its parent directory (see IdentityDbConnectionFactory's remarks).
        var root = Path.Combine(Path.GetTempPath(), $"flare-identity-dir-test-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "nested", "flare-identity.db");
        Assert.False(Directory.Exists(Path.GetDirectoryName(dbPath)));

        try
        {
            var factory = new IdentityDbConnectionFactory(Options.Create(new IdentityOptions { DbPath = dbPath }));

            Assert.True(Directory.Exists(Path.GetDirectoryName(dbPath)));

            // The directory existing isn't enough on its own to prove the connection
            // string is usable - actually open it, same as every real caller would.
            await using var connection = await factory.OpenAsync();
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
