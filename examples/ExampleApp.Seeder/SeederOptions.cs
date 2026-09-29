namespace ExampleApp.Seeder;

public sealed class SeederOptions
{
    public HashSet<string> Scenarios { get; } = [];
    public bool ShowHelp { get; private set; }
    public bool Clear { get; private set; }
    public bool Append { get; private set; }
    public bool DryRun { get; private set; }
    public int Minutes { get; private set; } = 60;
    public string Locale { get; private set; } = "en";
    public int? Seed { get; private set; }
    public Uri Otlp { get; private set; } = WithSlash(Env("FLARE_OTLP_HTTP_URL") ?? "http://localhost:4318");
    public Uri Api { get; private set; } = WithSlash(Env("FLARE_API_URL") ?? "http://localhost:8080");
    public string? Token { get; private set; } = Env("FLARE_TOKEN");
    public string? IngestKey { get; private set; } = Env("FLARE_INGEST_KEY");
    public Uri ClickHouse { get; private set; } = WithSlash(Env("CLICKHOUSE_URL") ?? "http://localhost:8123");
    public string ClickHouseUser { get; private set; } = Env("CLICKHOUSE_USER") ?? "default";
    public string ClickHousePassword { get; private set; } = Env("CLICKHOUSE_PASSWORD") ?? "flare";
    public string ClickHouseDatabase { get; private set; } = "clickhousedb";

    private static readonly string[] Locales = ["en", "ru", "zh-CN"];

    public static SeederOptions Parse(string[] args, IReadOnlySet<string> known)
    {
        var options = new SeederOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg != "all" && !known.Contains(arg))
                {
                    throw new ArgumentException($"Unknown scenario '{arg}'.");
                }

                options.Scenarios.Add(arg);
                continue;
            }

            var (name, inline) = arg.IndexOf('=') is var eq and > 0 ? (arg[..eq], arg[(eq + 1)..]) : (arg, null);
            string Value() => inline ?? (++i < args.Length ? args[i] : throw new ArgumentException($"{name} needs a value."));

            switch (name)
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--clear":
                    options.Clear = true;
                    break;
                case "--append":
                    options.Append = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--minutes":
                    options.Minutes = int.TryParse(Value(), out var minutes) && minutes is >= 5 and <= 1440
                        ? minutes
                        : throw new ArgumentException("--minutes must be 5-1440.");
                    break;
                case "--locale":
                    var locale = Value();
                    options.Locale = Locales.FirstOrDefault(l => l.Equals(locale, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"--locale must be one of {string.Join(", ", Locales)}.");
                    break;
                case "--seed":
                    options.Seed = int.TryParse(Value(), out var seed) ? seed : throw new ArgumentException("--seed must be an integer.");
                    break;
                case "--otlp":
                    options.Otlp = WithSlash(Value());
                    break;
                case "--api":
                    options.Api = WithSlash(Value());
                    break;
                case "--token":
                    options.Token = Value();
                    break;
                case "--ingest-key":
                    options.IngestKey = Value();
                    break;
                case "--clickhouse":
                    options.ClickHouse = WithSlash(Value());
                    break;
                case "--clickhouse-user":
                    options.ClickHouseUser = Value();
                    break;
                case "--clickhouse-password":
                    options.ClickHousePassword = Value();
                    break;
                case "--clickhouse-database":
                    options.ClickHouseDatabase = Value();
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{name}'.");
            }
        }

        if (options.Clear && options.Append)
        {
            throw new ArgumentException("--clear and --append don't mix.");
        }

        return options;
    }

    public static void PrintUsage(IEnumerable<Scenario> scenarios)
    {
        Console.WriteLine("""
            Usage: dotnet run --project examples/ExampleApp.Seeder -- <scenario>... [options]

            Posts backdated OTLP/JSON to Flare (and creates the dashboards, saved views and
            pipeline rules those pages show) for the last --minutes. Re-running a scenario
            replaces its previous data rather than adding to it.

            Scenarios ("all" = every one):
            """);
        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"  {scenario.Name,-12} {scenario.Description}");
        }

        Console.WriteLine("""

            Options:
              --minutes N              Window to backfill, ending now (default 60)
              --locale en|ru|zh-CN     Language for dashboard, saved view and pipeline rule names (default en)
              --clear                  Delete the scenarios' seeded data and objects, seed nothing
              --append                 Don't clear the previous run first (no ClickHouse access needed)
              --dry-run                Generate and count everything, send nothing
              --seed N                 Change the data's random shape (default: fixed per scenario)
              --otlp URL               Flare.Ingest OTLP/HTTP        (default http://localhost:4318, env FLARE_OTLP_HTTP_URL)
              --api URL                Flare.Api                     (default http://localhost:8080, env FLARE_API_URL)
              --token PAT              Personal access token, when Flare has auth on (env FLARE_TOKEN)
              --ingest-key KEY         Ingest API key, when ingest requires one (env FLARE_INGEST_KEY)
              --clickhouse URL         ClickHouse HTTP, for clearing (default http://localhost:8123, env CLICKHOUSE_URL)
              --clickhouse-user U      (default default, env CLICKHOUSE_USER)
              --clickhouse-password P  (default flare, env CLICKHOUSE_PASSWORD)
            """);
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static Uri WithSlash(string url) => new(url.EndsWith('/') ? url : url + "/");
}
