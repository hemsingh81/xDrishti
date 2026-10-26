namespace XDrishti.Domain.Paper;

/// <summary>Where one ticket fell in the discipline report (spec P10-03).</summary>
/// <param name="Ticket">The ticket.</param>
/// <param name="Category">One of <see cref="DisciplineCategory.All"/>.</param>
/// <param name="Trades">Its stored paper trades (one per account).</param>
/// <param name="SkipCode">For <see cref="DisciplineCategory.ApprovedNotEntered"/>: the skip code (the first by name when accounts differ).</param>
/// <param name="PendingWhy">For <see cref="DisciplineCategory.Pending"/>: undecided, awaiting_capture or open.</param>
/// <param name="SizeReduced">Whether the owner approved less than the proposed size.</param>
public sealed record TicketOutcome(ReviewTicket Ticket, string Category, IReadOnlyList<ReviewTrade> Trades, string? SkipCode, string? PendingWhy, bool SizeReduced)
{
    /// <summary>Whether the ticket has a closed paper trade on every account (followed or sized down).</summary>
    public bool ClosedTrade => Category is DisciplineCategory.Followed or DisciplineCategory.SizedDown;

    /// <summary>The ticket's R, once: every account's replay gives the same size-free R, so the first account's is used (count once).</summary>
    public double? R => ClosedTrade ? Trades.Where(t => t.Closed).OrderBy(t => t.AccountId).First().Exit!.RSim : null;

    /// <summary>The ticket's size effect in R (spec P10-03, criterion 5): <c>(allocated - quantity) / allocated x r_sim</c> over all accounts of the ticket; null unless closed.</summary>
    public double? SizeEffectR
    {
        get
        {
            if (R is not { } r)
            {
                return null;
            }

            double allocated = Trades.Sum(t => t.QtyAllocated);
            double taken = Trades.Sum(t => t.Qty);
            return allocated <= 0 ? null : (allocated - taken) / allocated * r;
        }
    }
}
