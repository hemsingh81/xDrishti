using System.Text;

namespace XDrishti.Domain.Research;

/// <summary>One parameter of a candidate exit with its values: one value, or a ladder for the lists of a <c>PYRAMID</c> (P4-05, D1).</summary>
/// <param name="Name">The parameter name of doc 08 (<c>rr</c>, <c>t2_rr</c>, <c>add_at_rr</c> ...).</param>
/// <param name="Values">The values, in the order given; at least one.</param>
public sealed record ExitParameterValue(string Name, IReadOnlyList<double> Values);

/// <summary>One candidate exit after the lists of candidate values were expanded (P4-05, D1/D2): a policy and its parameters by name, sorted by name.</summary>
/// <param name="Policy">The policy as doc 08 names it (<c>FIXED_RR</c>, <c>PARTIAL_BE</c> ...).</param>
/// <param name="Parameters">The parameters sorted by ordinal name.</param>
public sealed record ExitCandidate(string Policy, IReadOnlyList<ExitParameterValue> Parameters)
{
    /// <summary>The stable spelling of the candidate, <c>FIXED_RR{rr=2}</c>: parameters in name order, a ladder joined by <c>|</c>, numbers in the canonical spelling.</summary>
    public string Id
    {
        get
        {
            var text = new StringBuilder(Policy).Append('{');
            for (var i = 0; i < Parameters.Count; i++)
            {
                text.Append(i == 0 ? string.Empty : ",").Append(Parameters[i].Name).Append('=').Append(string.Join('|', Parameters[i].Values.Select(CanonicalText.Number)));
            }

            return text.Append('}').ToString();
        }
    }
}

/// <summary>The <c>time_stop</c> overlay of a choice (doc 16 <c>exits.overlays</c>): applied to every candidate.</summary>
/// <param name="MinProgressRr">The least progress in R the trade must have made when the window ends.</param>
/// <param name="IntradayBars">Bars of the window for an intraday strategy; 0 when not given.</param>
/// <param name="SwingSessions">Sessions of the window for a swing strategy; 0 when not given.</param>
public sealed record TimeStopChoice(double MinProgressRr, int IntradayBars, int SwingSessions);

/// <summary>
/// The exits a run is asked to use in place of the built-in's own (P4-05, D1): the candidates, already expanded (one per combination of the candidate lists, in the order of the flags), and the overlay for all of them. Holds no strategy:
/// the engine turns it into the exits and overlays of a definition and leaves the entry, the stop and the time rules as they are, which is what makes the entries the same.
/// </summary>
/// <param name="Candidates">One to <see cref="ExitFlags.MaxCandidates"/> candidates with distinct ids.</param>
/// <param name="TimeStop">The overlay, or null.</param>
public sealed record ExitChoice(IReadOnlyList<ExitCandidate> Candidates, TimeStopChoice? TimeStop)
{
    /// <summary>The id of a candidate under this choice: its own id, plus <c>+time_stop</c> when the overlay is on.</summary>
    public string IdOf(ExitCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return TimeStop is null ? candidate.Id : candidate.Id + "+time_stop";
    }
}
