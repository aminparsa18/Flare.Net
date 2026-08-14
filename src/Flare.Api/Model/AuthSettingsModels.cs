namespace Flare.Api.Model;

/// <summary>Response/request body for <c>GET</c>/<c>PUT /api/settings/auth</c> - the
/// consolidated <c>/auth</c> screen's umbrella switch.</summary>
public sealed record AuthSettingsDto
{
    public required bool Enabled { get; init; }

    public required bool LocalEnabled { get; init; }
}
