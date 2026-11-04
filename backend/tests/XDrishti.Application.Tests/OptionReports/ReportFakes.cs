using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Application.Tests.OptionReports;

internal sealed class FakeReportReader : IOptionReportReader
{
    public List<Guid> Underlyings { get; } = [];

    public List<ExpressionRunStored> Runs { get; } = [];

    public List<PairFormationStored> Formations { get; } = [];

    /// <summary>The limits and ranges the handlers asked for: every read is bounded.</summary>
    public List<string> Asked { get; } = [];

    public ValueTask<IReadOnlyList<Guid>> ArchiveUnderlyingsAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        Asked.Add($"underlyings {from:yyyy-MM-dd}..{to:yyyy-MM-dd} limit {limit}");
        return ValueTask.FromResult<IReadOnlyList<Guid>>([.. Underlyings.Take(limit)]);
    }

    public ValueTask<IReadOnlyList<ExpressionRunStored>> ExpressionRunsAsync(string? profileHash, int limit, CancellationToken cancellationToken)
    {
        Asked.Add($"runs profile {profileHash ?? "all"} limit {limit}");
        return ValueTask.FromResult<IReadOnlyList<ExpressionRunStored>>([.. Runs.Where(r => profileHash is null || r.ProfileHash == profileHash).OrderByDescending(r => r.CreatedAt).Take(limit)]);
    }

    public ValueTask<ExpressionRunStored?> ExpressionRunAsync(string runId, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.FirstOrDefault(r => r.RunId == runId));

    public ValueTask<IReadOnlyList<PairFormationStored>> PairFormationsAsync(IReadOnlyList<string> families, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        Asked.Add($"formations {from:yyyy-MM-dd}..{to:yyyy-MM-dd} limit {limit}");
        return ValueTask.FromResult<IReadOnlyList<PairFormationStored>>([.. Formations.Where(f => families.Contains(f.Family) && f.WindowTo >= from && f.WindowFrom <= to).Take(limit)]);
    }
}

internal sealed class FakeSpreadModels : ISpreadModelStore
{
    public Dictionary<Guid, HalfSpreadModel> Models { get; } = [];

    public ValueTask<bool> ExistsAsync(string modelId, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<bool> AppendAsync(BuiltSpreadModel model, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<HalfSpreadModel?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken) => ValueTask.FromResult(Models.GetValueOrDefault(underlyingId));
}

internal sealed class FakeEvaluationReader : IExtensionEvaluationReader
{
    public List<ExtensionEvaluationRecord> Records { get; } = [];

    public List<(string Family, int Limit)> Asked { get; } = [];

    public ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken)
    {
        Asked.Add((family, limit));
        return ValueTask.FromResult<IReadOnlyList<ExtensionEvaluationRecord>>([.. Records.Where(r => r.Family == family).Take(limit)]);
    }

    public static ExtensionEvaluationRecord Record(string family, string verdict, string protocol, int foldsTotal, int foldsEvaluable, IReadOnlyList<string> missing, bool holdOut = false) =>
        new(family.Replace(':', '-').PadRight(64, '0'), family, protocol, null, "v-1", "none", new string('a', 64), null, null, 3, 1.4, "quarter", foldsTotal, foldsEvaluable, 0, null, "{\"mean_d\":\"unavailable\"}", verdict, missing, ["gross of charges"], "{}", "{}", holdOut);
}

internal sealed class FakeBackfillLog : IBackfillFetchLog
{
    public Dictionary<BackfillKey, KeyState> States { get; } = [];

    /// <summary>Adds keys of an underlying: finished with bars, finished empty, finished with no data, and given up.</summary>
    public void Add(Guid underlying, int ok, int empty, int noData, int givenUp)
    {
        var n = 0;
        BackfillKey Next() => new(underlying, 'W', 1, n++, "CE", 5, new DateOnly(2025, 5, 20));
        for (var i = 0; i < ok; i++) { States[Next()] = new KeyState(1, 0, BackfillOutcomes.Ok); }
        for (var i = 0; i < empty; i++) { States[Next()] = new KeyState(1, 0, BackfillOutcomes.Empty); }
        for (var i = 0; i < noData; i++) { States[Next()] = new KeyState(1, 0, BackfillOutcomes.NoData); }
        for (var i = 0; i < givenUp; i++) { States[Next()] = new KeyState(3, KeyState.MaxFailedAttempts, null); }
    }

    public ValueTask<IReadOnlyDictionary<BackfillKey, KeyState>> LoadStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyDictionary<BackfillKey, KeyState>>(States);

    public ValueTask<int> CountSentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => ValueTask.FromResult(0);
}
