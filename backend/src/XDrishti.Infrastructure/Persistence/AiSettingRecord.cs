namespace XDrishti.Infrastructure.Persistence;

/// <summary>
/// <c>ai.settings</c> (spec P9-02, ADR 0137): every change of the assistant's switches, appended by the owner's act and never edited; the newest row is current, no row means the assistant is off and sees no portfolio data. The id, the time and the hash of the canonical
/// text of every value column are stamped by a trigger. Nothing in this slice writes it; the settings endpoint of P9-03 does.
/// </summary>
internal sealed class AiSettingRecord
{
    public long Id { get; set; }

    public bool Enabled { get; set; }

    public bool PortfolioContext { get; set; }

    public int MaxToolCalls { get; set; }

    public bool AllowDraftConfirm { get; set; }

    public bool AutoLabQuickTest { get; set; }

    public bool AllowProposalAccept { get; set; }

    public bool AutoAcceptHypothesis { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string SnapshotHash { get; set; } = string.Empty;
}
