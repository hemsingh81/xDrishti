using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>The pre-registered primary profile of protocol O (P14-09, spec item "Trials and N"): at the money, <c>nearest_safe</c> expiry, the cash-mapped exits (no premium stop), the default liquidity filter, an intraday plan (<c>max_hold_sessions</c> 1, as P14-08 requires for an intraday run). Every other profile is secondary and counted.</summary>
public static class ExpressionProfiles
{
    /// <summary>The hash of the primary profile.</summary>
    public static string PrimaryHash
    {
        get
        {
            var parameters = OptionProfileParams.Create(OptionProfileParams.Atm, OptionProfileParams.NearestSafe, null, 1).Value;
            return parameters.ToProfile().Value.Hash;
        }
    }
}
