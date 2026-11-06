using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>The one word of the P10 paper phase check that the readiness scorecard reads (row 15): MET is true, NOT MET is false, anything else (NOT EVALUABLE, a failed read) is null, which is never a pass.</summary>
internal static class PaperPhaseVerdict
{
    public static bool? Met(Result<PaperPhaseCheckView> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.IsFailure ? null : check.Value.Report.ExitVerdict switch
        {
            "MET" => true,
            "NOT MET" => false,
            _ => null,
        };
    }
}
