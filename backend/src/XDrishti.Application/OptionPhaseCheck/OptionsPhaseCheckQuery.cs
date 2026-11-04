using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.OptionPhaseCheck;

/// <summary>The P14 phase check (spec P14-17): the exit "an option expression beats cash OOS after costs, else stays disabled" read from stored verdicts. Read only.</summary>
/// <param name="Cut">The families the owner leaves out of the overall (listed, not counted); unknown names are refused.</param>
public sealed record OptionsPhaseCheckQuery(IReadOnlyList<string> Cut) : IQuery<Result<OptionsPhaseCheckView>>;

/// <summary>The report with its two renderings; both come from the same report.</summary>
public sealed record OptionsPhaseCheckView(OptionsPhaseReport Report, string Text, string Json);
