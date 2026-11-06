namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>
/// Reads whether the P10 paper phase check says MET: true for MET, false for NOT MET, null when it cannot be read or is not evaluable (never a pass). A delegate, not a handler contract, on purpose (CF-11 part 1, ADR 0221): the readiness scorecard needs the one word and nothing else of
/// the phase check, and the check's handler reaches the schedule reader, which names every scheduled service. The cell lifecycle recorder (which the automatic pause also uses) reaches the readiness gate, so naming the handler there would make the whole set of services reachable from
/// the automatic kill path in the architecture test's closure, with no run-time path behind it: a pause is never judged by a gate. The delegate is built where the handler is registered.
/// </summary>
/// <param name="cancellationToken">Cancels the read.</param>
public delegate ValueTask<bool?> PaperPhaseMetReader(CancellationToken cancellationToken);
