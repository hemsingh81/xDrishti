using XDrishti.Execution.Host;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddExecution(builder.Configuration);

await builder.Build().RunAsync();
return 0;
