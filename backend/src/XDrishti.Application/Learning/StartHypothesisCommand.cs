using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Attaches the candidate run to an approved hypothesis and moves it to Running (P6-10, ADR 0076), after the one-variable rule holds between the two stored runs. The candidate run is made separately (the Lab or <c>xd simulate</c>) with the one knob changed.
/// </summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="CandidateRunKey">The stored candidate run (64 lower-case hex).</param>
/// <param name="Actor">Who attaches it.</param>
public sealed record StartHypothesisCommand(long Id, string CandidateRunKey, string Actor) : ICommand;
