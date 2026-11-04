using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Flare.Benchmarks.Export;

// `export <dir>` writes the fixture bytes the TypeScript benchmarks read; anything else is
// BenchmarkDotNet's own command line (--filter, --job, --artifacts, ...). scripts/bench.sh
// drives both - see this project's README.
if (args is ["export", var directory])
{
    FixtureExporter.Export(directory);
    return;
}

// Wide parameter column: BenchmarkDotNet otherwise abbreviates values over 20 characters
// (`trace(...)hetic [21]`) in the exported Parameters too, which the report matches on.
var config = DefaultConfig.Instance
    .AddExporter(JsonExporter.Brief)
    .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(64));
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args, config);
