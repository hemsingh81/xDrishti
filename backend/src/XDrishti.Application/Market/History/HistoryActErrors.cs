using System.Globalization;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.History;

/// <summary>The refusals that only the web acts of the history card add (CF-04e); the refusals of the backfill itself stay in <see cref="BackfillErrors"/>. Codes are part of the contract.</summary>
public static class HistoryActErrors
{
    public const string RunActiveCode = "history.run_active";

    public const string OutsideWindowCode = "history.outside_window";

    public const string OwnerOnlyCode = "history.owner_only";

    /// <summary>The acts are the owner's: an empty actor (no e-mail address on the session) is refused, as every owner-act handler does.</summary>
    public static Error OwnerOnly() => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner can start the history backfill or the seam probe. Nothing was requested.");

    public static bool IsOwner(string? actor) => !string.IsNullOrWhiteSpace(actor) && actor.Trim().Length > OwnerConfirmation.OwnerPrefix.Length && actor.StartsWith(OwnerConfirmation.OwnerPrefix, StringComparison.Ordinal);

    public static Error RunActive() => Error.Conflict(RunActiveCode, "A history backfill or seam probe is running now. Nothing was requested.");

    /// <summary>Outside <c>Backfill:RunWindow</c>: names the next start of the window.</summary>
    public static Error OutsideWindow(TimeOnly from, TimeOnly to) =>
        Error.Conflict(OutsideWindowCode, string.Create(CultureInfo.InvariantCulture, $"This is outside the run window {from:HH\\:mm}-{to:HH\\:mm} (India time). The next window starts at {from:HH\\:mm}. Nothing was requested."));
}
