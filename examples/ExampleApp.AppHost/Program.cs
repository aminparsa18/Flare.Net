var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

builder.AddProject<Projects.ExampleApp_LogGenerator>("log-generator")
    .WithOtlpEndpoint(flare)
    .WaitFor(flare);

builder.Build().Run();
