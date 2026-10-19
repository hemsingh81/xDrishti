using Microsoft.Extensions.Options;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.CostSheets;

/// <summary>
/// The process entry of the <c>cost-sheet</c> verb (spec P3-03f, F4): builds the generic host of the <c>simulate</c> verb (no arguments bound to configuration, no <c>AddApi</c>, no broker vault key, validated on build, not started, logs to
/// stderr) and runs <see cref="CostSheetCommand"/>. Ctrl-C becomes a cancellation. Kept apart from <c>Program.cs</c> so that it can be tested.
/// </summary>
internal static class CostSheetProgram
{
    /// <summary>Runs the verb for the process: <paramref name="args"/> are the arguments after <c>cost-sheet</c>.</summary>
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

    /// <summary>Builds the host and runs the verb; a host that cannot be built is a configuration error (exit 2).</summary>
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
            host = SimulateHost.Create(overrides, configure, "xd-cost-sheet");
        }
        catch (Exception failure) when (failure is InvalidOperationException or OptionsValidationException or ArgumentException or AggregateException)
        {
            await console.Error.WriteLineAsync("the configuration is wrong: " + SimulateCommand.Describe(failure));
            await console.Error.WriteAsync(CostSheetOptions.Usage);
            return SimulateCommand.UsageError;
        }

        using (host)
        {
            return await CostSheetCommand.RunAsync(args, host.Services, console, cancellationToken);
        }
    }
}
