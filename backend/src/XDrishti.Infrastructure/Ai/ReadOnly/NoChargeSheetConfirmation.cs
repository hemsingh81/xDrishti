using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// The tool server never has the owner's confirmation of the charge sheet (it lives in settings no assistant role may read, spec P9-05): every report it builds is gross. The confirmation is always absent here, never taken from a request.
/// </summary>
internal sealed class NoChargeSheetConfirmation : IChargeSheetConfirmationSource
{
    public ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ChargeSheetConfirmation?>(null);
}
