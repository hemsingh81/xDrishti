using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.History;

/// <summary>Runs the seam probe for the owner (CF-04e decision D5), in the request: one request per chosen instrument, written nowhere but the seam rows. The password was checked before this runs.</summary>
public sealed record ProbeHistorySeamCommand(string Actor) : ICommand;

/// <summary>Same order as the backfill without the dry run; one probe at a time per process, refused while the backfill service runs.</summary>
internal sealed class ProbeHistorySeamHandler(HistoryActGuard guard, HistoryProbeGate gate, ICommandHandler<ProbeSeamCommand, ProbeSeamSummary> probe) : ICommandHandler<ProbeHistorySeamCommand, ProbeSeamSummary>
{
    public async ValueTask<Result<ProbeSeamSummary>> HandleAsync(ProbeHistorySeamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!gate.TryEnter())
        {
            return await RefusedAsync(HistoryActErrors.RunActive(), command.Actor);
        }

        try
        {
            if (await guard.CheckAsync(cancellationToken) is { } refusal)
            {
                return await RefusedAsync(refusal, command.Actor);
            }

            var result = await probe.HandleAsync(new ProbeSeamCommand(), cancellationToken);
            if (result.IsFailure)
            {
                return await RefusedAsync(result.Error, command.Actor);
            }

            var s = result.Value;
            await guard.AuditAsync(AccountEventType.HistorySeamProbed, string.Create(CultureInfo.InvariantCulture, $"probed={s.Probed} passed={s.Passed} unresolved={s.Unresolved} no_overlap={s.NoOverlap} failed={s.Failed} requests={s.Requests}"), command.Actor);
            return result;
        }
        finally
        {
            gate.Leave();
        }
    }

    private async ValueTask<Result<ProbeSeamSummary>> RefusedAsync(Error error, string actor)
    {
        await guard.AuditAsync(AccountEventType.HistorySeamProbed, "refused=" + error.Code, actor);
        return Result.Failure<ProbeSeamSummary>(error);
    }
}
