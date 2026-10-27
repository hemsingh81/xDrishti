namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The report engine as the assistant's tools read it (spec P9-05): the same handlers as the Reports screen, so there is one definition of expectancy, evidence and states. Gross basis only. Read only; a refusal of the engine comes back as its code in <see cref="AiToolData.ErrorCode"/>.
/// </summary>
public interface IAiReportReader
{
    /// <summary><paramref name="report"/> is summary, groups, pivot, what_works, equity, distribution or insights; <paramref name="query"/> holds the query-string pairs of the matching endpoint.</summary>
    Task<AiToolData> GetReportAsync(string report, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken);

    /// <summary>The stored simulation runs a report may use, with the default run set marked.</summary>
    Task<AiToolData> ListSimRunsAsync(CancellationToken cancellationToken);
}
