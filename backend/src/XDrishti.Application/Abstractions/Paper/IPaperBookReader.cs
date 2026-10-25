using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>
/// The paper book as the nightly plan reads it (spec P10-02, ADR 0118): available only when capture is complete up to the last closed session, else <see cref="PaperBookUnavailable"/>. Read only; used only when the owner turned
/// <c>Planning:OpenBook:UsePaperLedger</c> on.
/// </summary>
public interface IPaperBookReader
{
    /// <summary>Reads the book for a plan about to be built with <paramref name="lastClosedSession"/> as its last closed session.</summary>
    ValueTask<PaperBookRead> ReadAsync(DateOnly lastClosedSession, IReadOnlyList<PaperAccountInput> accounts, CancellationToken cancellationToken);
}
