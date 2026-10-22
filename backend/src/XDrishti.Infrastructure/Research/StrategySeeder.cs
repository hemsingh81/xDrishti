using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// Writes the built-in strategies to the store from the texts embedded in this assembly (spec P3-02d, D5; ADR 0002, ADR 0056). Each text is read and compiled by the engine on every declared timeframe; a text that does not pass is logged at error
/// level and not stored (a bad seed is loud, and the equality test with the builders catches it before a release). Idempotent by hash: a restart writes nothing; a changed text for an id becomes the next version and the old one stays. Never
/// logs a text.
/// </summary>
internal sealed partial class StrategySeeder(IStrategyStore store, ISimulationEngine engine, ILogger<StrategySeeder> logger)
{
    private const string ResourceInfix = ".Seed.Strategies.";

    /// <summary>The seed texts embedded in this assembly, by resource name, in ordinal order.</summary>
    internal static IReadOnlyList<(string Name, string Text)> EmbeddedTexts()
    {
        var assembly = typeof(StrategySeeder).Assembly;
        var texts = new List<(string, string)>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(ResourceInfix, StringComparison.Ordinal) && n.EndsWith(".yaml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            texts.Add((name[(name.IndexOf(ResourceInfix, StringComparison.Ordinal) + ResourceInfix.Length)..], reader.ReadToEnd()));
        }

        return texts;
    }

    /// <summary>
    /// Seeds the embedded texts; the number of strategies that are stored after the call (written now or before). Only a failed <see cref="Domain.Common.Result"/> (a text that does not pass, an append the store refuses) is logged and skipped;
    /// a database exception is not caught and aborts <c>xd-api migrate</c>, so a broken database never reads as a partial seed.
    /// </summary>
    public ValueTask<int> SeedAsync(CancellationToken cancellationToken) => SeedAsync(EmbeddedTexts(), cancellationToken);

    /// <summary>Seeds the given texts (the embedded ones, or a test's).</summary>
    internal async ValueTask<int> SeedAsync(IEnumerable<(string Name, string Text)> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var stored = 0;
        foreach (var (name, text) in texts)
        {
            var described = engine.Describe(text);
            if (described.IsFailure)
            {
                LogRefused(logger, name, described.Error.Code, described.Error.Fields?.Count ?? 0);
                continue;
            }

            var d = described.Value;
            var appended = await store.AppendAsync(new NewVersion(d.Id, d.Style, text, d.CanonicalJson, d.Hash, Seed: true), cancellationToken);
            if (appended.IsFailure)
            {
                LogFailed(logger, d.Id, appended.Error.Code);
                continue;
            }

            LogSeeded(logger, d.Id, appended.Value.Version, d.Hash[..12], appended.Value.Created);
            stored++;
        }

        return stored;
    }

    [LoggerMessage(EventId = 4433, Level = LogLevel.Information, Message = "lab.seed.strategy {StrategyId} version {Version} hash {Hash}, created {Created}")]
    private static partial void LogSeeded(ILogger logger, string strategyId, int version, string hash, bool created);

    [LoggerMessage(EventId = 4434, Level = LogLevel.Error, Message = "lab.seed.refused {Resource}: {Code} with {Problems} problem(s); it is not stored")]
    private static partial void LogRefused(ILogger logger, string resource, string code, int problems);

    [LoggerMessage(EventId = 4435, Level = LogLevel.Error, Message = "lab.seed.failed {StrategyId}: {Code}")]
    private static partial void LogFailed(ILogger logger, string strategyId, string code);
}
