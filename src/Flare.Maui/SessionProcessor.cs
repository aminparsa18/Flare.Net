using System.Diagnostics;
using Activity = System.Diagnostics.Activity;
using OpenTelemetry;

namespace Flare.Maui;

/// <summary>Stamps one random <c>session.id</c> per process start on every span.</summary>
internal sealed class SessionProcessor(string sessionId) : BaseProcessor<Activity>
{
    public string SessionId { get; } = sessionId;

    public override void OnStart(Activity data) => data.SetTag("session.id", SessionId);
}
