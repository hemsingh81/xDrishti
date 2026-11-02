using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Filings;

/// <summary>The calendar store as an in-memory fake that applies the real domain rule per (instrument, period, purpose).</summary>
internal sealed class FakeCalendarStore : ICalendarStore
{
    public List<CalendarRow> Rows { get; } = [];

    public Exception? ThrowOnInsert { get; set; }

    private readonly Lock _gate = new();

    public ValueTask<FilingInsertResult> InsertAsync(NewMeeting meeting, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Insert(meeting);
        }
    }

    private ValueTask<FilingInsertResult> Insert(NewMeeting meeting)
    {
        if (ThrowOnInsert is not null)
        {
            throw ThrowOnInsert;
        }

        var f = meeting.Filing;
        var existing = Rows.FirstOrDefault(r => r.Filing.ContentSha256 == f.ContentSha256);
        if (existing is not null)
        {
            return ValueTask.FromResult(new FilingInsertResult(f.SameSidecar(existing.Filing) ? FilingInsertOutcome.Duplicate : FilingInsertOutcome.SidecarConflict, existing.Filing));
        }

        var group = f.State == BroadcastState.Ok ? Rows.Where(r => r.Filing.InstrumentId == f.InstrumentId && r.Filing.PeriodEnd == f.PeriodEnd && r.Purpose == meeting.Purpose && r.Filing.State == BroadcastState.Ok).Select(r => r.Filing).ToList() : [];
        var decision = PointInTimeRule.Decide(f, group);
        if (decision.Outcome == RevisionOutcome.AmbiguousOrder)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.AmbiguousOrder));
        }

        if (decision.Outcome == RevisionOutcome.BadSupersede)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.BadSupersede));
        }

        Rows.Add(new CalendarRow(new StoredFiling(f.Id, f.InstrumentId, f.Kind, f.PeriodEnd, f.BroadcastTs, f.State, f.UnavailableReason, f.IngestedAt, f.ContentSha256, f.FileBytes, f.FileName, decision.Supersedes), meeting.MeetingDate, meeting.Purpose, meeting.Status));
        return ValueTask.FromResult(new FilingInsertResult(decision.Outcome == RevisionOutcome.StoreOutOfOrder ? FilingInsertOutcome.StoredOutOfOrder : FilingInsertOutcome.Stored, null, decision.Supersedes));
    }
}

/// <summary>The shareholding store as an in-memory fake that applies the real domain rule per (instrument, period).</summary>
internal sealed class FakeShareholdingStore : IShareholdingStore
{
    public List<ShareholdingEnvelope> Groups { get; } = [];

    private readonly Lock _gate = new();

    public ValueTask<FilingInsertResult> InsertAsync(NewShareholding group, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Insert(group);
        }
    }

    private ValueTask<FilingInsertResult> Insert(NewShareholding group)
    {
        var f = group.Filing;
        var existing = Groups.FirstOrDefault(r => r.Filing.ContentSha256 == f.ContentSha256);
        if (existing is not null)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.Duplicate, existing.Filing));
        }

        var rows = f.State == BroadcastState.Ok ? Groups.Where(r => r.Filing.InstrumentId == f.InstrumentId && r.Filing.PeriodEnd == f.PeriodEnd && r.Filing.State == BroadcastState.Ok).Select(r => r.Filing).ToList() : [];
        var decision = PointInTimeRule.Decide(f, rows);
        if (decision.Outcome == RevisionOutcome.AmbiguousOrder)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.AmbiguousOrder));
        }

        Groups.Add(new ShareholdingEnvelope(new StoredFiling(f.Id, f.InstrumentId, f.Kind, f.PeriodEnd, f.BroadcastTs, f.State, f.UnavailableReason, f.IngestedAt, f.ContentSha256, f.FileBytes, f.FileName, decision.Supersedes), group.Lines));
        return ValueTask.FromResult(new FilingInsertResult(decision.Outcome == RevisionOutcome.StoreOutOfOrder ? FilingInsertOutcome.StoredOutOfOrder : FilingInsertOutcome.Stored, null, decision.Supersedes));
    }
}

internal sealed class FakeCalendarRows : ICalendarRows
{
    public List<CalendarRow> Rows { get; } = [];

    public List<(IReadOnlyCollection<Guid>? Ids, DateTimeOffset Cutoff)> Calls { get; } = [];

    public ValueTask<IReadOnlyList<CalendarRow>> ResultsRowsAsync(IReadOnlyCollection<Guid>? instrumentIds, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        Calls.Add((instrumentIds, cutoff));
        return ValueTask.FromResult<IReadOnlyList<CalendarRow>>([.. Rows.Where(r => r.Purpose == MeetingRow.Results && r.Filing.BroadcastTs <= cutoff && (instrumentIds is null || instrumentIds.Contains(r.Filing.InstrumentId)))]);
    }
}

internal sealed class FakeShareholdingRows : IShareholdingRows
{
    public List<ShareholdingEnvelope> Groups { get; } = [];

    public ValueTask<IReadOnlyList<ShareholdingEnvelope>> GroupsAsync(Guid instrumentId, DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ShareholdingEnvelope>>([.. Groups.Where(g => g.Filing.InstrumentId == instrumentId && g.Filing.BroadcastTs <= cutoff)]);
}

internal sealed class FakeTradingCalendar(TradingCalendar? calendar = null) : ITradingCalendar
{
    private readonly TradingCalendar _calendar = calendar ?? new TradingCalendar();

    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_calendar);

    public void Invalidate()
    {
    }
}

/// <summary>The announcement store as an in-memory fake: idempotent on the content hash, every row its own group.</summary>
internal sealed class FakeAnnouncementStore : IAnnouncementStore
{
    public List<NewAnnouncement> Rows { get; } = [];

    private readonly Lock _gate = new();

    public ValueTask<FilingInsertResult> InsertAsync(NewAnnouncement announcement, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var existing = Rows.FirstOrDefault(r => r.Filing.ContentSha256 == announcement.Filing.ContentSha256);
            if (existing is not null)
            {
                return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.Duplicate));
            }

            Rows.Add(announcement);
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.Stored));
        }
    }
}

/// <summary>The sample source as a fake over a list of candidates and their bodies.</summary>
internal sealed class FakeAnnouncementCandidates : IAnnouncementCandidates
{
    public List<SampleCandidate> Dated { get; } = [];

    public Dictionary<Guid, AnnouncementBody> Bodies { get; } = [];

    public int NoMetadata { get; set; }

    public (DateTimeOffset From, DateTimeOffset To)? Range { get; private set; }

    public (DateTimeOffset From, DateTimeOffset To) Candidates() => Range!.Value;

    public ValueTask<AnnouncementCandidates> ListAsync(DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        Range = (from, toExclusive);
        return ValueTask.FromResult(new AnnouncementCandidates([.. Dated], NoMetadata));
    }

    public ValueTask<IReadOnlyDictionary<Guid, AnnouncementBody>> ReadBodiesAsync(IReadOnlyCollection<Guid> filingIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, AnnouncementBody>>(Bodies.Where(b => filingIds.Contains(b.Key)).ToDictionary(b => b.Key, b => b.Value));
}

/// <summary>The announcement eval files in memory; counts the writes.</summary>
internal sealed class FakeAnnouncementEvalFiles : IAnnouncementEvalFiles
{
    public Dictionary<AnnouncementFile, string> Files { get; } = [];

    public List<AnnouncementFile> Writes { get; } = [];

    public ValueTask<string?> ReadAsync(AnnouncementFile file, CancellationToken cancellationToken) => ValueTask.FromResult(Files.TryGetValue(file, out var text) ? text : null);

    public ValueTask WriteAsync(AnnouncementFile file, string text, CancellationToken cancellationToken)
    {
        Files[file] = text;
        Writes.Add(file);
        return ValueTask.CompletedTask;
    }
}
