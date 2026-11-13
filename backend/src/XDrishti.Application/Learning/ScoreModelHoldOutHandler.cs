using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The owner's hold-out score of a model (CF-10e part 3). Refuses a non-owner actor before anything is read; the endpoint has checked the session, the CSRF token and a fresh password. One look, one pass, insert-only; see <see cref="ModelHoldOutScorer"/>.</summary>
internal sealed class ScoreModelHoldOutHandler(ModelHoldOutScorer scorer) : ICommandHandler<ScoreModelHoldOutCommand, MlHoldOutScoreView>
{
    public async ValueTask<Result<MlHoldOutScoreView>> HandleAsync(ScoreModelHoldOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MlActors.IsOwner(command.Actor)
            ? await scorer.ScoreAsync(command.ModelId, ModelHoldOutScorer.Owner, command.Actor, cancellationToken)
            : Result.Failure<MlHoldOutScoreView>(MlSafetyErrors.OwnerOnly());
    }
}
