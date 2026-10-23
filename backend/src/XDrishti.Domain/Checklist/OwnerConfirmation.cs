using XDrishti.Domain.Common;

namespace XDrishti.Domain.Checklist;

/// <summary>
/// The owner's "approve despite" (spec P7-05), bound to one account, one symbol and the check codes named. It covers only those checks of that ticket on that account: another account or symbol, or a check not named, is not covered.
/// Only the owner can make one, and the reason cannot be blank. P7-10 stores <see cref="StoredText"/> in <c>workflow.approvals.confirm_reason</c> (varchar 500). A confirmation never turns a check into a pass.
/// </summary>
public sealed record OwnerConfirmation
{
    /// <summary>The prefix of an owner actor: the one definition. The plan record's actor check reuses it, and the database check repeats it (ADR 0083). The actor must come from the authenticated identity, never from a request.</summary>
    public const string OwnerPrefix = "owner:";

    /// <summary>The longest reason accepted. With the 14 codes it keeps <see cref="StoredText"/> under the 500 characters of the column.</summary>
    public const int MaxReasonLength = 200;

    private OwnerConfirmation(string actor, string reason, string accountId, string symbol, IReadOnlyList<string> codes)
    {
        Actor = actor;
        Reason = reason;
        AccountId = accountId;
        Symbol = symbol;
        Codes = codes;
    }

    /// <summary>Who confirmed.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed.</summary>
    public string Reason { get; }

    /// <summary>The account it was made for.</summary>
    public string AccountId { get; }

    /// <summary>The symbol it was made for.</summary>
    public string Symbol { get; }

    /// <summary>The checks it covers, distinct, in the fixed check order.</summary>
    public IReadOnlyList<string> Codes { get; }

    /// <summary>Whether the owner is the actor of a plan record move: <c>owner:</c> followed by a name.</summary>
    public static bool IsOwnerActor(string? actor) =>
        actor is not null && actor.StartsWith(OwnerPrefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(actor[OwnerPrefix.Length..]);

    /// <summary>Makes a confirmation, or says why not: an owner actor, a reason with text that fits, an account, a symbol and at least one known confirmable check code.</summary>
    public static Result<OwnerConfirmation> Create(string? actor, string? reason, string? accountId, string? symbol, IEnumerable<string>? codes)
    {
        if (!IsOwnerActor(actor))
        {
            return Result.Failure<OwnerConfirmation>(Error.Forbidden("checklist.owner_only", "Only the owner can approve despite a check."));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.reason_required", "Approving despite a check needs a reason."));
        }

        if (text.Length > MaxReasonLength)
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.reason_too_long", "The reason is at most " + MaxReasonLength + " characters."));
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(symbol))
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.target_required", "A confirmation names the account and the symbol."));
        }

        var wanted = (codes ?? []).ToList();
        if (wanted.Count == 0 || wanted.Any(c => !ChecklistCodes.IsConfirmable(c)))
        {
            return Result.Failure<OwnerConfirmation>(Error.Validation("checklist.codes_invalid", "A confirmation names at least one check, and only checks that can be approved despite."));
        }

        var ordered = ChecklistCodes.All.Where(wanted.Contains).ToList();
        return Result.Success(new OwnerConfirmation(actor!, text, accountId.Trim(), symbol.Trim(), ordered));
    }

    /// <summary>Whether this confirmation was made for the account, the symbol and the check.</summary>
    public bool Covers(string accountId, string symbol, string code) =>
        string.Equals(AccountId, accountId?.Trim(), StringComparison.Ordinal)
        && string.Equals(Symbol, symbol?.Trim(), StringComparison.OrdinalIgnoreCase)
        && Codes.Contains(code);

    /// <summary>The text P7-10 stores in <c>confirm_reason</c>: the codes actually overridden, then the reason, so the Decisions report can show which checks were overridden.</summary>
    public string StoredText(IEnumerable<string> overriddenCodes) => "overrode " + string.Join(",", overriddenCodes) + ": " + Reason;
}
