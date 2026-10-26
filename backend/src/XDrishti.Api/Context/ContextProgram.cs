using Microsoft.Extensions.Options;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Context;

/// <summary>The process entry of the <c>context</c> verb (P3-01d): the generic host of the other verbs (no endpoints, no broker vault key, not started, logs to stderr) and <see cref="ContextCommand"/>. Ctrl-C becomes a cancellation.</summary>
internal static class ContextProgram
{
    public static async Task<int> RunProcessAsync(IReadOnlyList<string> args)
    {
        using var cancel = new CancellationTokenSource();
        var interrupt = new SimulateProgram.InterruptCounter(cancel);
        ConsoleCancelEventHandler handler = (_, e) => e.Cancel = interrupt.Pressed();
        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(args, SimulateConsole.Process(), null, null, cancel.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        SimulateConsole console,
        IReadOnlyDictionary<string, string?>? overrides,
        Action<IServiceCollection>? configure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);

        IHost host;
        try
        {
            host = SimulateHost.Create(overrides, configure, "xd-context");
        }
        catch (Exception failure) when (failure is InvalidOperationException or OptionsValidationException or ArgumentException or AggregateException)
        {
            await console.Error.WriteLineAsync("the configuration is wrong: " + SimulateCommand.Describe(failure));
            await console.Error.WriteAsync(ContextOptions.Usage);
            return SimulateCommand.UsageError;
        }

        using (host)
        {
            return await ContextCommand.RunAsync(args, host.Services, console, cancellationToken);
        }
    }
}
