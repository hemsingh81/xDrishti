using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Judges a running hypothesis with the experiment of P6-11 (honest rules, purged folds, trials) and moves it to Judged (P6-10, ADR 0076). The experiment is recorded in the ledger whatever its outcome and counts as a trial. Accepting is a separate move.
/// </summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="Options">The rules of the experiment (defaults: the doc 09 section 12 numbers).</param>
/// <param name="HoldOutMonths">Months kept out at the end of the data (6).</param>
/// <param name="Actor">Who judges.</param>
public sealed record JudgeHypothesisCommand(long Id, ExperimentOptions Options, int HoldOutMonths, string Actor) : ICommand;
