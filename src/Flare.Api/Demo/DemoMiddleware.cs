namespace Flare.Api.Demo;

/// <summary>
/// The two request-time halves of demo mode (ADR-0278), registered between
/// <c>UseAuthentication()</c> and <c>UseAuthorization()</c> only while it is on:
/// <list type="number">
/// <item>A request with no valid session or token runs as the demo Viewer, so visitors never see a
/// login screen. Authorization then evaluates that principal like any other: Member/Admin routes
/// answer 403.</item>
/// <item>The demo Viewer may not change anything, even what a signed-in Viewer can (saved views,
/// dashboard pins, preferences, access tokens, sessions, password): any method other than
/// GET/HEAD/OPTIONS is refused unless the endpoint carries <see cref="AllowInDemoAttribute"/>.</item>
/// </list>
/// A real account signed in through <c>/login</c> is untouched by both.
/// </summary>
public sealed class DemoMiddleware(RequestDelegate next, DemoMode demo)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true && demo.Principal is { } principal)
        {
            context.User = principal;
        }

        if (DemoMode.IsDemo(context.User)
            && !IsReadMethod(context.Request.Method)
            && context.GetEndpoint() is { } endpoint
            && endpoint.Metadata.GetMetadata<AllowInDemoAttribute>() is null)
        {
            await Results.Problem(
                title: "Read-only demo",
                detail: "This is a public read-only demo. Changes are disabled.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsReadMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
