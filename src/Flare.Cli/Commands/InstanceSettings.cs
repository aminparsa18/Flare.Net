using System.ComponentModel;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Shared `-n|--name` option every command accepts to target a named Flare instance
/// instead of the default one at <c>~/.flare/</c>. There's no global-option mechanism in
/// Spectre.Console.Cli, so this is the mechanism: every command's own <c>Settings</c>
/// extends this instead of <see cref="CommandSettings"/> directly.
/// </summary>
internal class InstanceSettings : CommandSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("Target a named instance (see `flare instances list`) instead of the default ~/.flare/ one. Omit for the default instance.")]
    public string? InstanceName { get; init; }
}
