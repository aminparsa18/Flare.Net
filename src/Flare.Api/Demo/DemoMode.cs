using System.Security.Claims;
using Flare.Identity.Users;
using Microsoft.Extensions.Options;

namespace Flare.Api.Demo;

/// <summary>
/// Runtime state of demo mode (ADR-0278): whether it is on, and the principal a request without a
/// session is given. The principal is set once at startup by <see cref="DemoProvisioner"/>, after
/// the demo account exists, and is only ever read afterwards.
/// </summary>
public sealed class DemoMode(IOptions<DemoOptions> options)
{
    /// <summary>Marks the synthetic demo principal, so the write block and <c>/api/auth/me</c> can tell
    /// it apart from someone who signed into a real Viewer account.</summary>
    public const string ClaimType = "flare:demo";

    private const string AuthenticationType = "FlareDemo";

    public bool Enabled => options.Value.Enabled;

    public DemoOptions Options => options.Value;

    public ClaimsPrincipal? Principal { get; private set; }

    internal void SetUser(User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, nameof(UserRole.Viewer)),
                new Claim(ClaimType, "true"),
            ],
            AuthenticationType);
        Principal = new ClaimsPrincipal(identity);
    }

    public static bool IsDemo(ClaimsPrincipal principal) => principal.HasClaim(c => c.Type == ClaimType);
}
