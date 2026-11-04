using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;

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

    /// <summary>The hash of the pre-registered primary profile of a family (P14-10): <c>atm</c>, width 2, <c>nearest_safe</c>, no structure stop and an intraday plan for the spreads; <c>k</c> 6, <c>m</c> 1 for the condor. Null for a family that is not an expression family.</summary>
    public static string? PrimaryHashOf(string family) => family switch
    {
        ExpressionFamilies.OptionBuy => PrimaryHash,
        ExpressionFamilies.DebitSpread => SpreadParams.Create(Expression.DebitSpread, OptionProfileParams.Atm, 2, OptionProfileParams.NearestSafe, null, 1).Value.ToProfile().Value.Hash,
        ExpressionFamilies.CreditSpread => SpreadParams.Create(Expression.CreditSpread, OptionProfileParams.Atm, 2, OptionProfileParams.NearestSafe, null, 1).Value.ToProfile().Value.Hash,
        ExpressionFamilies.IronCondor => CondorParams.Primary.ToProfile().Value.Hash,
        _ => null,
    };

    /// <summary>The expression kind a family's profiles must have; null for an unknown family.</summary>
    public static Expression? KindOf(string family) => family switch
    {
        ExpressionFamilies.OptionBuy => Expression.OptionBuy,
        ExpressionFamilies.DebitSpread => Expression.DebitSpread,
        ExpressionFamilies.CreditSpread => Expression.CreditSpread,
        ExpressionFamilies.IronCondor => Expression.IronCondor,
        _ => null,
    };
}
