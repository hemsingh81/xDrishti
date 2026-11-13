using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd learn holdout-score MODEL-ID</c> (CF-10e part 3, ADR 0285): shows where the model stands with its one look at the locked hold-out and the stored figures. Read only on purpose: the look is irreversible and the spec makes it an owner act with a fresh password, which the command line
/// cannot ask for, so the scoring itself is <c>POST /api/learning/models/{id}/holdout-score</c> in the app (or the evening seal service while ml.holdout.auto_score is on). Nothing is claimed, scored or switched here.
/// </summary>
internal static class LearnHoldOutScoreCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnInfluenceOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync("usage:\n  xd learn holdout-score MODEL-ID\n    where the model stands with its one hold-out look and the stored figures; read only (the score is the owner's act in the app, with a fresh password)\nexit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled\n");
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var influence = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>>()
                .HandleAsync(new GetMlInfluenceQuery(options.ModelId), cancellationToken);
            if (influence.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(influence.Error.Code + ": " + influence.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in LearnReportText.Fit(LearnInfluenceCommand.HoldOut(influence.Value.HoldOut)))
            {
                await console.Out.WriteLineAsync(line);
            }

            await console.Out.WriteLineAsync("to score the model on the hold-out: the Models drawer of the app (owner, fresh password); the evening seal service does it for the pre-dated champion while ml.holdout.auto_score is on");
            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }
}
