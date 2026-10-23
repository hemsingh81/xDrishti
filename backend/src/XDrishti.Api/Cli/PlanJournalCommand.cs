using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Planning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd plan journal</c> (spec P7-14, ADR 0093): journals the stored fills by calling the use case. No decision lives here, and nothing is sent to a broker.</summary>
internal static class PlanJournalCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = PlanJournalOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(PlanJournalOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<JournalPlansCommand, JournalOutcome>>()
                .HandleAsync(new JournalPlansCommand(options.Date), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{o.Plans} plan(s), {o.Tickets} approved ticket(s); matched this run: {o.Entries} entry fill(s), {o.Exits} exit fill(s), {o.Discretionary} discretionary, {o.Unmatched.Count} unmatched (ambiguous); newly written: {o.Written} row(s) (rows stored by an earlier run are matched again, not written again)"));
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"fill grades: {o.GradesWritten} written ({o.GradesUncertain} uncertain: pre-existing position or oversized exit, rupee result only, no R), {o.GradesPending} waiting (position open or plan grade not there yet){(o.OpenTruncated ? "; more open entries than one run grades, the oldest wait for a later run" : string.Empty)}; basis: realised fills, not simulated"));
            foreach (var u in o.Unmatched)
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  unmatched {u.Date:yyyy-MM-dd} {u.Side} {u.Qty} {u.Symbol}: {u.Reason}")));
            }

            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }
}
