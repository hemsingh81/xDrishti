using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application;

public enum LookupVerdict
{
    /// <summary>The broker has the leg: adopt its status.</summary>
    Found,

    /// <summary>Absent in two lookups 2 s apart: the leg was never placed.</summary>
    NotSent,

    /// <summary>The lookup itself failed: the leg stays unknown (the caller raises the alarm after 30 s).</summary>
    StillUnknown,
}

public sealed record LookupResult(LookupVerdict Verdict, BrokerLeg? Leg);

/// <summary>Resolves an unknown send by asking the broker for our client id, never by sending again.</summary>
public sealed class LegLookup(IBrokerOrders broker, TimeProvider clock)
{
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);

    public async Task<LookupResult> ResolveAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        for (var look = 0; look < 2; look++)
        {
            try
            {
                if (await broker.LookupAsync(clientOrderId, cancellationToken).ConfigureAwait(false) is { } found)
                {
                    return new LookupResult(LookupVerdict.Found, found);
                }
            }
            catch (BrokerLookupFailedException)
            {
                return new LookupResult(LookupVerdict.StillUnknown, null);
            }

            if (look == 0)
            {
                await Task.Delay(Gap, clock, cancellationToken).ConfigureAwait(false);
            }
        }

        return new LookupResult(LookupVerdict.NotSent, null);
    }
}
