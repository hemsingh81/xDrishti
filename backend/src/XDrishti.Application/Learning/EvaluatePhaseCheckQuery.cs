using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>
/// Runs the phase check of P6-18 (ADR 0080): the roadmap exit criteria judged from stored records, plus one measurement, the time to read the two runs of the newest experiment and judge them again. Writes nothing.
/// </summary>
/// <param name="ReplaySeconds">Seconds a full replay of the candidate run took, timed by the owner outside this check; null when it was not timed. It is printed as supplied and never verified.</param>
public sealed record EvaluatePhaseCheckQuery(double? ReplaySeconds) : IQuery<Result<PhaseCheckView>>;
