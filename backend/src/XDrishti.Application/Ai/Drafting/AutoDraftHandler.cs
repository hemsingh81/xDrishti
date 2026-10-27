using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// The auto option (spec P9-07 AC 13, D9; the owner's rule that anything with a manual approval can also run automatically, default OFF). With the confirm handler, this is the only type of the drafting code that stores a strategy or starts a Lab run. It acts only when ALL hold,
/// and every doubt leaves the draft <c>valid</c> for the owner: the setting <c>auto_lab_quick_test</c> is on now (an owner act with a fresh password), drafting and the assistant are on, the draft came from the dedicated job on an idea an owner typed (never the chat tool, whose
/// text stored data can steer), it is valid with no finding, it has no decision, its idea names exactly one existing basket by exact name, its id is free, and fewer than 1 auto run in the last hour and 3 on this India date were made (counted from the decision rows, under the
/// confirm lock, so concurrent drafts cannot pass together). An input that cannot be read (the baskets) counts as no pass. It stores the text with the note, starts a QUICK test only, and writes the decision as auto with the settings row id and hash. It places no order and has no
/// order port.
/// </summary>
internal sealed class AutoDraftHandler(
    IDraftStore store,
    IAssistantSettingsStore settings,
    IBasketQueries baskets,
    IStrategyChecker checker,
    IStrategyStore strategies,
    ICommandHandler<CreateStrategyCommand, CreatedStrategy> create,
    ICommandHandler<StartLabRunCommand, StartedLabRun> start,
    DraftOptions options,
    TimeProvider time,
    ILogger<AutoDraftHandler> logger) : ICommandHandler<AutoDraftCommand, AutoDraftOutcome>
{
    public const string Actor = "system:auto-draft";
    public const int PerHour = 1;
    public const int PerDay = 3;

    public async ValueTask<Result<AutoDraftOutcome>> HandleAsync(AutoDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var outcome = await DecideAsync(command.DraftId, cancellationToken);
        DraftLog.Auto(logger, command.DraftId, outcome.Started, outcome.Reason);
        return Result.Success(outcome);
    }

    private async ValueTask<AutoDraftOutcome> DecideAsync(long draftId, CancellationToken cancellationToken)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        if (!options.Enabled || !current.Enabled || !current.AutoLabQuickTest || current.Id is null || current.SnapshotHash is null)
        {
            return Left("off");
        }

        await using var confirmLock = await store.LockConfirmAsync(cancellationToken);
        var draft = await store.GetAsync(draftId, cancellationToken);
        if (draft is null || draft.Via != "job" || !OwnerConfirmation.IsOwnerActor(draft.RequestedBy))
        {
            return Left("not_an_owner_job_draft");
        }

        if (draft.Status != DraftStatus.Valid || draft.Findings.Count > 0 || string.IsNullOrWhiteSpace(draft.Yaml))
        {
            return Left("not_clean");
        }

        if (await store.DecisionAsync(draftId, cancellationToken) is not null)
        {
            return Left("decided");
        }

        var check = checker.Check(draft.Yaml);
        if (check.Description is not { } description)
        {
            return Left("not_valid_now");
        }

        Guid basketId;
        try
        {
            var found = BasketsNamedIn(draft.Idea, await baskets.ListAsync(false, cancellationToken));
            if (found.Count != 1)
            {
                return Left(found.Count == 0 ? "no_basket_named" : "many_baskets_named");
            }

            basketId = found[0];
        }
#pragma warning disable CA1031 // an unreadable input is no pass: the draft waits for the owner
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return Left("basket_unavailable");
        }

        if (await strategies.GetAsync(description.Id, cancellationToken) is not null)
        {
            return Left("id_taken");
        }

        var now = time.GetUtcNow();
        var dayStart = new DateTimeOffset(IndiaTime.Today(now).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        if (await store.CountAutoBetweenAsync(now.AddHours(-1), now.AddDays(1), cancellationToken) >= PerHour)
        {
            return Left("limit_hour");
        }

        if (await store.CountAutoBetweenAsync(dayStart, dayStart.AddDays(1), cancellationToken) >= PerDay)
        {
            return Left("limit_day");
        }

        var created = await create.HandleAsync(new CreateStrategyCommand(draft.Yaml, $"Assistant draft {draft.Id}; auto-confirmed ({Actor})"), cancellationToken);
        if (created.IsFailure)
        {
            return Left("create_refused");
        }

        var started = await start.HandleAsync(new StartLabRunCommand(created.Value.Id, created.Value.Version, basketId, null, "quick", null, null), cancellationToken);
        await store.InsertDecisionAsync(
            new NewDraftDecision(draft.Id, "confirmed", created.Value.Id, created.Value.Version, started.IsSuccess ? started.Value.Run.Id : null, false, true, current.Id, current.SnapshotHash, Actor, started.IsSuccess ? null : started.Error.Code),
            CancellationToken.None);
        return new AutoDraftOutcome(true, started.IsSuccess ? "started" : "run_not_started");
    }

    /// <summary>The baskets whose exact name occurs in the idea (case-sensitive, not inside a longer word).</summary>
    internal static List<Guid> BasketsNamedIn(string idea, IReadOnlyList<BasketView> all)
    {
        var found = new List<Guid>();
        foreach (var basket in all.Where(b => b.ArchivedAt is null && b.Name.Length > 0))
        {
            for (var at = idea.IndexOf(basket.Name, StringComparison.Ordinal); at >= 0; at = idea.IndexOf(basket.Name, at + 1, StringComparison.Ordinal))
            {
                var before = at == 0 || !char.IsLetterOrDigit(idea[at - 1]);
                var end = at + basket.Name.Length;
                if (before && (end == idea.Length || !char.IsLetterOrDigit(idea[end])))
                {
                    found.Add(basket.Id);
                    break;
                }
            }
        }

        return found;
    }

    private static AutoDraftOutcome Left(string reason) => new(false, reason);
}
