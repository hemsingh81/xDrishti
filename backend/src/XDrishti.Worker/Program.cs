using XDrishti.Application;
using XDrishti.Hosting;
using XDrishti.Infrastructure;
using XDrishti.Worker.Heartbeats;

var builder = Host.CreateApplicationBuilder(args);
builder.AddXDrishtiDefaults("xd-worker");

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddOptions<HeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<HeartbeatService>();

await builder.Build().RunAsync();
