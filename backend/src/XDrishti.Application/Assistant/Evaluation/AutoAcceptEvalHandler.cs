using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The auto option of the baseline after an eval run (ADR 0292): accepts the run when the setting is on and no set regressed.</summary>
public sealed record AutoAcceptEvalCommand(Guid RunId) : ICommand;

/// <summary>What the auto option did: <c>accepted</c>, <c>off</c>, <c>unavailable</c> (the setting could not be read), <c>not_eligible</c> (not a gating PASS, or a set regressed) or <c>already</c>.</summary>
public sealed record EvalAutoAcceptOutcome(string State, string Detail);

/// <summary>Sets <c>ai.eval.auto_accept</c> as the owner through his own verb (the same actor that accepts a baseline by hand).</summary>
public sealed record SetEvalAutoAcceptCommand(bool On, string Actor) : ICommand;

/// <summary>
/// The auto option of the eval baseline re-accept (ADR 0292, rule of ADR 0282): with <c>ai.eval.auto_accept</c> ON (the default, as no evidence is lost when nothing regresses) a gating PASS on which no set got worse than the current baseline is accepted as
/// <c>auto:xd</c>, with the setting snapshot as the reason, so it is never mistaken for the owner's act. A run that would need a reason is left to the owner. An unreadable setting never accepts. It applies nothing else.
/// </summary>
internal sealed class AutoAcceptEvalHandler(IEvalStore store, IEssentialSettingStore settings, TimeProvider time, ILogger<AutoAcceptEvalHandler> logger) : ICommandHandler<AutoAcceptEvalCommand, EvalAutoAcceptOutcome>
{
    public const string Key = "ai.eval.auto_accept";

    public const string AutoActor = "auto:xd";

    public async ValueTask<Result<EvalAutoAcceptOutcome>> HandleAsync(AutoAcceptEvalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        StoredEssential? setting;
        try
        {
            setting = await settings.GetAsync(Key, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return Result.Success(new EvalAutoAcceptOutcome("unavailable", "the setting " + Key + " could not be read, so nothing was accepted"));
        }

        var on = setting is null || setting.Value == "true";
        var snapshot = setting is null
            ? $"{Key}=true (default, ADR 0292)"
            : string.Create(CultureInfo.InvariantCulture, $"{Key}={setting.Value} (set by {setting.UpdatedBy} at {setting.UpdatedAt:yyyy-MM-ddTHH:mm:ssZ})");
        if (!on)
        {
            return Result.Success(new EvalAutoAcceptOutcome("off", snapshot));
        }

        if (await store.GetAsync(command.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<EvalAutoAcceptOutcome>(EvalErrors.NotFound(command.RunId));
        }

        var current = await store.CurrentBaselineAsync(cancellationToken);
        if (current?.RunId == run.Id)
        {
            return Result.Success(new EvalAutoAcceptOutcome("already", "the run is the baseline"));
        }

        var summary = EvalMapping.SummaryOf(run);
        var currentRun = current is null ? null : await store.GetAsync(current.RunId, cancellationToken);
        var baseline = currentRun is null ? null : EvalMapping.SummaryOf(currentRun);
        if (!EvalAcceptance.AutoEligible(summary with { NonGating = !run.Gating || summary.NonGating }, baseline))
        {
            return Result.Success(new EvalAutoAcceptOutcome("not_eligible", "not a gating PASS, or a set got worse than the baseline: the owner decides (xd eval accept)"));
        }

        var (_, created) = await store.InsertBaselineAsync(run.Id, AutoActor, "auto: " + snapshot, time.GetUtcNow(), cancellationToken);
        EvalLog.Accepted(logger, run.Id, created);
        return Result.Success(new EvalAutoAcceptOutcome("accepted", snapshot));
    }
}

/// <summary>Sets the switch; only the owner's verb may (the same rule as accepting by hand).</summary>
internal sealed class SetEvalAutoAcceptHandler(IEssentialSettingStore settings, TimeProvider time) : ICommandHandler<SetEvalAutoAcceptCommand, EvalAutoAcceptOutcome>
{
    public async ValueTask<Result<EvalAutoAcceptOutcome>> HandleAsync(SetEvalAutoAcceptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor != EvalErrors.CliActor)
        {
            return Result.Failure<EvalAutoAcceptOutcome>(Error.Forbidden("assistant.owner_only", "Only the owner's own verb changes the eval auto-accept setting."));
        }

        var value = command.On ? "true" : "false";
        await settings.SetAsync(AutoAcceptEvalHandler.Key, value, command.Actor, time.GetUtcNow(), cancellationToken);
        return Result.Success(new EvalAutoAcceptOutcome(command.On ? "on" : "off", $"{AutoAcceptEvalHandler.Key}={value}"));
    }
}
