using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan allocate</c> (spec P7-07). Nothing here reads a file or the database.</summary>
internal sealed record PlanAllocateOptions(AllocationTicket Ticket, IReadOnlyList<AllocationAccount> Accounts)
{
    public const string Usage = """
        usage:
          xd plan allocate --ticket SYMBOL:SIDE:ENTRY:STOP:QTY:LOT --account ID:MODE:ROLE:EQUITY:RISK%:CAPITAL%:TOTALRISK%:OPENRISK%:FUNDS:HELD [--account ...]
            SIDE is BUY or SELL; MODE is paper or manual_live; ROLE is trading or other; HELD is yes or no; '?' stands for a number that is not known
            splits the ticket's quantity across the paper-mode Trading accounts by their own limits and prints, for every account, the quantity or the reason it got none
            read only: nothing is stored, no order is placed, nothing is approved; a live or non-Trading account is excluded
        exit codes: 0 done, 2 wrong command line

        """;

    /// <summary>Parses the arguments after <c>plan allocate</c>; the errors are listed when the options are null.</summary>
    public static (PlanAllocateOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        AllocationTicket? ticket = null;
        var accounts = new List<AllocationAccount>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is not ("--ticket" or "--account"))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (arg == "--ticket")
            {
                var value = args[++i];
                if (ticket is not null)
                {
                    errors.Add("'--ticket' is given twice");
                }
                else
                {
                    ticket = ParseTicket(value, errors);
                }
            }
            else if (ParseAccount(args[++i], errors) is { } account)
            {
                accounts.Add(account);
            }
        }

        if (ticket is null && !errors.Contains("'--ticket' is given twice"))
        {
            errors.Add("give a --ticket");
        }

        if (accounts.Count == 0)
        {
            errors.Add("give at least one --account");
        }

        return errors.Count > 0 || ticket is null ? (null, errors) : (new PlanAllocateOptions(ticket, accounts), errors);
    }

    private static AllocationTicket? ParseTicket(string text, List<string> errors)
    {
        var p = text.Split(':');
        if (p.Length != 6)
        {
            errors.Add("--ticket is SYMBOL:SIDE:ENTRY:STOP:QTY:LOT");
            return null;
        }

        return new AllocationTicket(p[0], p[1], Number(p[2]), Number(p[3]), Whole(p[4]), Whole(p[5]));
    }

    private static AllocationAccount? ParseAccount(string text, List<string> errors)
    {
        var p = text.Split(':');
        if (p.Length != 10 || !Guid.TryParse(p[0], out var id))
        {
            errors.Add("--account is ID:MODE:ROLE:EQUITY:RISK%:CAPITAL%:TOTALRISK%:OPENRISK%:FUNDS:HELD with ID a GUID");
            return null;
        }

        var mode = p[1] switch { "paper" => TradingMode.Paper, "manual_live" => TradingMode.ManualLive, _ => (TradingMode?)null };
        if (mode is null || p[2] is not ("trading" or "other") || p[9] is not ("yes" or "no" or "?"))
        {
            errors.Add("--account MODE is paper or manual_live, ROLE is trading or other, HELD is yes, no or ?");
            return null;
        }

        return new AllocationAccount(id, mode.Value, p[2] == "trading", Number(p[3]), Number(p[4]), Number(p[5]), Number(p[6]), Number(p[7]), Number(p[8]), p[9] == "?" ? null : p[9] == "yes");
    }

    // An unparsable or '?' value is "not known": the allocator then reports it as unavailable instead of guessing.
    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static int? Whole(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
