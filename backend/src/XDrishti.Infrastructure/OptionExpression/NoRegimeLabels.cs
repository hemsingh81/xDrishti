using XDrishti.Application.Abstractions.OptionExpression;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary>
/// <see cref="IRegimeLabels"/> until the regime tagger exists (P6-02, needs P3-01d; neither is built, <c>CellId.Regime</c> is <c>*</c> everywhere): no label is stored, so the credit structures are NOT_EVALUABLE <c>regime_unavailable</c>. It reads nothing and invents nothing;
/// the day the tagger stores labels this class is replaced by a select-only reader of them.
/// </summary>
internal sealed class NoRegimeLabels : IRegimeLabels
{
    public ValueTask<bool> AnyAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<IReadOnlySet<DateOnly>> RangeSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly>());
}
