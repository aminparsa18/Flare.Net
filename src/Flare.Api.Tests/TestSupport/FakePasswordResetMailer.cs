using Flare.Api.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>Records sends instead of talking to SMTP - see <see cref="FakeUserStore"/>'s remarks.</summary>
internal sealed class FakePasswordResetMailer : IPasswordResetMailer
{
    public bool IsConfigured { get; set; } = true;

    public List<(string To, string Link)> Sent { get; } = [];

    public string BuildLink(string rawToken) => $"https://flare.test/set-password?token={rawToken}";

    public Task<bool> SendAsync(string to, string link, CancellationToken cancellationToken)
    {
        Sent.Add((to, link));
        return Task.FromResult(true);
    }
}
