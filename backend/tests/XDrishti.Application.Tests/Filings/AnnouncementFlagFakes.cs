using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Tests.Filings;

/// <summary>The announcement flagger as a script: it records every input it was given and answers what the script says; <see cref="Down"/> makes it fail as a model server would. No model is called.</summary>
internal sealed class FakeFlagger : IAnnouncementFlagger
{
    public List<AnnouncementFlagInput> Calls { get; } = [];

    public Func<AnnouncementFlagInput, int, AnnouncementFlag> Script { get; set; } = (_, _) => Ok(false);

    /// <summary>The number of calls after which the server fails (null: it never does).</summary>
    public int? DownAfter { get; set; }

    public bool Down { get; set; }

    public static AnnouncementFlag Ok(bool material, string category = "other", decimal? order = null, bool ungrounded = false) =>
        new(ExtractionStates.Ok, null, new AnnouncementAnswer(material, category, "a short reason", order, "none", "none"), ungrounded, 10, 100, 20);

    public static AnnouncementFlag Invalid(string reason = ExtractionStates.NotJson) => new(ExtractionStates.InvalidOutput, reason, null, false, 10, 100, 20);

    public ValueTask<AnnouncementFlag> FlagAsync(AnnouncementFlagInput input, CancellationToken cancellationToken)
    {
        Calls.Add(input);
        if (Down || (DownAfter is { } after && Calls.Count > after))
        {
            throw new ModelUnavailableException();
        }

        return ValueTask.FromResult(Script(input, Calls.Count - 1));
    }
}

/// <summary>The extraction store in memory: the same key rules as the table (one row per filing, model, prompt and schema), the same order as the pending read.</summary>
internal sealed class MemoryExtractionStore : IAnnouncementExtractionStore
{
    public List<(Guid Filing, DateTimeOffset At)> Announcements { get; } = [];

    public List<NewExtraction> Rows { get; } = [];

    public ValueTask<IReadOnlyList<Guid>> PendingAsync(DateTimeOffset from, DateTimeOffset toExclusive, ExtractionKey key, int skip, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Guid>>([.. Announcements.Where(a => a.At >= from && a.At < toExclusive && !Rows.Any(r => r.FilingId == a.Filing && r.Key == key)).OrderBy(a => a.At).ThenBy(a => a.Filing).Skip(skip).Take(take).Select(a => a.Filing)]);

    public ValueTask<bool> InsertAsync(NewExtraction extraction, CancellationToken cancellationToken)
    {
        if (Rows.Any(r => r.FilingId == extraction.FilingId && r.Key == extraction.Key))
        {
            return ValueTask.FromResult(false);
        }

        Rows.Add(extraction);
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyList<ExtractionCoverageRow>> CoverageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ExtractionCoverageRow>>([.. Rows.GroupBy(r => (r.Key, r.State)).Select(g => new ExtractionCoverageRow("2026-07", g.Key.Key.ModelSha256, g.Key.Key.PromptName, g.Key.Key.PromptVersion, g.Key.Key.SchemaVersion, g.Key.State, g.Count(), g.Count(r => r.Ungrounded)))]);
}
