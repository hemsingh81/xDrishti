using XDrishti.Application.Market.History;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>Counts, over the instruments of one run, how many had their pre-seam bars admitted and how many were excluded, by first failing reason (CF-04d decision 10). An instrument with no seam row, or a switch that is off, is not counted.</summary>
internal sealed class AdmissionTally
{
    private readonly SortedDictionary<string, int> _excluded = new(StringComparer.Ordinal);

    private int _admitted;

    private bool _any;

    public void Count(PreSeamDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.HasSeamRow)
        {
            return;
        }

        _any = true;
        if (decision.IsAdmitted)
        {
            _admitted++;
        }
        else
        {
            var reason = decision.Reason ?? "unknown";
            _excluded[reason] = _excluded.GetValueOrDefault(reason) + 1;
        }
    }

    /// <summary>The record for the run, or null when no instrument was counted (every run with the switch off).</summary>
    public BackfillAdmissionRecord? Record() => _any ? new BackfillAdmissionRecord(_admitted, [.. _excluded.Select(e => new BackfillExclusion(e.Key, e.Value))]) : null;
}
