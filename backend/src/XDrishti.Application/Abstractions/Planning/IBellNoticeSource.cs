using XDrishti.Application.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>
/// A module's notices for the top-bar bell, carried into the plan notices (P8-15) without the plan notices naming the module (CF-12 part 12b). Derived on read from stored state; an item is present while its cause stands. Read only.
/// </summary>
public interface IBellNoticeSource
{
    /// <summary>The current notices at <paramref name="now"/>; empty when there is none.</summary>
    ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
