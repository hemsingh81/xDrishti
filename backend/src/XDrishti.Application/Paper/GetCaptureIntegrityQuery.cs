using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Paper;

/// <summary>
/// Criterion I of the paper phase check (capture integrity) for the newest epoch, up to the last closed session, and nothing else of the check (CF-11, ADR 0225). It exists so that the readiness scorecard reads this one criterion without the whole check, whose handler reaches
/// every scheduled service. Read only: no replay is started, nothing is written.
/// </summary>
public sealed record GetCaptureIntegrityQuery : IQuery<CaptureIntegrityView>;

/// <summary>Criterion I as the phase check judges it.</summary>
/// <param name="Verdict"><c>MET</c>, <c>NOT MET</c> or <c>NOT EVALUABLE</c> (no capture run in the epoch yet, or no epoch).</param>
/// <param name="Label">The criterion's own words, with the reason.</param>
/// <param name="CaptureRuns">The capture rows read.</param>
/// <param name="Through">The last closed session read; null when there is no epoch.</param>
/// <param name="Facts">What the criterion faced (counts, the sessions that disagreed or stay pending).</param>
public sealed record CaptureIntegrityView(string Verdict, string Label, int CaptureRuns, DateOnly? Through, IReadOnlyList<string> Facts);
