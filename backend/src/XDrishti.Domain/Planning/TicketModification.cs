using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Planning;

/// <summary>
/// What the owner may change on a ticket and how it is recorded (spec P7-10, ADR 0095). Only the size per account, and only DOWN: a new quantity is at least 1 and below the allocated one, for an account the ticket is already sized for
/// (a paper-mode account; a live account or a new account cannot be added), so the risk of a modified ticket is never above what the allocation and the checklist judged. Side, instrument, stop, target and trigger cannot be changed (their rules
/// are text the system cannot re-validate; to trade differently the owner rejects the ticket). The text stored in the approval is canonical (<c>qty:{account}={n};...</c>, accounts in id order), so the same change is the same text, and
/// <see cref="Parse"/> reads it back to check it again when the modified ticket is approved. Pure.
/// </summary>
public static class TicketModification
{
    /// <summary>The longest text stored (the column holds 1000 characters).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>The code of a refused modification.</summary>
    public const string InvalidCode = "plans.modification_invalid";

    private const string Prefix = "qty:";

    /// <summary>Checks the changes against the allocations of the ticket; the changes in canonical order when they are valid.</summary>
    public static Result<IReadOnlyList<QuantityChange>> Validate(IReadOnlyList<(Guid AccountId, string Mode, int Quantity)> allocated, IReadOnlyList<QuantityChange>? changes)
    {
        ArgumentNullException.ThrowIfNull(allocated);
        if (changes is null || changes.Count == 0)
        {
            return Fail("A modification names at least one account and its new quantity.");
        }

        if (changes.Select(c => c.AccountId).Distinct().Count() != changes.Count)
        {
            return Fail("An account appears once in a modification.");
        }

        foreach (var change in changes)
        {
            var index = allocated.ToList().FindIndex(a => a.AccountId == change.AccountId);
            if (index < 0)
            {
                return Fail($"The ticket is not sized for account {change.AccountId:D}; a modification cannot add an account.");
            }

            var current = allocated[index];
            if (!string.Equals(current.Mode, "paper", StringComparison.Ordinal))
            {
                return Fail("Only a paper-mode allocation can be modified.");
            }

            if (change.Quantity < 1 || change.Quantity >= current.Quantity)
            {
                return Fail($"The new quantity for account {change.AccountId:D} is at least 1 and below the allocated {current.Quantity}; a modification can only reduce a size.");
            }
        }

        var ordered = changes.OrderBy(c => c.AccountId).ToList();
        return Serialize(ordered).Length > MaxTextLength ? Fail("The modification is too long.") : Result.Success<IReadOnlyList<QuantityChange>>(ordered);
    }

    /// <summary>The canonical text of valid changes.</summary>
    public static string Serialize(IReadOnlyList<QuantityChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return string.Join(';', changes.OrderBy(c => c.AccountId).Select(c => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{c.AccountId:D}={c.Quantity}")));
    }

    /// <summary>Reads a stored text back; a failure when it is not exactly what <see cref="Serialize"/> writes.</summary>
    public static Result<IReadOnlyList<QuantityChange>> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail("The stored modification is empty.");
        }

        var changes = new List<QuantityChange>();
        foreach (var part in text.Split(';'))
        {
            var pair = part.StartsWith(Prefix, StringComparison.Ordinal) ? part[Prefix.Length..].Split('=') : [];
            if (pair.Length != 2 || !Guid.TryParseExact(pair[0], "D", out var id) || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
            {
                return Fail("The stored modification cannot be read.");
            }

            changes.Add(new QuantityChange(id, quantity));
        }

        return string.Equals(Serialize(changes), text, StringComparison.Ordinal) ? Result.Success<IReadOnlyList<QuantityChange>>(changes) : Fail("The stored modification is not in canonical form.");
    }

    private static Result<IReadOnlyList<QuantityChange>> Fail(string message) => Result.Failure<IReadOnlyList<QuantityChange>>(Error.Validation(InvalidCode, message));
}
