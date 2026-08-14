using Flare.Identity.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flare.Api.Auth;

/// <summary>
/// The single choke point behind Flare's opt-in auth model (see docs/auth.md and
/// Planning.md's "opt-in auth" entry): when <see cref="AuthSettings.Enabled"/> is
/// false, every authorization check anywhere in the app - the nine existing endpoint
/// groups' <c>RequireAuthorization()</c>/<c>RequireMember</c>/<c>RequireAdmin</c> in
/// <c>Program.cs</c> - short-circuits to allowed, regardless of what the policy actually
/// evaluated to. A fresh Flare instance therefore has zero login requirement anywhere
/// until an Admin turns this on from the <c>/auth</c> screen, with **zero changes**
/// needed to any of those nine endpoint files or their route-group declarations - the
/// same "wrap once, nothing downstream changes" property those groups' shared
/// <c>RequireAuthorization()</c> calls already established.
///
/// Wraps (does not replace) the framework's own <see cref="AuthorizationMiddlewareResultHandler"/> -
/// when <see cref="AuthSettings.Enabled"/> is true, this delegates to it and behaves
/// exactly as if this class didn't exist. Registered in <c>Program.cs</c> as a plain
/// <c>AddSingleton</c> *after* <c>AddAuthorizationBuilder()</c>, so it wins over the
/// framework's own <c>TryAddSingleton</c> default registration.
/// </summary>
public sealed class ConditionalAuthorizationMiddlewareResultHandler(IAuthSettingsStore authSettings) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var settings = await authSettings.GetAsync(context.RequestAborted);
        if (!settings.Enabled)
        {
            await next(context);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
