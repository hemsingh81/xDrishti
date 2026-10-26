namespace XDrishti.Infrastructure.Reporting;

/// <summary>A row of <c>workflow.charge_sheet_events</c> (spec P8-23, ADR 0127): the owner's acts on the charge-sheet confirmation, insert-only. The id and the time are drawn by a trigger under a lock, the India date of a confirmation too.</summary>
internal sealed class ChargeSheetEventRecord
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? SheetId { get; set; }

    public string? RatesSha256 { get; set; }

    public DateOnly? ConfirmedOn { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
