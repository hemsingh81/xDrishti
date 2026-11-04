using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>The inbox, the instruments, the store folder and the envelope table as in-memory fakes; the store applies the real domain rule.</summary>
internal sealed class FilingWorld
{
    public static readonly Guid Reliance = new("aaaaaaaa-0000-0000-0000-000000000001");

    public static readonly Guid Tcs = new("aaaaaaaa-0000-0000-0000-000000000002");

    public static readonly DateOnly Q1 = new(2026, 6, 30);

    public static readonly DateTimeOffset Broadcast = new(2026, 7, 24, 10, 42, 0, TimeSpan.Zero);

    public FakeSource Source { get; } = new();

    public FakeInstruments Instruments { get; } = new();

    public FakeFiles Files { get; } = new();

    public FakeStore Store { get; } = new();

    public FakeSettings Settings { get; } = new();

    public List<string> Events { get; } = [];

    public FilingWorld()
    {
        Instruments.Map["RELIANCE"] = InstrumentMatch.One(Reliance);
        Instruments.Map["TCS"] = InstrumentMatch.One(Tcs);
        Instruments.Map["TWIN"] = InstrumentMatch.Many;
        Files.Events = Events;
        Store.Events = Events;
    }

    public IngestFilingsHandler Handler() => new(Source, Instruments, Store, Files, Settings, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), NullLogger<IngestFilingsHandler>.Instance);

    public async Task<FilingIngestSummary> RunAsync() => (await Handler().HandleAsync(new IngestFilingsCommand(true), TestContext.Current.CancellationToken)).Value;

    public FilingEntry Add(string symbol = "RELIANCE", FilingKind kind = FilingKind.Results, DateOnly? period = null, DateTimeOffset? ts = null, string content = "a", string? name = null, BroadcastState state = BroadcastState.Ok, string? reason = null, bool useDefaultTime = true)
    {
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
        var time = state == BroadcastState.Ok ? (useDefaultTime ? ts ?? Broadcast : ts) : null;
        var entry = new FilingEntry(symbol, kind, period ?? Q1, time, state, reason, name ?? $"{symbol}_{sha[..8]}.xml", content.Length, sha);
        Source.Accepted.Add(entry);
        Source.Content[entry.Sha256] = content;
        return entry;
    }
}

internal sealed class FakeSource : IFilingsSource
{
    public List<FilingEntry> Accepted { get; } = [];

    public List<RejectedFiling> Rejected { get; } = [];

    public List<string> Problems { get; } = [];

    public Dictionary<string, string> Content { get; } = [];

    public Dictionary<string, byte[]> Raw { get; } = [];

    public int Opened { get; set; }

    public int Listed { get; private set; }

    public ValueTask<FilingListing> ListAsync(FilingQuery query, CancellationToken cancellationToken)
    {
        Listed++;
        return ValueTask.FromResult(new FilingListing([.. Accepted], [.. Rejected], [.. Problems], false));
    }

    public ValueTask<Result<Stream>> OpenAsync(FilingRef filing, CancellationToken cancellationToken)
    {
        Opened++;
        return ValueTask.FromResult(Result.Success<Stream>(new MemoryStream(Raw.TryGetValue(filing.Sha256, out var raw) ? raw : System.Text.Encoding.UTF8.GetBytes(Content[filing.Sha256]))));
    }
}

internal sealed class FakeInstruments : IFilingInstruments
{
    public Dictionary<string, InstrumentMatch> Map { get; } = new(StringComparer.Ordinal);

    public List<TrackedInstrument> Tracked { get; } = [];

    public ValueTask<InstrumentMatch> ResolveAsync(string symbol, CancellationToken cancellationToken) => ValueTask.FromResult(Map.GetValueOrDefault(symbol, InstrumentMatch.Unknown));

    public ValueTask<IReadOnlyList<TrackedInstrument>> TrackedAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([.. Tracked]);
}

internal sealed class FakeFiles : IFilingFiles
{
    private readonly Lock _gate = new();

    public List<string> Events { get; set; } = [];

    public HashSet<string> Stored { get; } = [];

    public long Used { get; set; }

    public long? Free { get; set; } = 100L * 1024 * 1024 * 1024;

    public Result<bool>? Fail { get; set; }

    public Exception? Throw { get; set; }

    public int Copies { get; private set; }

    public ValueTask<long> UsedBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Used);

    public long? FreeBytes() => Free;

    public async ValueTask<Result<bool>> CopyAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Fail is not null)
        {
            return Fail;
        }

        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        var text = System.Text.Encoding.UTF8.GetString(copy.ToArray());
        lock (_gate)
        {
            Content.TryAdd(sha256, text);
            Events.Add("copy:" + sha256[..4]);
            var created = Stored.Add(sha256);
            Copies += created ? 1 : 0;
            return Result.Success(created);
        }
    }

    public int RebuildableCopies { get; private set; }

    public ValueTask<Result<bool>> CopyRebuildableAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            RebuildableCopies++;
        }

        return CopyAsync(source, sha256, expectedBytes, cancellationToken);
    }

    public Dictionary<string, string> Content { get; } = [];

    public ValueTask<Result<Stream>> OpenAsync(string sha256, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Content.TryGetValue(sha256, out var text) ? Result.Success<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))) : Result.Failure<Stream>(Error.Validation("unreadable", "missing")));
        }
    }
}

internal sealed class FakeSettings : IFilingSettings
{
    public int LagMinutes => 5;

    public long MaxFilesBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    public long MinFreeBytes { get; set; } = 20L * 1024 * 1024 * 1024;
}

internal sealed class FakeStore : IFilingStore
{
    public List<string> Events { get; set; } = [];

    public List<StoredFiling> Rows { get; } = [];

    public Exception? ThrowOnInsert { get; set; }

    public int Inserts { get; private set; }

    public ValueTask<StoredFiling?> FindByHashAsync(string contentSha256, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.ContentSha256 == contentSha256));

    public ValueTask<FilingInsertResult> InsertAsync(NewFiling filing, CancellationToken cancellationToken)
    {
        if (ThrowOnInsert is not null)
        {
            throw ThrowOnInsert;
        }

        var existing = Rows.FirstOrDefault(r => r.ContentSha256 == filing.ContentSha256);
        if (existing is not null)
        {
            return ValueTask.FromResult(new FilingInsertResult(filing.SameSidecar(existing) ? FilingInsertOutcome.Duplicate : FilingInsertOutcome.SidecarConflict, existing));
        }

        var group = filing.PeriodEnd is null ? [] : Rows.Where(r => r.InstrumentId == filing.InstrumentId && r.Kind == filing.Kind && r.PeriodEnd == filing.PeriodEnd && r.State == BroadcastState.Ok).ToList();
        var decision = PointInTimeRule.Decide(filing, group);
        if (decision.Outcome == RevisionOutcome.AmbiguousOrder)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.AmbiguousOrder));
        }

        if (decision.Outcome == RevisionOutcome.BadSupersede)
        {
            return ValueTask.FromResult(new FilingInsertResult(FilingInsertOutcome.BadSupersede));
        }

        Inserts++;
        Events.Add("insert:" + filing.ContentSha256[..4]);
        Rows.Add(new StoredFiling(filing.Id, filing.InstrumentId, filing.Kind, filing.PeriodEnd, filing.BroadcastTs, filing.State, filing.UnavailableReason, filing.IngestedAt, filing.ContentSha256, filing.FileBytes, filing.FileName, decision.Supersedes));
        return ValueTask.FromResult(new FilingInsertResult(decision.Outcome == RevisionOutcome.StoreOutOfOrder ? FilingInsertOutcome.StoredOutOfOrder : FilingInsertOutcome.Stored, null, decision.Supersedes));
    }
}
