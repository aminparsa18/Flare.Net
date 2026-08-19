using System.Reflection;
using Flare.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("flare");

    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";
    config.SetApplicationVersion(version);

    config.AddCommand<StartCommand>("start")
        .WithDescription("Start the standing Flare stack (first run also initializes ~/.flare/).");
    config.AddCommand<StopCommand>("stop")
        .WithDescription("Stop the stack without removing data volumes - a pause, not a teardown.");
    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show the stack's current health/state.");
    config.AddCommand<OpenCommand>("open")
        .WithDescription("Open the dashboard in your default browser.");
    config.AddCommand<TailCommand>("tail")
        .WithDescription("Live-tail structured log events (filterable by service/level/trace/search).");
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Pull the latest images for the pinned tag and recreate containers. --tag <TAG> moves the pin itself first.");
    config.AddCommand<LogsCommand>("logs")
        .WithDescription("Show or follow container logs.");
    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Run read-only diagnostics against Docker and the stack.");
    config.AddCommand<DestroyCommand>("destroy")
        .WithDescription("Remove containers AND data volumes. Destructive - requires --yes.");
});

return await app.RunAsync(args);
