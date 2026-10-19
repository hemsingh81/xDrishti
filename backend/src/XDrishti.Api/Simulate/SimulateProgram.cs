using Microsoft.Extensions.Options;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The process entry of the verb (spec P3-04b2, D2): builds the host, reads the export root from configuration and runs <see cref="SimulateCommand"/>. Ctrl-C is turned into a cancellation (the process must not die before the writer has removed
/// its staging folder). Kept apart from <c>Program.cs</c>, which is top-level code, so that it can be tested.
/// </summary>
internal static class SimulateProgram
{
    /// <summary>The configuration key of the export root (<c>Export__Directory</c> in the environment); a setting, never a flag.</summary>
    public const string ExportDirectoryKey = "Export:Directory";

    /// <summary>Runs the verb for the process: <paramref name="args"/> are the arguments after <c>simulate</c>.</summary>
    public static async Task<int> RunProcessAsync(IReadOnlyList<string> args)
    {
        using var cancel = new CancellationTokenSource();
        var interrupt = new InterruptCounter(cancel);
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
            host = SimulateHost.Create(overrides, configure);
        }
        catch (Exception failure) when (failure is InvalidOperationException or OptionsValidationException or ArgumentException or AggregateException)
        {
            await console.Error.WriteLineAsync("the configuration is wrong: " + failure.GetType().Name + ": " + Cut(failure.Message));
            await console.Error.WriteAsync(SimulateOptions.Usage);
            return SimulateCommand.UsageError;
        }

        using (host)
        {
            var exportRoot = host.Services.GetRequiredService<IConfiguration>()[ExportDirectoryKey];
            return await SimulateCommand.RunAsync(args, host.Services, console, exportRoot, cancellationToken);
        }
    }

    private static string Cut(string message)
    {
        var clean = string.Concat(message.Select(c => char.IsControl(c) ? ' ' : c));
        return clean.Length > SimulateCommand.MaxFailureMessage ? clean[..SimulateCommand.MaxFailureMessage] + "..." : clean;
    }

    /// <summary>
    /// Ctrl-C: the first press cancels the token and the process goes on (it must not die before the writer has removed its staging folder); a second press is not swallowed, so a stuck synchronous write can still be killed.
    /// </summary>
    internal sealed class InterruptCounter(CancellationTokenSource cancel)
    {
        private int _presses;

        /// <summary>Records a press; true means "swallow it" (the first), false lets the default action end the process (any later one).</summary>
        public bool Pressed()
        {
            if (Interlocked.Increment(ref _presses) > 1)
            {
                return false;
            }

            try
            {
                cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // the run has ended; nothing to cancel
            }

            return true;
        }
    }
}
