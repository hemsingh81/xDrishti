using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// A strategy ready to be replayed, as the use case sees it (spec P3-04a, D6): opaque. It exposes only the record a run keeps of the strategy and the warnings of its compile; the engine behind the port holds the compiled sides
/// and hands the handle back to <see cref="ISimulationEngine.Replay"/>. Application cannot name the types of the simulation library, so the handle is an interface made here and implemented by the adapter in Infrastructure.
/// </summary>
public interface IPreparedStrategy
{
    /// <summary>The strategy as a run records it (of the first candidate for a comparison): id, parameters, canonical JSON and its SHA-256, timeframe and sides.</summary>
    StrategyRecord Record { get; }

    /// <summary>The candidates in order (P4-05): one for a plain run or a single chosen exit, two or more for a comparison.</summary>
    IReadOnlyList<CandidateRecord> Candidates { get; }

    /// <summary>The strategy holds its positions overnight (a swing strategy): a comparison of its candidates is not gated (P4-05, D4).</summary>
    bool HoldsOvernight { get; }

    /// <summary>The strategy reads the daily market context (P3-lift, ADR 0145): a run gives the replay the stored context days, and an instrument is skipped with <c>context_unavailable</c> when there are none.</summary>
    bool ReadsContext { get; }

    /// <summary>The strategy names the filter <c>not_event_day</c> (P15-06): a run gives the replay a results-calendar snapshot, is refused with <c>simulation.filter_not_available</c> when the window has no calendar row at all, and an instrument is skipped with <c>event_calendar_unavailable</c> when it is given none.</summary>
    bool ReadsEvents { get; }

    /// <summary>The strategy names <c>days_to_results</c> or <c>days_since_results</c> (P15-05, ADR 0185): a run gives the replay the results-calendar snapshot and the results filing times, and is refused with <c>simulation.filter_not_available</c> when the window has neither a calendar row nor a filing.</summary>
    bool ReadsResultsDays { get; }

    /// <summary>The strategy names <c>results_sue_q</c> or <c>results_car3_q</c> (P15-09): a run also gives the replay the surprise numbers of the universe, from which the breakpoints of the previous complete season are made; an instrument is skipped with the reason <c>event_calendar_unavailable</c> and the cause <c>no_surprise_inputs</c> when the inputs of the replay carry none. Default: false.</summary>
    bool ReadsSurprise => false;

    /// <summary>The warnings of the compile (path in <see cref="FieldError.Field"/>, code in <see cref="FieldError.Code"/>); counted in the log, not stored in the run record.</summary>
    IReadOnlyList<FieldError> Warnings { get; }
}
