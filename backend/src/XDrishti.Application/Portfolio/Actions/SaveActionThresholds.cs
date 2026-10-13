using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>
/// Saves the thresholds of the Action-center rules (all ten values required, within the fixed ranges: below cost 1–90 %, high 2–95 % and above below cost, gain 1–1000 %, overweight floor 1–100 %, multiple 1–10, minimum positions 2–50, concentration 5–100 %,
/// lag 1–100 points, lag days 30–3650, long-term window 1–365 days); <c>portfolio.invalid</c> with a field error per problem otherwise. The next read uses them at once. Audited with the changed values; saving the values in force changes nothing.
/// </summary>
public sealed record SaveActionThresholdsCommand(ActionThresholdsInput Input, string Actor) : ICommand;

internal sealed class SaveActionThresholdsHandler(ActionThresholdsWriter writer) : ICommandHandler<SaveActionThresholdsCommand, ActionThresholdsView>
{
    public async ValueTask<Result<ActionThresholdsView>> HandleAsync(SaveActionThresholdsCommand command, CancellationToken cancellationToken)
    {
        var parsed = ActionThresholdsWriter.Parse(command.Input);
        return parsed.IsFailure
            ? Result.Failure<ActionThresholdsView>(parsed.Error)
            : await writer.WriteAsync(parsed.Value, command.Actor, reset: false, cancellationToken);
    }
}
