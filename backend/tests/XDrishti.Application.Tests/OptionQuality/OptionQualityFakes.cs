using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionQuality;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Tests.OptionQuality;

/// <summary>The archive in memory: the lists a test sets are what the reader answers.</summary>
internal sealed class FakeArchive : IOptionArchiveReader
{
    public List<DateOnly> PendingChain { get; } = [];

    public Dictionary<DateOnly, List<ChainRequestSummary>> Summaries { get; } = [];

    public Dictionary<(Guid, DateOnly), List<ChainPoint>> Points { get; } = [];

    public HashSet<DateOnly> RecordedSessions { get; } = [];

    public HashSet<DateOnly> CheckedSessions { get; } = [];

    public DateOnly? First { get; set; }

    public List<Guid> Underlyings { get; } = [];

    public List<RollingWindow> PendingRolling { get; } = [];

    public Dictionary<string, List<RollingPoint>> Bars { get; } = [];

    public Exception? ThrowOnChainPoints { get; set; }

    public Exception? ThrowOnPendingChain { get; set; }

    /// <summary>The days a throw on chain points applies to (all when empty).</summary>
    public HashSet<DateOnly> ThrowOnDays { get; } = [];

    /// <summary>Fetch keys whose bars cannot be read.</summary>
    public HashSet<string> ThrowOnKeys { get; } = [];

    public Dictionary<(Guid, DateOnly), HashSet<DateOnly>> ActiveDays { get; } = [];

    public ValueTask<IReadOnlySet<DateOnly>> ActiveDaysAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<DateOnly>>(ActiveDays.TryGetValue((underlyingId, from), out var days) ? days : []);

    public ValueTask<IReadOnlyList<DateOnly>> PendingChainSessionsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken) =>
        ThrowOnPendingChain is { } broken ? throw broken : ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. PendingChain.Where(d => d <= upTo).Take(limit)]);

    public ValueTask<IReadOnlyList<ChainRequestSummary>> ChainRequestSummariesAsync(DateOnly session, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChainRequestSummary>>(Summaries.TryGetValue(session, out var list) ? list : []);

    public ValueTask<IReadOnlyList<ChainPoint>> ChainPointsAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken) =>
        ThrowOnChainPoints is { } failure && (ThrowOnDays.Count == 0 || ThrowOnDays.Contains(session)) ? throw failure : ValueTask.FromResult<IReadOnlyList<ChainPoint>>(Points.TryGetValue((underlyingId, session), out var list) ? list : []);

    public ValueTask<IReadOnlySet<DateOnly>> ChainSessionDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<DateOnly>>(RecordedSessions.Where(d => d >= from && d <= to).ToHashSet());

    public ValueTask<IReadOnlySet<DateOnly>> ChainResultDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<DateOnly>>(CheckedSessions.Where(d => d >= from && d <= to).ToHashSet());

    public ValueTask<DateOnly?> FirstChainSessionAsync(CancellationToken cancellationToken) => ValueTask.FromResult(First);

    public ValueTask<IReadOnlyList<Guid>> RecentChainUnderlyingsAsync(DateOnly since, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<Guid>>(Underlyings);

    public ValueTask<IReadOnlyList<RollingWindow>> PendingRollingWindowsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<RollingWindow>>([.. PendingRolling.Where(w => w.To <= upTo && !Done.Contains(w.FetchKey + "@" + w.From.DayNumber)).Take(limit)]);

    /// <summary>The windows a test marks as already checked (the fake store adds them as the runner writes).</summary>
    public HashSet<string> Done { get; } = [];

    public ValueTask<IReadOnlyList<RollingPoint>> RollingPointsAsync(RollingWindow window, CancellationToken cancellationToken) =>
        ThrowOnKeys.Contains(window.FetchKey) ? throw new InvalidOperationException("unreadable") : ValueTask.FromResult<IReadOnlyList<RollingPoint>>(Bars.TryGetValue(window.FetchKey, out var list) ? list : []);
}

internal sealed class FakeReference : IOptionReference
{
    public Dictionary<Guid, string> Symbols { get; } = [];

    public int? Step { get; set; } = 5_000;

    public List<LotSizeEntry> Lots { get; } = [];

    public List<MarketEpoch> Epochs { get; } = [];

    public ValueTask<IReadOnlyDictionary<Guid, string>> SymbolsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(Symbols.Where(s => ids.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value));

    public ValueTask<Guid?> FindUnderlyingAsync(string canonicalSymbol, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Symbols.Where(s => s.Value == canonicalSymbol).Select(s => (Guid?)s.Key).FirstOrDefault());

    public ValueTask<int?> StrikeStepAsync(Guid underlyingId, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(Step);

    public ValueTask<IReadOnlyList<LotSizeEntry>> LotSizesAsync(Guid underlyingId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<LotSizeEntry>>(Lots);

    public ValueTask<IReadOnlyList<MarketEpoch>> EpochsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MarketEpoch>>(Epochs);
}

internal sealed class FakeQualityStore : IOptionQualityStore
{
    private long _next;

    public List<(long Run, OptionQualityRow Row)> Rows { get; } = [];

    public List<(long Run, string Trigger, int Sessions, int Results, string? Note)> Finished { get; } = [];

    /// <summary>Called after every batch of results (a test moves the clock here).</summary>
    public Action? AfterAdd { get; set; }

    public ValueTask<long> StartRunAsync(CancellationToken cancellationToken) => ValueTask.FromResult(++_next);

    public FakeArchive? Archive { get; set; }

    public ValueTask AddResultsAsync(long runId, IReadOnlyList<OptionQualityRow> rows, CancellationToken cancellationToken)
    {
        Rows.AddRange(rows.Select(r => (runId, r)));
        foreach (var key in rows.Where(r => r.FetchKey is not null).Select(r => r.FetchKey!).Distinct())
        {
            var window = Archive?.PendingRolling.FirstOrDefault(w => w.FetchKey == key && rows.Any(r => r.SessionDate >= w.From && r.SessionDate < w.To));
            if (window is not null)
            {
                Archive!.Done.Add(window.FetchKey + "@" + window.From.DayNumber);
            }
        }

        AfterAdd?.Invoke();
        return ValueTask.CompletedTask;
    }

    public Exception? ThrowOnFinish { get; set; }

    public ValueTask FinishRunAsync(long runId, string trigger, DateTimeOffset startedAt, DateTimeOffset endedAt, int sessions, int results, string? note, CancellationToken cancellationToken)
    {
        if (ThrowOnFinish is { } failure)
        {
            throw failure;
        }

        Finished.Add((runId, trigger, sessions, results, note));
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<OptionQualityRow>> ChainResultsAsync(Guid underlyingId, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<OptionQualityRow>>([.. Newest().Where(r => r.UnderlyingId == underlyingId && r.Source == "chain" && r.SessionDate <= to)]);

    public ValueTask<IReadOnlyList<OptionQualityRow>> RollingResultsAsync(Guid underlyingId, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<OptionQualityRow>>([.. Newest().Where(r => r.UnderlyingId == underlyingId && r.Source == "rolling" && r.SessionDate >= from && r.SessionDate <= to)
            .GroupBy(r => (r.SessionDate, r.FetchKey)).OrderBy(g => g.Key.SessionDate).ThenBy(g => g.Key.FetchKey, StringComparer.Ordinal).Take(limit).SelectMany(g => g)]);

    private IEnumerable<OptionQualityRow> Newest() =>
        Rows.GroupBy(r => (r.Row.SessionDate, r.Row.Source, r.Row.FetchKey, r.Row.Check.Code)).Select(g => g.MaxBy(x => x.Run).Row).OrderBy(r => r.SessionDate);
}

internal sealed class QualityWorld
{
    public static readonly Guid Nifty = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public static readonly Guid BankNifty = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    public QualityWorld(DateTimeOffset? now = null)
    {
        Clock = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 14, 16, 15, 0, TimeSpan.FromMinutes(330)));
        Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        Settings = new TestSettings();
        Store.Archive = Archive;
        Reference.Symbols[Nifty] = "NSE:NIFTY";
        Reference.Symbols[BankNifty] = "NSE:BANKNIFTY";
    }

    public FakeTimeProvider Clock { get; }

    public FakeArchive Archive { get; } = new();

    public FakeReference Reference { get; } = new();

    public FakeQualityStore Store { get; } = new();

    public ITradingCalendar Calendar { get; } = Substitute.For<ITradingCalendar>();

    public TestSettings Settings { get; }

    public OptionArchiveQualityRunner Runner() => new(Archive, Reference, Store, Calendar, Settings, Clock, NullLogger<OptionArchiveQualityRunner>.Instance);

    public GetOptionCoverageHandler Handler() => new(Reference, Store, Clock);
}

internal sealed class TestSettings : IOptionQualitySettings
{
    public OptionQualityThresholds Thresholds { get; set; } = new();

    public int LookbackDays { get; set; } = 10;

    public int MaxChainSessionsPerRun { get; set; } = 20;

    public int MaxRollingWindowsPerRun { get; set; } = 200;

    public int MaxRunMinutes { get; set; } = 30;
}
