using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>"Restore defaults": writes the documented default thresholds (10 %, 20 %, 25 %, 15 % × 1.5, 3 positions, 35 %, 10 points over 180 days, 30 days). Audited; nothing changes when they are already in force.</summary>
public sealed record ResetActionThresholdsCommand(string Actor) : ICommand;

internal sealed class ResetActionThresholdsHandler(ActionThresholdsWriter writer) : ICommandHandler<ResetActionThresholdsCommand, ActionThresholdsView>
{
    public async ValueTask<Result<ActionThresholdsView>> HandleAsync(ResetActionThresholdsCommand command, CancellationToken cancellationToken) =>
        await writer.WriteAsync(ActionThresholds.Defaults, command.Actor, reset: true, cancellationToken);
}
