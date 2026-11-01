using XDrishti.Application.Abstractions.Options;
using XDrishti.Domain.Options;

namespace XDrishti.Application.Tests.Options;

/// <summary>The option setting and store in memory: the setting row, the turnover ranking, the master list's underlyings, and what the store was asked to write.</summary>
internal sealed class InMemoryOptionData : IOptionUnderlyingSetting, IOptionContractStore
{
    /// <summary>The stored setting value; the default (an empty list) keeps no contract, so refresh tests that do not care see no option step.</summary>
    public string? Setting { get; set; } = "[]";

    public string? SettingActor { get; private set; }

    public List<string> MostTraded { get; } = [];

    /// <summary>The India date of the newest daily bar behind the ranking.</summary>
    public DateOnly? NewestBar { get; set; } = new DateOnly(2026, 10, 31);

    public Dictionary<string, Guid> Underlyings { get; } = new(StringComparer.Ordinal);

    public List<(IReadOnlyList<OptionContractCandidate> Contracts, IReadOnlyList<LotObservation> Lots, IReadOnlyList<LotSizeSeed> Seeds, DateOnly Today)> Applied { get; } = [];

    public OptionContractsApplied Result { get; set; } = new(0, 0, 0, 0, 0, 0);

    public Exception? ApplyFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) =>
        ReadFailure is null ? ValueTask.FromResult(Setting) : throw ReadFailure;

    public ValueTask<RankedShares> MostTradedFnoSharesAsync(DateOnly before, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RankedShares([.. MostTraded.Take(take)], MostTraded.Count == 0 ? null : NewestBar));

    public ValueTask<bool> FixDefaultAsync(string value, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Setting is not null)
        {
            return ValueTask.FromResult(false);
        }

        Setting = value;
        SettingActor = actor;
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyDictionary<string, Guid>> ResolveUnderlyingsAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, Guid>>(Underlyings.Where(u => canonicalSymbols.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value, StringComparer.Ordinal));

    public ValueTask<OptionContractsApplied> ApplyAsync(
        IReadOnlyList<OptionContractCandidate> contracts, IReadOnlyList<LotObservation> lots, IReadOnlyList<LotSizeSeed> seeds, DateOnly today, CancellationToken cancellationToken)
    {
        if (ApplyFailure is not null)
        {
            throw ApplyFailure;
        }

        Applied.Add((contracts, lots, seeds, today));
        return ValueTask.FromResult(Result);
    }
}
