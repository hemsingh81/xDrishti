using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// Costs are gross forever (ADR 0292): no charge-sheet confirmation ever exists, in any host, so every report, paper figure and replay is gross and every net-only gate stays closed.
/// </summary>
internal sealed class NoChargeSheetConfirmation : IChargeSheetConfirmationSource
{
    public ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ChargeSheetConfirmation?>(null);
}
