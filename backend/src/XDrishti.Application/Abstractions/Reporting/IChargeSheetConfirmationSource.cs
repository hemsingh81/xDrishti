using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// The owner's stored confirmation of the charge sheet as their own (ISS-25, P8-00 question 19, ADR 0100). This is the only place a <see cref="ChargeSheetConfirmation"/> comes from: it is never taken from a request, a query string, a header or a body, and no
/// request type can carry one (an architecture test). The owner's act of confirming (P8-23, ADR 0127) is the only writer of what it reads; nothing here writes it.
/// </summary>
public interface IChargeSheetConfirmationSource
{
    /// <summary>The stored confirmation, or null when the owner has not confirmed a sheet (then every report is gross and net figures are unavailable).</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken);
}
