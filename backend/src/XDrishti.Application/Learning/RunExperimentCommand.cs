using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Judges a knob change and records it in the experiment ledger (P6-11, ADR 0072). Both runs are stored runs made on the same data with the same entries; the candidate differs from the baseline in the one knob named. The replay that makes the candidate run
/// is a separate step (the Lab or <c>xd simulate</c>); this command compares the two results.
/// </summary>
/// <param name="BaselineRunKey">The stored run made with the baseline value (64 lower-case hex).</param>
/// <param name="CandidateRunKey">The stored run made with the candidate value.</param>
/// <param name="Knob">The knob and its two values.</param>
/// <param name="Options">The rules; <see cref="ExperimentOptions.Trials"/> can only raise the ledger count, and <see cref="ExperimentOptions.HoldOutStart"/> is set from <paramref name="HoldOutMonths"/>.</param>
/// <param name="HoldOutMonths">Months at the end of the data kept out of the experiment (6); 0 keeps none, and the report then says so.</param>
/// <param name="Actor">Who asks (<c>cli:xd</c> for the verb).</param>
public sealed record RunExperimentCommand(string BaselineRunKey, string CandidateRunKey, ExperimentKnob Knob, ExperimentOptions Options, int HoldOutMonths, string Actor) : ICommand;
