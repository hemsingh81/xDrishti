using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Learning;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Fakes and builders for the extension evaluations (P15-10): an in-memory store that enforces the same rules as the table, a ledger that logs the order of its calls, and populations built by hand.</summary>
internal static class EvaluationKit
{
    public sealed class Store(List<string> log) : IExtensionEvaluationStore
    {
        public List<ExtensionEvaluationRecord> Records { get; } = [];

        public ValueTask<Result<bool>> AppendAsync(ExtensionEvaluationRecord record, DateTimeOffset createdAt, CancellationToken cancellationToken)
        {
            log.Add("store.append");
            if (Records.Any(r => r.EvaluationId == record.EvaluationId))
            {
                return ValueTask.FromResult(Result.Success(false));
            }

            if (record.HoldOutRead && Records.Any(r => r.HoldOutRead && r.Family == record.Family && r.VersionKey == record.VersionKey))
            {
                return ValueTask.FromResult(Result.Failure<bool>(Error.Conflict("extensions.holdout_used", "read already")));
            }

            Records.Add(record);
            return ValueTask.FromResult(Result.Success(true));
        }

        public ValueTask<ExtensionEvaluationRecord?> GetAsync(string evaluationId, CancellationToken cancellationToken) => ValueTask.FromResult(Records.FirstOrDefault(r => r.EvaluationId == evaluationId));

        public ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ExtensionEvaluationRecord>>([.. Enumerable.Reverse(Records).Where(r => r.Family == family).Take(limit)]);

        public ValueTask<bool> HoldOutUsedAsync(string family, string versionKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Records.Any(r => r.HoldOutRead && r.Family == family && r.VersionKey == versionKey));
    }

    public sealed class Ledger(List<string> log, bool failing = false) : ITestLedger
    {
        public List<LedgerRow> Rows { get; } = [];

        public ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
        {
            log.Add("ledger.append");
            if (failing)
            {
                throw new InvalidOperationException("ledger down");
            }

            if (Rows.Any(r => r.RunId == row.RunId))
            {
                return ValueTask.FromResult(false);
            }

            Rows.Add(row);
            return ValueTask.FromResult(true);
        }

        public ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.Where(r => r.StrategyId == strategyId).Select(r => r.StrategyHash).Distinct().Count());

        public ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<LedgerRow>>([.. Rows.Where(r => r.StrategyId == strategyId)]);
    }

    public sealed class Population(FilterPopulation? value) : IFilterPopulationSource
    {
        public ValueTask<Result<FilterPopulation>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(value ?? new FilterPopulation([], "none", 30, [], [], 0)));
    }

    public sealed class Auto : ICommandHandler<ApplyEventFilterAutoCommand, EventFilterAutoResult>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<EventFilterAutoResult>> HandleAsync(ApplyEventFilterAutoCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Result.Success(new EventFilterAutoResult("none", "approval_manual")));
        }
    }

    public sealed class Switch(int days) : IEventFilterSwitch
    {
        public ValueTask<EventFilterState> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new EventFilterState(true, days, "auto", "auto"));
    }

    public sealed class Settings : IFilingSettings
    {
        public int LagMinutes => 30;

        public long MaxFilesBytes => 1;

        public long MinFreeBytes => 1;
    }

    public sealed class Calendars : ITradingCalendar
    {
        private readonly TradingCalendar _calendar = new();

        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_calendar);

        public void Invalidate()
        {
        }
    }

    public sealed class Trainer : IModelTrainer
    {
        public IScorerFactory Candidate { get; } = new LogisticScorer();

        public string Parameters => "test-trainer";

        public Result<TrainedModel> Train(IReadOnlyList<ValidationRow> rows) => throw new NotSupportedException();
    }

    public sealed class TrainingSource(IReadOnlyList<ExtensionRunSource> runs, ExtensionTrainingData? data) : IExtensionTrainingSource
    {
        public int Loads { get; private set; }

        public ValueTask<IReadOnlyList<ExtensionRunSource>> ListRunsAsync(string version, CancellationToken cancellationToken) => ValueTask.FromResult(runs);

        public ValueTask<Result<ExtensionTrainingData>> LoadAsync(ExtensionGroup group, IReadOnlyList<string> runKeys, CancellationToken cancellationToken)
        {
            Loads++;
            return ValueTask.FromResult(data is null ? Result.Failure<ExtensionTrainingData>(Error.Validation("learning.invalid", "none")) : Result.Success(data));
        }
    }

    public sealed class Pead(IReadOnlyList<PeadEvent> events) : IPeadEventSource
    {
        public ValueTask<PeadEventSet> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new PeadEventSet(events, "test"));
    }

    public sealed class Flag(FlagGrade? grade) : IFlagGradeSource
    {
        public ValueTask<FlagGrade?> NewestAsync(CancellationToken cancellationToken) => ValueTask.FromResult(grade);
    }

    public sealed record Rig(List<string> Log, Store Store, Ledger Ledger, Auto Auto, EvaluationWriter Writer);

    public static Rig NewRig(bool failingLedger = false)
    {
        var log = new List<string>();
        var store = new Store(log);
        var ledger = new Ledger(log, failingLedger);
        return new Rig(log, store, ledger, new Auto(), new EvaluationWriter(store, ledger, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero))));
    }

    public static FilterEvaluation Filter(Rig rig, FilterPopulation? population) => new(new Population(population), rig.Writer, rig.Store, rig.Auto, NullLogger<FilterEvaluation>.Instance);

    public static FilterObservation Obs(string key, DateOnly date, double r, string state, int? days = null, string? reason = null) =>
        new(key, date, r, new ResultsAnswer(state, days, null, null, null, reason));

    /// <summary>The planted-tail population of the domain tests as observations: per season 25 skipped (days 1), 100 taken (days 30), a gap-through tail of 20 % against 5 %, equal means, over 12 seasons (8 development, 4 reserved).</summary>
    public static FilterPopulation PlantedTail(int seasons = 12, int noUpcomingPerSeason = 0)
    {
        var trades = new List<FilterObservation>();
        for (var s = 0; s < seasons; s++)
        {
            var day = new DateOnly(2018 + (s / 4), ((s % 4) * 3) + 1, 1);
            for (var i = 0; i < 25; i++)
            {
                trades.Add(Obs($"s|{s}|{i}", day.AddDays(i), i < 5 ? -2.0 : 0.96875, ResultsAnswer.Value, 1));
            }

            for (var i = 0; i < 100; i++)
            {
                trades.Add(Obs($"t|{s}|{i}", day.AddDays(i % 80), i < 5 ? -2.0 : 0.5, ResultsAnswer.Value, 30));
            }

            for (var i = 0; i < noUpcomingPerSeason; i++)
            {
                trades.Add(Obs($"u|{s}|{i}", day.AddDays(i % 80), 0.1, ResultsAnswer.Unavailable, null, ResultsAnswer.NoUpcomingEntry));
            }
        }

        return new FilterPopulation([.. trades.OrderBy(t => t.Date).ThenBy(t => t.Key, StringComparer.Ordinal)], "cal-sha", 30, ["a".PadRight(64, 'a')], ["sim-8"], 0);
    }
}

internal static class ComparisonKit
{
    public const string RunKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public const string Clean = "{\"nan\":{},\"parser_unverified\":{}}";

    /// <summary>Five years of weekday rows, three a day, one base feature and the extension columns: the first extension column carries the label when planted, every other column is noise. R is +1 for a win and -1 for a loss.</summary>
    public static ExtensionTrainingData Rows(ExtensionGroup group, bool planted, int seed)
    {
        var random = new SeededRandom((ulong)seed + 11);
        var rows = new List<ValidationRow>();
        var index = 0;
        for (var d = new DateOnly(2019, 1, 1); d <= new DateOnly(2023, 12, 31); d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var i = 0; i < 3; i++)
            {
                var win = random.Next(1000) < 500;
                var features = new double[1 + group.Names.Count];
                features[0] = (win ? 0.05 : 0.0) + (random.Next(1000) / 1000.0);
                for (var c = 1; c < features.Length; c++)
                {
                    features[c] = random.Next(1000) / 1000.0;
                }

                if (planted)
                {
                    features[1] = (win ? 1.0 : 0.0) + (random.Next(1000) / 10000.0);
                }

                var epoch = new DateTimeOffset(d.Year, d.Month, d.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() + (i * 60L);
                rows.Add(new ValidationRow("t" + index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), win ? 1 : 0, win ? 1.0 : -1.0, d, epoch, epoch + 1800, "S|15m|BUY|intraday|X|*", [.. features]));
                index++;
            }
        }

        return new ExtensionTrainingData(rows, 1, new string('d', 64), "tb-1");
    }

    public static ExtensionRunSource Run(ExtensionGroup group, int closed, string? parser, string counts = Clean, int lag = 30) =>
        new(RunKey, "S", closed, new ExtensionExtractionRecord(RunKey, group.Id, group.Version, lag, parser, new string('c', 64), null, closed, counts, DateTimeOffset.UnixEpoch));

    public static ComparisonEvaluation Evaluation(EvaluationKit.Rig rig, EvaluationKit.TrainingSource source, string? parser = null) =>
        new(source, new EvaluationKit.Trainer(), new EvaluationKit.Settings(), new EvaluationKit.Calendars(), rig.Writer, rig.Ledger, rig.Store, NullLogger<ComparisonEvaluation>.Instance, parser);
}
