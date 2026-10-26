#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Learning.Jobs;

/// <summary>Hand fixtures of the replay job tests (spec P8-18 Part B): a stored record as the store keeps it, its header, a fake engine that says which definition each call compiles to, and an in-memory job store that behaves like the table.</summary>
internal static class JobFixtures
{
    public const string BaselineKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public const string Owner = "owner:test@example.com";

    public static readonly string DefinitionSha = new('d', 64);

    public static readonly Guid InstrumentA = new("00000000-0000-0000-0000-00000000000a");

    public static readonly Guid InstrumentB = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A request as text, so that two requests compare by value (a record compares its lists by reference).</summary>
    public static string Describe(Application.Research.SimulationRequest r) =>
        string.Join('|', r.StrategyId, r.StrategyHash, string.Join(',', (r.Parameters ?? new Dictionary<string, double>()).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)), r.EvaluationTimeframe, r.From, r.To,
            string.Join(',', r.InstrumentIds ?? []), r.BasketId, r.Costs, r.AcknowledgeCsvAdjusted, r.Options, r.Fills, r.Exits is null ? "no exits" : string.Join(';', r.Exits.Candidates.Select(c => r.Exits.IdOf(c))) + "/" + r.Exits.TimeStop, r.StrategyVersion);

    public static string Fills => FillRules.Default.ToCanonicalJson();

    /// <summary>The definition text of a strategy with one exit: the value is a number, or the name of a parameter.</summary>
    public static string Definition(string exits = """[{"policy":"FIXED_RR","parameters":[{"name":"rr","values":["rr"]}]}]""", string? overlays = null) =>
        "{\"id\":\"ORB\",\"buy\":{\"exits\":" + exits + (overlays is null ? string.Empty : ",\"overlays\":" + overlays) + "}}";

    public static string Record(string? definition = null, string costs = "null", string universeKind = "instrument_list", string? basket = null, string fills = "", string timeframe = "15m", string sha = "")
    {
        var record = new
        {
            schema_version = 6,
            strategy = new { id = "ORB", parameters = new { or_min = 15, rr = 2 }, definition_json = definition ?? Definition(), definition_sha256 = sha.Length == 0 ? DefinitionSha : sha, evaluation_timeframe = timeframe, sides = new[] { "BUY" } },
            from = "2024-04-01",
            to = "2026-04-30",
            options = new { min_session_completeness = 0.9, discontinuity_threshold = 0.2 },
            acknowledge_csv_adjusted = false,
            fills = fills.Length == 0 ? Fills : fills,
            universe = new { kind = universeKind, basket_id = basket, basket_name = (string?)null, label = "x", members = new[] { new { id = InstrumentA, symbol = "NSE:AAA" }, new { id = InstrumentB, symbol = "NSE:BBB" } } },
        };
        var json = JsonSerializer.Serialize(record);
        return costs == "null" ? json : json[..^1] + ",\"costs\":" + costs + "}";
    }

    public const string SheetCosts = """{"sheet_id":"default","rates_sha256":"x","sheet_json":"{}","checked":null,"capital":1000000,"profile":{"name":"standard","risk_per_trade_pct":0.5,"max_capital_per_trade_pct":25},"max_cost_r":0.1}""";

    public static StoredRunHeader Header(string key = BaselineKey, int schema = 6, string? fills = null, string sha = "") =>
        new(1, key, "sim-6", schema, 5, "ORB", sha.Length == 0 ? DefinitionSha : sha, new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), "instrument_list", "x", 2, 2, 0, 0, 10, 0, false, fills ?? Fills, null, null, 100, new string('b', 64), new string('c', 64), DateTimeOffset.UnixEpoch, "cli:simulate");

    public static IPreparedStrategy Prepared(string sha)
    {
        var prepared = Substitute.For<IPreparedStrategy>();
        prepared.Record.Returns(new StrategyRecord("ORB", new Dictionary<string, double>(), "{}", sha, Timeframe.M15, [Side.Buy]));
        return prepared;
    }

    /// <summary>An engine whose compile gives the baseline's definition only when the exits asked for are the ones given (null: the strategy's own).</summary>
    public static ISimulationEngine Engine(Func<ExitChoice?, bool> reproduces, bool builtIn = true)
    {
        var engine = Substitute.For<ISimulationEngine>();
        engine.BuiltIns.Returns(builtIn ? [new BuiltInStrategyInfo("ORB", "x", [Timeframe.M15], Timeframe.M15, [])] : []);
        engine.Prepare(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, double>>(), Arg.Any<Timeframe>(), Arg.Any<FillRules?>(), Arg.Any<ExitChoice?>())
            .Returns(call => Result.Success(Prepared(reproduces(call.ArgAt<ExitChoice?>(4)) ? DefinitionSha : new string('0', 64))));
        engine.PrepareStored(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, double>>(), Arg.Any<Timeframe>(), Arg.Any<FillRules?>(), Arg.Any<ExitChoice?>())
            .Returns(call => Result.Success(Prepared(reproduces(call.ArgAt<ExitChoice?>(5)) ? DefinitionSha : new string('0', 64))));
        return engine;
    }

    public static IReplayStrategySource Source(string? text)
    {
        var source = Substitute.For<IReplayStrategySource>();
        source.StoredSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(text));
        return source;
    }

    /// <summary>A job store in memory that behaves like the table: one active job, the identical input returns its job, a daily limit that counts every state, a final state is final.</summary>
    internal sealed class FakeJobs : IExperimentJobStore
    {
        public List<ExperimentJob> Rows { get; } = [];

        public ValueTask<ExperimentJobInsert> InsertAsync(NewExperimentJob job, int maxPerDay, TimeSpan window, CancellationToken cancellationToken)
        {
            if (Rows.LastOrDefault(r => r.JobKey == job.JobKey && r.State is ExperimentJobState.Queued or ExperimentJobState.Running or ExperimentJobState.Succeeded) is { } same)
            {
                return ValueTask.FromResult(new ExperimentJobInsert(ExperimentJobInsertStatus.Existing, same, null));
            }

            if (Rows.FirstOrDefault(r => ExperimentJobStates.IsActive(r.State)) is { } active)
            {
                return ValueTask.FromResult(new ExperimentJobInsert(ExperimentJobInsertStatus.Active, active, null));
            }

            var inWindow = Rows.Where(r => r.CreatedAt >= job.At - window).ToList();
            if (inWindow.Count >= maxPerDay)
            {
                return ValueTask.FromResult(new ExperimentJobInsert(ExperimentJobInsertStatus.DailyLimit, null, inWindow.Min(r => r.CreatedAt) + window));
            }

            var row = new ExperimentJob(job.Id, job.JobKey, job.BaselineRunKey, job.KnobKind, job.KnobName, job.BaselineValue, job.CandidateValue, job.HoldOutMonths, job.Reason, ExperimentJobState.Queued, null, 0, 0, false, null, null, null, null, job.Instruments, null, null, false, job.CreatedBy, job.At, null, null, null, job.At);
            Rows.Add(row);
            return ValueTask.FromResult(new ExperimentJobInsert(ExperimentJobInsertStatus.Created, row, null));
        }

        public ValueTask<ExperimentJob?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public ValueTask<IReadOnlyList<ExperimentJob>> ListAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<ExperimentJob>>([.. Rows.AsEnumerable().Reverse().Take(limit)]);

        public ValueTask<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.Count(r => r.CreatedAt >= since));

        public ValueTask<ExperimentJob?> LatestMeasuredAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Rows.LastOrDefault(r => r.State == ExperimentJobState.Succeeded && r.ReplayMs is not null));

        public ValueTask<ExperimentJob?> ActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => ExperimentJobStates.IsActive(r.State)));

        public ValueTask<ExperimentJob?> ClaimNextAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var i = Rows.FindIndex(r => r.State == ExperimentJobState.Queued);
            if (i < 0)
            {
                return ValueTask.FromResult<ExperimentJob?>(null);
            }

            Rows[i] = Rows[i] with { State = ExperimentJobState.Running, Stage = "prepare", Host = host, StartedAt = now };
            return ValueTask.FromResult<ExperimentJob?>(Rows[i]);
        }

        public ValueTask<bool> ReportAsync(Guid id, string stage, int done, int total, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var i = Rows.FindIndex(r => r.Id == id && r.State == ExperimentJobState.Running);
            if (i < 0)
            {
                return ValueTask.FromResult(false);
            }

            Rows[i] = Rows[i] with { Stage = stage, Done = done, Total = total };
            return ValueTask.FromResult(true);
        }

        public ValueTask MarkOtherHeavyAsync(Guid id, CancellationToken cancellationToken)
        {
            var i = Rows.FindIndex(r => r.Id == id);
            Rows[i] = Rows[i] with { OtherHeavyJobsSeen = true };
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> FinishAsync(ExperimentJobFinish finish, CancellationToken cancellationToken)
        {
            var i = Rows.FindIndex(r => r.Id == finish.Id && r.State == ExperimentJobState.Running);
            if (i < 0)
            {
                return ValueTask.FromResult(false);
            }

            Rows[i] = Rows[i] with { State = finish.State, Stage = finish.Stage, CandidateRunKey = finish.CandidateRunKey, ExperimentId = finish.ExperimentId, ErrorCode = finish.ErrorCode, ErrorText = finish.ErrorText, ReplayMs = finish.ReplayMs, CompareMs = finish.CompareMs, OtherHeavyJobsSeen = finish.OtherHeavyJobsSeen, FinishedAt = finish.At };
            return ValueTask.FromResult(true);
        }

        public ValueTask<ExperimentJobCancelOutcome> RequestCancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var i = Rows.FindIndex(r => r.Id == id);
            if (i < 0)
            {
                return ValueTask.FromResult(ExperimentJobCancelOutcome.NotFound);
            }

            switch (Rows[i].State)
            {
                case ExperimentJobState.Queued:
                    Rows[i] = Rows[i] with { State = ExperimentJobState.Cancelled, FinishedAt = now };
                    return ValueTask.FromResult(ExperimentJobCancelOutcome.Cancelled);
                case ExperimentJobState.Running:
                    Rows[i] = Rows[i] with { CancelRequested = true };
                    return ValueTask.FromResult(ExperimentJobCancelOutcome.Requested);
                default:
                    return ValueTask.FromResult(ExperimentJobCancelOutcome.NotCancellable);
            }
        }

        public ValueTask<bool> IsCancelRequestedAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.Any(r => r.Id == id && r.CancelRequested));

        public ValueTask<IReadOnlyList<Guid>> InterruptRunningAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].State == ExperimentJobState.Running && Rows[i].Host == host)
                {
                    Rows[i] = Rows[i] with { State = ExperimentJobState.Interrupted, FinishedAt = now };
                    ids.Add(Rows[i].Id);
                }
            }

            return ValueTask.FromResult<IReadOnlyList<Guid>>(ids);
        }
    }

    internal sealed class RecordingProgress : IExperimentJobProgress
    {
        public List<ExperimentJobProgress> Progress { get; } = [];

        public List<ExperimentJobFinished> Finished { get; } = [];

        public ValueTask ProgressAsync(ExperimentJobProgress progress, CancellationToken cancellationToken)
        {
            lock (Progress)
            {
                Progress.Add(progress);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask FinishedAsync(ExperimentJobFinished finished, CancellationToken cancellationToken)
        {
            lock (Finished)
            {
                Finished.Add(finished);
            }

            return ValueTask.CompletedTask;
        }
    }
}
