using Flare.Api.Endpoints;
using Flare.Api.Query;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Same connection name Flare.AppHost references onto Flare.Ingest - both projects read/
// write the same `clickhousedb.logs` table.
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILogQueryService, LogQueryService>();
builder.Services.AddOpenApi();

// Permissive CORS for every environment, not just dev: v1 has no auth story anywhere
// yet (Planning.md's roadmap lists it as a later item), so this isn't loosening the
// product's actual security posture - it's what lets the still-undecided dashboard SPA
// (Planning.md open question #1) call this API cross-origin once it exists, in exactly
// the self-hosted deployment this product ships as. Revisit once auth lands.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapLogsEndpoints();

app.Run();
