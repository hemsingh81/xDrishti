using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

/// <summary>The read side of the result store alone (P14-17): the engine and charge sheet of one stored cash run. It calls <c>FindAsync</c> and nothing else.</summary>
internal sealed class CashRunHeaders(IRunStore runs) : ICashRunHeaders
{
    public async ValueTask<PhaseCashRun?> FindAsync(string runKey, CancellationToken cancellationToken)
    {
        var header = await runs.FindAsync(runKey, cancellationToken);
        return header is null ? null : new PhaseCashRun(header.RunKey, header.EngineVersion, header.CostsSheetId);
    }
}
