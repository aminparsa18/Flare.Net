using Flare.Maui;

namespace ExampleApp.Maui;

public partial class MainPage
{
    // Runs the scenario in FLARE_AUTORUN, a comma-separated list of steps, in place of Maestro on devices it cannot drive
    // (a physical iPhone). Each step calls what the matching button calls.
    async Task AutoRunAsync(string steps)
    {
        Console.WriteLine($"[example] autorun '{steps}' endpoint={MauiProgram.Endpoint} version={MauiProgram.Version}");
        await Task.Delay(1500); // first frame, app.start and the endpoint settle
        foreach (var step in steps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (step)
            {
                case "http": OnHttpClicked(null, EventArgs.Empty); await Task.Delay(1500); break;
                case "offline": OnOfflineClicked(null, EventArgs.Empty); await Task.Delay(4000); break;
                case "custom": OnCustomClicked(null, EventArgs.Empty); break;
                case "record": OnRecordClicked(null, EventArgs.Empty); break;
                case "release": OnReleaseErrorClicked(null, EventArgs.Empty); break;
                case "unhandled": OnUnhandledTaskClicked(null, EventArgs.Empty); await Task.Delay(1500); break;
                case "scrub": OnScrubClicked(null, EventArgs.Empty); break;
                case "flush": OnFlushClicked(null, EventArgs.Empty); await Task.Delay(2000); break;
                case "jank": await JankAsync(); break;
                case "push": await Shell.Current.GoToAsync(nameof(DetailPage)); await Task.Delay(1500); break;
                case "slow": await Shell.Current.GoToAsync(nameof(SlowPage)); await Task.Delay(1500); break;
                case "pop": await Shell.Current.GoToAsync(".."); await Task.Delay(500); break;
                case "wait6": await Task.Delay(6000); break;
                case "wait12": await Task.Delay(12000); break;
                // These end the process or block the UI thread, so they run on the main loop and not in this task.
                case "freeze": Dispatcher.Dispatch(() => OnFreezeClicked(null, EventArgs.Empty)); await Task.Delay(20000); break;
                case "crash": Dispatcher.Dispatch(() => OnCrashClicked(null, EventArgs.Empty)); await Task.Delay(5000); break;
                case "nativecrash": Dispatcher.Dispatch(() => OnNativeCrashClicked(null, EventArgs.Empty)); await Task.Delay(5000); break;
                default: Say($"Unknown autorun step '{step}'."); break;
            }
        }
        Say("Autorun done.");
    }
}
