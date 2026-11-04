namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>
/// The stored session regime labels (P6-02, needs P3-01d; neither is built): which sessions are labelled <c>range</c>. Credit spreads and condors (P14-10) trade only the session after a range session. <see cref="AnyAsync"/> is an existence query: when it is false the credit
/// structures are NOT_EVALUABLE <c>regime_unavailable</c> and no bar is read. Both reads are select only. Until the regime tagger stores labels the adapter answers false and an empty set.
/// </summary>
public interface IRegimeLabels
{
    /// <summary>Whether any regime label is stored.</summary>
    ValueTask<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>The sessions in the range (inclusive) that are labelled <c>range</c>; a session with no label is not in it.</summary>
    ValueTask<IReadOnlySet<DateOnly>> RangeSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
