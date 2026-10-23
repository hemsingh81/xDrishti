using System.Globalization;
using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Allocation;

/// <summary>
/// Splits one ticket's quantity across paper-mode Trading accounts by each account's own limits (spec P7-07, ADR 0089). Pure and deterministic: the same inputs in any order give the same decisions.
/// Only a paper-mode account with the Trading role can receive a quantity; any other is excluded with a code. An input that is missing or unusable gives nothing, never a guess.
/// Capacity of an account is the least of: equity x risk % / stop distance, equity x capital % / entry, funds / entry, and the open-risk headroom / stop distance, rounded down to whole lots.
/// </summary>
public static class PerAccountAllocator
{
    private const double Tolerance = 1e-9;

    /// <summary>Allocates. Every supplied account appears once in the result, ordered by account id.</summary>
    public static AllocationResult Allocate(AllocationTicket ticket, IReadOnlyList<AllocationAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(accounts);

        var ticketProblem = TicketProblem(ticket);
        var duplicates = accounts.GroupBy(a => a.AccountId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var decisions = new List<AccountAllocation>();
        var candidates = new List<Candidate>();

        foreach (var account in accounts.OrderBy(a => a.AccountId).ThenBy(a => a.Mode))
        {
            if (duplicates.Contains(account.AccountId))
            {
                decisions.Add(None(account, AllocationCodes.DuplicateAccount, null, null, "the account id was supplied more than once"));
            }
            else if (!account.IsTrading)
            {
                decisions.Add(None(account, AllocationCodes.NotTrading, null, null, "the account has no Trading role"));
            }
            else if (account.Mode != TradingMode.Paper)
            {
                decisions.Add(None(account, AllocationCodes.NotPaper, null, null, $"mode is {account.Mode}; only paper accounts receive tickets"));
            }
            else if (ticketProblem is not null)
            {
                decisions.Add(None(account, AllocationCodes.UnavailableTicket, null, null, ticketProblem));
            }
            else if (AccountProblem(account) is { } accountProblem)
            {
                decisions.Add(None(account, AllocationCodes.UnavailableAccount, null, null, accountProblem));
            }
            else if (account.HoldsSymbol == true)
            {
                decisions.Add(None(account, AllocationCodes.AlreadyHeld, null, null, $"the account already holds {ticket.Symbol}"));
            }
            else
            {
                var candidate = Size(ticket, account);
                if (candidate.Lots == 0)
                {
                    decisions.Add(None(account, AllocationCodes.BelowOneLot, candidate.Binding, 0, $"{candidate.Binding} allows {Fmt(candidate.RawShares)} shares, below one lot of {ticket.LotSize}"));
                }
                else
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count > 0)
        {
            decisions.AddRange(Split(ticket, candidates));
        }

        return new AllocationResult([.. decisions.OrderBy(d => d.AccountId).ThenBy(d => d.Mode)], ticket.Quantity);
    }

    private static List<AccountAllocation> Split(AllocationTicket ticket, List<Candidate> candidates)
    {
        var lot = ticket.LotSize!.Value;
        long pool = ticket.Quantity!.Value / lot;
        var total = candidates.Sum(c => c.Lots);
        var given = new long[candidates.Count];
        if (total <= pool)
        {
            for (var i = 0; i < given.Length; i++)
            {
                given[i] = candidates[i].Lots;
            }
        }
        else
        {
            for (var i = 0; i < given.Length; i++)
            {
                given[i] = pool * candidates[i].Lots / total;
            }

            var left = pool - given.Sum();
            var order = Enumerable.Range(0, given.Length).OrderByDescending(i => candidates[i].Lots - given[i]).ThenBy(i => candidates[i].Account.AccountId).ToList();
            while (left > 0)
            {
                var progressed = false;
                foreach (var i in order)
                {
                    if (left > 0 && given[i] < candidates[i].Lots)
                    {
                        given[i]++;
                        left--;
                        progressed = true;
                    }
                }

                if (!progressed)
                {
                    break;
                }
            }
        }

        var result = new List<AccountAllocation>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var quantity = checked((int)(given[i] * lot));
            var capacity = checked((int)(c.Lots * lot));
            if (quantity == 0)
            {
                result.Add(None(c.Account, AllocationCodes.PoolExhausted, c.Binding, capacity, $"could take {capacity} shares ({c.Binding}) but the ticket's {ticket.Quantity} went to accounts with more room"));
                continue;
            }

            var riskPct = quantity * c.Distance / c.Equity * 100;
            result.Add(new AccountAllocation(c.Account.AccountId, c.Account.Mode, quantity, AllocationCodes.Allocated, c.Binding, capacity, riskPct, $"{quantity} of {capacity} shares allowed ({c.Binding}), risk {Fmt(riskPct)}% of equity"));
        }

        return result;
    }

    private static Candidate Size(AllocationTicket ticket, AllocationAccount account)
    {
        var entry = ticket.EntryPrice!.Value;
        var distance = Math.Abs(entry - ticket.StopPrice!.Value);
        var equity = account.Equity!.Value;
        var headroomPct = account.MaxTotalRiskPct!.Value - account.OpenRiskPct!.Value;
        var caps = new (string Code, double Shares)[]
        {
            (AllocationCodes.RiskPerTrade, equity * account.RiskPerTradePct!.Value / 100 / distance),
            (AllocationCodes.CapitalPerTrade, equity * account.MaxCapitalPerTradePct!.Value / 100 / entry),
            (AllocationCodes.Funds, account.FundsAvailable!.Value / entry),
            (AllocationCodes.ExposureHeadroom, Math.Max(0, equity * headroomPct / 100 / distance)),
        };
        var binding = caps.OrderBy(c => c.Shares).First();
        var lot = ticket.LotSize!.Value;
        var lots = (long)Math.Min(Math.Floor((binding.Shares + Tolerance) / lot), int.MaxValue / lot);
        return new Candidate(account, binding.Code, binding.Shares, lots, distance, equity);
    }

    private static string? TicketProblem(AllocationTicket t)
    {
        if (t.Side is not ("BUY" or "SELL"))
        {
            return "side is not BUY or SELL";
        }

        if (t.EntryPrice is not { } entry || !double.IsFinite(entry) || entry <= 0)
        {
            return "entry price is missing or not above 0";
        }

        if (t.StopPrice is not { } stop || !double.IsFinite(stop) || stop <= 0)
        {
            return "stop price is missing or not above 0";
        }

        if (t.Side == "BUY" ? stop >= entry : stop <= entry)
        {
            return "stop is not on the loss side of the entry";
        }

        if (t.LotSize is not { } lot || lot < 1)
        {
            return "lot size is missing or below 1";
        }

        return t.Quantity is not { } quantity || quantity < lot ? "quantity is missing or below one lot" : null;
    }

    private static string? AccountProblem(AllocationAccount a)
    {
        var missing = new List<string>();
        Positive(a.Equity, "equity", missing);
        Positive(a.RiskPerTradePct, "risk per trade", missing);
        Positive(a.MaxCapitalPerTradePct, "capital per trade", missing);
        Positive(a.MaxTotalRiskPct, "total risk", missing);
        NotNegative(a.OpenRiskPct, "open risk", missing);
        NotNegative(a.FundsAvailable, "funds", missing);
        if (a.HoldsSymbol is null)
        {
            missing.Add("holdings");
        }

        return missing.Count == 0 ? null : "missing or unusable: " + string.Join(", ", missing);
    }

    private static void Positive(double? value, string name, List<string> missing)
    {
        if (value is not { } v || !double.IsFinite(v) || v <= 0)
        {
            missing.Add(name);
        }
    }

    private static void NotNegative(double? value, string name, List<string> missing)
    {
        if (value is not { } v || !double.IsFinite(v) || v < 0)
        {
            missing.Add(name);
        }
    }

    private static AccountAllocation None(AllocationAccount account, string code, string? binding, int? capacity, string message) =>
        new(account.AccountId, account.Mode, 0, code, binding, capacity, 0, message);

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record Candidate(AllocationAccount Account, string Binding, double RawShares, long Lots, double Distance, double Equity);
}
