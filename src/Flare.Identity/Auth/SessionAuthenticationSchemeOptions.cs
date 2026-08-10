using Microsoft.AspNetCore.Authentication;

namespace Flare.Identity.Auth;

/// <summary>No scheme-specific options today - <see cref="AuthOptions"/> (cookie name/
/// lifetime/flags) is bound separately and injected into
/// <see cref="SessionAuthenticationHandler"/> directly, since those values are also
/// needed by the login endpoint (which isn't part of the auth *scheme*). This type
/// exists only because <c>AddScheme&lt;TOptions, THandler&gt;</c> requires one.</summary>
public sealed class SessionAuthenticationSchemeOptions : AuthenticationSchemeOptions;
