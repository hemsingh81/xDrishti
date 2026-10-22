using Microsoft.Extensions.Options;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The process entry of the <c>xd</c> verb (spec P5-07): the host of the other verbs (built, never started, logs to stderr) and <see cref="XdCommand"/>. Ctrl-C becomes a cancellation that also stops the run it waits for.</summary>
internal static class XdProgram
{
    public static async Task<int> RunProcessAsync(IReadOnlyList<string> args)
    {
        using var cancel = new CancellationTokenSource();
        var interrupt = new SimulateProgram.InterruptCounter(cancel);
        ConsoleCancelEventHandler handler = (_, e) => e.Cancel = interrupt.Pressed();
        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(args, SimulateConsole.Process(), null, null, XdCommand.DefaultPoll, cancel.Token);
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
        TimeSpan poll,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);

        IHost host;
        try
        {
            host = SimulateHost.Create(overrides, configure, "xd-cli");
        }
        catch (Exception failure) when (failure is InvalidOperationException or OptionsValidationException or ArgumentException or AggregateException)
        {
            await console.Error.WriteLineAsync("the configuration is wrong: " + SimulateCommand.Describe(failure));
            await console.Error.WriteAsync(XdOptions.Usage);
            return XdCommand.UsageError;
        }

        using (host)
        {
            return await XdCommand.RunAsync(args, host.Services, console, poll, cancellationToken);
        }
    }
}
