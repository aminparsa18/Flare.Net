using System.Text.Json.Nodes;
using static ExampleApp.Seeder.SeedContext;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// App sessions from a MAUI app on two releases, shaped like <c>Flare.Maui</c>'s output: one
/// <c>session.id</c> per launch, <c>navigation</c> and <c>HttpClient</c> spans, breadcrumbs, a
/// <c>user.id</c> on most sessions, and fatal <c>app.unhandled_exception</c> spans, some of them
/// native (ANR, SIGABRT). The newer release crashes about four times as often, so the Sessions
/// page's release-health table has something to compare.
/// </summary>
public sealed class MobileScenario : Scenario
{
    public override string Name => "mobile";

    public override string Description => "App sessions on two releases, with crashes, native crashes and users";

    private const string Service = "example-maui-app";

    private static readonly string[] Screens = ["//home", "//catalog", "//catalog/detail", "//cart", "//checkout", "//settings"];

    private static readonly (string Os, string Version, string Manufacturer, string Model)[] Devices =
    [
        ("android", "14", "Google", "Pixel 8"),
        ("android", "13", "samsung", "SM-S911B"),
        ("android", "12", "OnePlus", "LE2123"),
        ("ios", "18.1", "Apple", "iPhone16,2"),
        ("ios", "17.6", "Apple", "iPhone14,5"),
        ("ios", "18.0", "Apple", "iPad13,1"),
    ];

    public override void Generate(SeedContext c)
    {
        for (var n = 0; n < c.PerHour(150); n++)
        {
            var newRelease = c.Rng.NextDouble() < 0.4;
            var version = newRelease ? "2.2.0" : "2.1.0";
            var (os, osVersion, manufacturer, model) = c.Pick(Devices);
            var resource = c.Batch.Resource(
                ("service.name", Service), ("service.version", version), ("app.build", newRelease ? "220" : "210"),
                ("os.type", os), ("os.version", osVersion), ("device.manufacturer", manufacturer), ("device.model.identifier", model));

            var sessionId = Otlp.TraceId(c.Ids);
            var userId = c.Rng.NextDouble() < 0.8 ? $"user-{c.Rng.Next(1, 400)}" : null;
            var start = c.RandomTime(60);
            var length = (long)c.LogNormal(150, 0.7) * Otlp.NanosPerSecond;
            var crashRate = newRelease ? 0.06 : 0.015;
            var crashed = c.Rng.NextDouble() < crashRate;
            var end = start + length;
            if (end > c.NowNanos - 20 * Otlp.NanosPerSecond) end = c.NowNanos - 20 * Otlp.NanosPerSecond;

            JsonArray Tags(params (string Key, object Value)[] extra)
            {
                var tags = Otlp.Attrs([("session.id", sessionId), .. extra]);
                if (userId is not null) tags.Add(Otlp.Attr("user.id", userId));
                return tags;
            }

            Breadcrumb(c, resource, Tags, sessionId, start, "lifecycle", "foreground");
            var screenCount = c.Rng.Next(2, 6);
            for (var s = 0; s < screenCount; s++)
            {
                var t = start + (end - start) * (s + 1) / (screenCount + 1);
                var screen = c.Pick(Screens);
                c.Span(resource, "Flare.Maui", Otlp.TraceId(c.Ids), null, "navigation", KindInternal, t, 4,
                    Tags(("screen.name", screen), ("navigation.source", "Push")));
                Breadcrumb(c, resource, Tags, sessionId, t + 5 * Otlp.NanosPerMs, "tap", "Button");

                if (c.Rng.NextDouble() < 0.7)
                {
                    var failed = c.Rng.NextDouble() < (newRelease ? 0.08 : 0.03);
                    var traceId = Otlp.TraceId(c.Ids);
                    c.Span(resource, "System.Net.Http", traceId, null, "GET", KindClient, t + 40 * Otlp.NanosPerMs, c.LogNormal(180, 0.5),
                        Tags(("http.request.method", "GET"), ("server.address", "api.example.com"), ("http.response.status_code", failed ? 503 : 200)),
                        error: failed ? "Service Unavailable" : null);
                }
            }

            if (crashed) Crash(c, resource, Tags, end, newRelease, os);
            else Breadcrumb(c, resource, Tags, sessionId, end, "lifecycle", "background");
        }
    }

    private static void Breadcrumb(SeedContext c, string resource, Func<(string, object)[], JsonArray> tags, string sessionId, long time, string category, string message) =>
        c.Span(resource, "Flare.Maui", Otlp.TraceId(c.Ids), null, "breadcrumb", KindInternal, time, 0.1,
            tags([("breadcrumb.category", category), ("breadcrumb.message", message)]));

    private static void Crash(SeedContext c, string resource, Func<(string, object)[], JsonArray> tags, long time, bool newRelease, string os)
    {
        // Most crashes are managed exceptions; on the new release a third are native, found on the next launch.
        var native = newRelease && c.Rng.NextDouble() < 0.35;
        var (type, message, stack) = native
            ? os == "android"
                ? c.Rng.NextDouble() < 0.5
                    ? ("Native.Anr", "Input dispatching timed out", "main thread blocked in CartPage.Recalculate")
                    : ("Native.NativeCrash", "signal 6 (SIGABRT), code -1", "")
                : ("Native.NativeCrash", "Namespace SPRINGBOARD, Code 0x8badf00d", "")
            : c.Pick<(string, string, string)>(
            [
                ("System.NullReferenceException", "Object reference not set to an instance of an object.",
                    "   at ExampleApp.Maui.CartPage.OnAppearing() in CartPage.xaml.cs:line 31"),
                ("System.InvalidOperationException", "Deliberate crash from ExampleApp.Maui",
                    "   at ExampleApp.Maui.MainPage.OnCrashClicked(Object sender, EventArgs e) in MainPage.xaml.cs:line 96"),
            ]);

        var attributes = tags([("exception.escaped", true)]);
        if (native)
        {
            attributes.Add(Otlp.Attr("crash.native", true));
            attributes.Add(Otlp.Attr("crash.kind", type["Native.".Length..]));
        }

        var events = new JsonArray(new JsonObject
        {
            ["name"] = "exception",
            ["timeUnixNano"] = Otlp.Nanos(time),
            ["attributes"] = Otlp.Attrs(("exception.type", type), ("exception.message", message), ("exception.stacktrace", $"{type}: {message}\n{stack}")),
        });
        c.Span(resource, "Flare.Maui", Otlp.TraceId(c.Ids), null, "app.unhandled_exception", KindInternal, time, 1, attributes, error: message, events: events);
    }
}
