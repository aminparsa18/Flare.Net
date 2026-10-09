namespace Flare.Maui;

/// <summary>Device and app facts stamped on every signal. No device identifier is collected.</summary>
internal sealed record FlareDeviceInfo(
    string OsType,
    string OsVersion,
    string Manufacturer,
    string Model,
    string AppVersion,
    string AppBuild)
{
    internal IEnumerable<KeyValuePair<string, object>> ToResourceAttributes()
    {
        yield return new("os.type", OsType.ToLowerInvariant());
        yield return new("os.version", OsVersion);
        yield return new("device.manufacturer", Manufacturer);
        yield return new("device.model.identifier", Model);
        yield return new("app.build", AppBuild);
    }
}
