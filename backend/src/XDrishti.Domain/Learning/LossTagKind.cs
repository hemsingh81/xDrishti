namespace XDrishti.Domain.Learning;

/// <summary>
/// What a losing trade shows about itself (P6-15, ADR 0071, rule version <see cref="LossTagger.RuleVersion"/>). Descriptions of the closed trade, never decision-time inputs. Stored nowhere; names are stable.
/// </summary>
public enum LossTagKind
{
    /// <summary>The stop exit filled beyond the stop level: the market gapped through it.</summary>
    GapThroughStop,

    /// <summary>The trade was open profit of at least the threshold in R and ended as a loss.</summary>
    GaveBackProfit,

    /// <summary>The modelled fills cost at least the threshold in R against the exact prices.</summary>
    HighSlippage,

    /// <summary>A trade that left on its entry date was decided late in the session.</summary>
    LateEntry,

    /// <summary>The stop was hit within a few minutes with almost no favourable move.</summary>
    QuickStop,

    /// <summary>The stop distance was a large share of the entry price.</summary>
    WideStop,

    /// <summary>A stop-out of about one planned risk that shows none of the other signatures: the ordinary cost of the strategy.</summary>
    NormalVariance,
}
