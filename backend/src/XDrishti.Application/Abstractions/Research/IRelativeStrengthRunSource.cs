using XDrishti.Domain.Market.Strength;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>What a Lab run reads of the stored relative-strength rows (BK-09): one <see cref="BeginAsync"/> per run, then one read per instrument.</summary>
public interface IRelativeStrengthRunSource
{
    /// <summary>Finds the pre-registered baskets (with their basis and parameter hash) and the sectors of the instruments; reads no rows yet.</summary>
    ValueTask<IRelativeStrengthRun> BeginAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>One run's reading of the stored rows.</summary>
public interface IRelativeStrengthRun
{
    /// <summary>The rows of one instrument and of its sector as of the sessions before the window end, for the window that starts at <paramref name="from"/>.</summary>
    ValueTask<RelativeStrengthInputs> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken);

    /// <summary>The record of everything read so far for the run header, with the count of <c>feature_unknown</c> steps.</summary>
    RelativeStrengthRecord Record(long featureUnknown = 0);
}
