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
        var ticketSeen = false;
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
                if (ticketSeen)
                {
                    errors.Add("'--ticket' is given twice");
                }
                else
                {
                    ticketSeen = true;
                    ticket = ParseTicket(value, errors);
                }
            }
            else if (ParseAccount(args[++i], errors) is { } account)
            {
                accounts.Add(account);
            }
        }

        if (!ticketSeen)
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
        var before = errors.Count;
        var p = text.Split(':');
        if (p.Length != 6)
        {
            errors.Add("--ticket is SYMBOL:SIDE:ENTRY:STOP:QTY:LOT");
            return null;
        }

        var ticket = new AllocationTicket(p[0], p[1], Number(p[2], "ENTRY", errors), Number(p[3], "STOP", errors), Whole(p[4], "QTY", errors), Whole(p[5], "LOT", errors));
        return errors.Count == before ? ticket : null;
    }

    private static AllocationAccount? ParseAccount(string text, List<string> errors)
    {
        var before = errors.Count;
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

        var account = new AllocationAccount(id, mode.Value, p[2] == "trading", Number(p[3], "EQUITY", errors), Number(p[4], "RISK%", errors), Number(p[5], "CAPITAL%", errors), Number(p[6], "TOTALRISK%", errors), Number(p[7], "OPENRISK%", errors), Number(p[8], "FUNDS", errors), p[9] == "?" ? null : p[9] == "yes");
        return errors.Count == before ? account : null;
    }

    // '?' is the only way to say "not known"; anything else that is not a number is a typo and a usage error.
    private static double? Number(string text, string name, List<string> errors)
    {
        if (text == "?")
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        errors.Add($"{name} must be a number or ?");
        return null;
    }

    private static int? Whole(string text, string name, List<string> errors)
    {
        if (text == "?")
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        errors.Add($"{name} must be a whole number or ?");
        return null;
    }
}
