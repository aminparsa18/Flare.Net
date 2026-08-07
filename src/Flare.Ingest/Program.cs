using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Flare.Ingest.Sinks;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Two Kestrel listeners in one process, matching OTLP's conventional ports:
//   4317 - gRPC (cleartext HTTP/2, no ALPN negotiation needed since the protocol is pinned per-listener)
//   4318 - HTTP/1.1, both application/x-protobuf and application/json bodies on POST /v1/logs
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(4317, o => o.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(4318, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc();

builder.Services.Configure<LogEventPipelineOptions>(
    builder.Configuration.GetSection(LogEventPipelineOptions.SectionName));

// Redis: durable buffer the pipeline writes to (RedisStreamLogEventSink) and reads from
// (ClickHouseFlushWorker). ClickHouse: batched insert destination. Connection names must
// match the resource names Flare.AppHost references onto this project.
builder.AddRedisClient(connectionName: "redis");
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

builder.Services.AddSingleton<ILogEventSink, RedisStreamLogEventSink>();
builder.Services.AddSingleton<IClickHouseLogEventWriter, ClickHouseLogEventWriter>();
builder.Services.AddHostedService<ClickHouseFlushWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<OtlpGrpcLogsService>();
app.MapOtlpHttpLogsEndpoint();

app.Run();