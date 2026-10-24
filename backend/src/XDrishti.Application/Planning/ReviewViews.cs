namespace XDrishti.Application.Planning;

/// <summary>One check on the review screen.</summary>
/// <param name="Code">The check code.</param>
/// <param name="Outcome">pass, fail or unavailable, as recorded when the plan was built.</param>
/// <param name="Standing">passed, warned, needsconfirmation, confirmeddespite or refused: where it stands for approval.</param>
/// <param name="Detail">One line.</param>
public sealed record ReviewCheckView(string Code, string Outcome, string Standing, string Detail);

/// <summary>A ticket's checklist for one account.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Mode">The allocation's mode (paper).</param>
/// <param name="Quantity">The size the plan allocated.</param>
/// <param name="EffectiveQuantity">The size after the owner's modification, or the allocated one.</param>
/// <param name="CanApprove">Whether this account's checklist allows approval without a confirmation.</param>
/// <param name="Checks">All checks in the fixed order, or empty when the plan stored no readable checklist.</param>
public sealed record ReviewAccountView(Guid AccountId, string Mode, int Quantity, int EffectiveQuantity, bool CanApprove, IReadOnlyList<ReviewCheckView> Checks);

/// <summary>One ticket on the review screen.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Rank">Order in the plan.</param>
/// <param name="Group">primary or reserve.</param>
/// <param name="State">Its state.</param>
/// <param name="Symbol">Instrument.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="ChecklistRecorded">False when the plan stored no readable checklist: the ticket cannot be approved.</param>
/// <param name="CanApprove">Whether the owner may approve it as it stands (checks all passed or warned).</param>
/// <param name="NeedsConfirmation">The check codes per account that need the owner's confirmation to approve; empty when none.</param>
/// <param name="ApproveAllEligible">Whether approve-all would approve it: primary, proposed, every check passed.</param>
/// <param name="Modification">The owner's size change, as stored, or null.</param>
/// <param name="ConfirmedDespite">What the approval overrode (stored text), or null.</param>
/// <param name="Accounts">The checklist per account.</param>
public sealed record ReviewTicketView(
    long ItemId,
    int Rank,
    string Group,
    string State,
    string Symbol,
    string Side,
    bool ChecklistRecorded,
    bool CanApprove,
    IReadOnlyList<string> NeedsConfirmation,
    bool ApproveAllEligible,
    string? Modification,
    string? ConfirmedDespite,
    IReadOnlyList<ReviewAccountView> Accounts);

/// <summary>A plan under review (spec P7-10).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ForDate">The session.</param>
/// <param name="State">The plan state.</param>
/// <param name="ReviewCutoff">Approvals and modifications are refused from this time; undecided tickets lapse.</param>
/// <param name="Open">Whether decisions can still be taken now (plan open and before the cutoff).</param>
/// <param name="Notice">What an approval means: watched, no order.</param>
/// <param name="Tickets">Primary and reserve tickets.</param>
public sealed record PlanReviewView(long PlanId, DateOnly ForDate, string State, DateTimeOffset ReviewCutoff, bool Open, string Notice, IReadOnlyList<ReviewTicketView> Tickets);

/// <summary>A suggestion in the inbox.</summary>
/// <param name="Id">The suggestion.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Subject">What it is about.</param>
/// <param name="Text">In words.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Seen">Whether the owner marked it seen.</param>
public sealed record SuggestionView(long Id, string Kind, string Subject, string Text, DateTimeOffset CreatedAt, bool Seen);
