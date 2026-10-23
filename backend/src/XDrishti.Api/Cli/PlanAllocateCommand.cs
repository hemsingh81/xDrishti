using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Allocation;

namespace XDrishti.Api.Cli;

/// <summary><c>xd plan allocate</c> (spec P7-07, ADR 0089): prints the split of a supplied ticket over supplied accounts. Read only: no database, no store, no order. The decision is the Domain allocator's.</summary>
internal static class PlanAllocateCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, SimulateConsole console)
    {
        var (options, errors) = PlanAllocateOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(PlanAllocateOptions.Usage);
            return XdCommand.UsageError;
        }

        var result = PerAccountAllocator.Allocate(options.Ticket, options.Accounts);
        await console.Out.WriteLineAsync(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"ticket {options.Ticket.Symbol} {options.Ticket.Side}: {result.TotalAllocated} of {options.Ticket.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "?"} allocated")));
        foreach (var d in result.Decisions)
        {
            await console.Out.WriteLineAsync(SimulateOptions.Show($"  {d.AccountId} {d.Quantity,8} {d.Code}: {d.Message}"));
        }

        return XdCommand.Done;
    }
}
