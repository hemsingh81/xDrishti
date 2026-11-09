using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LearnedSettings;

/// <summary>The log of learned changes with the guardrails (read only).</summary>
internal sealed class GetLearnedChangesHandler(ILearnedSettingStore store, ILearnedConfig config, IAutoSwitch auto, LearnedSettingsOptions options, TimeProvider clock) : IQueryHandler<GetLearnedChangesQuery, LearnedChangesView>
{
    public const int Limit = 100;

    public async ValueTask<LearnedChangesView> HandleAsync(GetLearnedChangesQuery query, CancellationToken cancellationToken)
    {
        var rows = await store.ListAsync(Limit, cancellationToken);
        var used = await store.AutoAppliedSinceAsync(clock.GetUtcNow().AddDays(-LearnedSnapshot.WeekDays), cancellationToken);
        var current = (await store.CurrentAppliedAsync(cancellationToken)).Select(r => r.Id).ToHashSet();
        var allow = LearnedAllowList.Keys.Order(StringComparer.Ordinal)
            .Select(k => config.Descriptor(k))
            .OfType<SettingDescriptor>()
            .Select(d => new LearnedAllowedSetting(d.Key, d.Label, d.Safer.ToString().ToLowerInvariant(), d.HasRange ? new SettingRange(d.Min, d.Max) : null))
            .ToList();
        return new LearnedChangesView(
            await auto.IsOnAsync(cancellationToken), options.WeeklyCap, used, "An applied change is read by the host at its next start; the running values do not move meanwhile.", allow, [.. rows.Select(r => LearnedViews.Of(r, config, current.Contains(r.Id)))]);
    }
}

/// <summary>One change, with whether a password is needed.</summary>
internal sealed class GetLearnedChangeHandler(ILearnedSettingStore store, ILearnedConfig config) : IQueryHandler<GetLearnedChangeQuery, LearnedChangeView?>
{
    public async ValueTask<LearnedChangeView?> HandleAsync(GetLearnedChangeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var row = await store.FindAsync(query.Id, cancellationToken);
        return row is null ? null : LearnedViews.Of(row, config, (await store.CurrentAppliedAsync(cancellationToken)).Any(r => r.Id == row.Id));
    }
}

/// <summary>The snapshot of the settings a decision was made under, and its hash.</summary>
internal static class LearnedSnapshot
{
    public const int WeekDays = 7;

    public static (string Json, string Hash) Make(bool autoOn, LearnedSettingsOptions options, IReadOnlyDictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteBoolean("auto", autoOn);
            w.WriteNumber("weeklyCap", options.WeeklyCap);
            w.WriteNumber("maxStepPercent", options.MaxStepPercent);
            w.WriteNumber("maxTotalPercent", options.MaxTotalPercent);
            w.WriteStartObject("values");
            foreach (var (k, v) in values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                w.WriteString(k, v);
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        return (json, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }

    /// <summary>The value in force for the allow-listed keys: the newest applied value, else the configured one.</summary>
    public static Dictionary<string, string> Effective(ILearnedConfig config, IReadOnlyDictionary<string, string> applied)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in LearnedAllowList.Keys)
        {
            var v = applied.TryGetValue(key, out var a) ? a : config.ConfiguredValue(key);
            if (v is not null)
            {
                values[key] = v;
            }
        }

        return values;
    }
}

/// <summary>
/// The weekly learning run for settings (AU-10, ADR 0242, ADR 0247). For each piece of evidence the pure proposer decides; the whole candidate configuration is validated (nothing is saved when it fails); the change is stored as a proposal (once per evidence);
/// a tightening is applied as <c>auto</c> only when Auto is ON and fewer than <c>Learned:WeeklyCap</c> automatic changes were applied in the trailing week. A loosening is never applied here. Unavailable evidence changes nothing.
/// </summary>
internal sealed class RunLearnedSettingsHandler(
    ILearnedEvidenceSource evidence,
    ILearnedSettingStore store,
    ILearnedConfig config,
    IAutoSwitch auto,
    LearnedSettingsOptions options,
    TimeProvider clock,
    ILogger<RunLearnedSettingsHandler> logger) : ICommandHandler<RunLearnedSettingsCommand, LearnedRunView>
{
    public async ValueTask<Result<LearnedRunView>> HandleAsync(RunLearnedSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var batch = await evidence.ReadAsync(cancellationToken);
        if (!batch.Available || batch.Items.Count == 0)
        {
            return Result.Success(new LearnedRunView("No learned evidence is available this week; nothing changed.", 0, 0, []));
        }

        var settings = options;
        var autoOn = await auto.IsOnAsync(cancellationToken);
        var applied = new Dictionary<string, string>(await store.AppliedValuesAsync(cancellationToken), StringComparer.Ordinal);
        var used = await store.AutoAppliedSinceAsync(clock.GetUtcNow().AddDays(-LearnedSnapshot.WeekDays), cancellationToken);
        var skipped = new List<LearnedSkip>();
        var proposed = 0;
        var appliedNow = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in batch.Items.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            if (!seen.Add(item.Key))
            {
                skipped.Add(Skip(item.Key, "learned.one_variable")); // one variable per change: the first piece of evidence for a key in a run is the only one
                continue;
            }

            var descriptor = config.Descriptor(item.Key);
            var current = applied.TryGetValue(item.Key, out var a) ? a : config.ConfiguredValue(item.Key);
            var decision = LearnedSettingProposer.Propose(descriptor, current, item, settings.MaxStepPercent, config.ConfiguredValue(item.Key), settings.MaxTotalPercent);
            if (decision.Outcome == LearnedOutcome.Skip)
            {
                skipped.Add(Skip(item.Key, decision.Code));
                continue;
            }

            var newText = LearnedSettingProposer.Text(decision.NewValue);
            var candidate = new Dictionary<string, string>(applied, StringComparer.Ordinal) { [item.Key] = newText };
            if (config.Failures(candidate).Count > 0)
            {
                skipped.Add(Skip(item.Key, "learned.config_invalid")); // some keys limit each other: nothing is saved
                continue;
            }

            var (json, hash) = LearnedSnapshot.Make(autoOn, settings, LearnedSnapshot.Effective(config, applied));
            var direction = decision.Outcome == LearnedOutcome.Tighten ? "tighten" : "loosen";
            var id = await store.InsertProposedAsync(new NewLearnedChange(item.Key, current!, newText, direction, "learning", json, hash, item.Reference, item.Hash, null), cancellationToken);
            if (id is null)
            {
                skipped.Add(Skip(item.Key, "learned.already_seen"));
                continue;
            }

            proposed++;
            LearnedLog.Proposed(logger, id.Value, item.Key, direction);
            if (decision.Outcome == LearnedOutcome.Tighten && autoOn && used < settings.WeeklyCap && await store.ApplyAsync(id.Value, LearnedSettingProposer.AutoActor, applied.GetValueOrDefault(item.Key), cancellationToken))
            {
                used++;
                appliedNow++;
                applied[item.Key] = newText;
                LearnedLog.Decided(logger, id.Value, "applied", LearnedSettingProposer.AutoActor);
            }
        }

        return Result.Success(new LearnedRunView($"{proposed} proposed, {appliedNow} applied automatically, {skipped.Count} left alone.", proposed, appliedNow, skipped));
    }

    private LearnedSkip Skip(string key, string code)
    {
        LearnedLog.Skipped(logger, key, code);
        return new LearnedSkip(key, code);
    }
}

/// <summary>What the three owner decisions share: the owner check, the lookup, the stored move and the refusals (AU-10).</summary>
internal sealed class LearnedDecisionSupport(ILearnedSettingStore store, ILearnedConfig config, IAutoSwitch auto, LearnedSettingsOptions options, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<LearnedDecisionSupport> logger)
{
    public ILearnedSettingStore Store => store;

    public ILearnedConfig Config => config;

    public LearnedSettingsOptions Options => options;

    public async ValueTask<Result<LearnedChangeRow>> FindAsync(long id, string actor, string state, CancellationToken cancellationToken)
    {
        if (!actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length)
        {
            LearnedLog.Refused(logger, "learned.owner_only");
            return Result.Failure<LearnedChangeRow>(Error.Forbidden("learned.owner_only", "Only the signed-in owner decides a learned change."));
        }

        var row = await store.FindAsync(id, cancellationToken);
        if (row is null)
        {
            return Result.Failure<LearnedChangeRow>(Error.NotFound("learned.not_found", "No such learned change."));
        }

        if (row.State != state)
        {
            LearnedLog.Refused(logger, "learned.wrong_state");
            return Result.Failure<LearnedChangeRow>(Error.Conflict("learned.wrong_state", $"The change is {row.State}; this needs it {state}."));
        }

        return Result.Success(row);
    }

    /// <summary>The whole configuration with <paramref name="key"/> set to <paramref name="value"/> over the applied values; the error when it is not valid, else null.</summary>
    public Error? CheckCandidate(IReadOnlyDictionary<string, string> applied, string key, string value)
    {
        var candidate = new Dictionary<string, string>(applied, StringComparer.Ordinal) { [key] = value };
        var failures = config.Failures(candidate);
        return failures.Count > 0 ? Error.Validation("learned.config_invalid", "The configuration with this change is not valid: " + string.Join(" ", failures.Take(3))) : null;
    }

    /// <summary>Applies (with the value the caller checked against) or dismisses a proposed row as the owner.</summary>
    public async ValueTask<Result<LearnedChangeView>> DecideAsync(long id, string state, string actor, string? expectedApplied, CancellationToken cancellationToken)
    {
        var done = state == "applied" ? await store.ApplyAsync(id, actor, expectedApplied, cancellationToken) : await store.DismissAsync(id, actor, cancellationToken);
        if (!done)
        {
            return Refuse(Error.Conflict("learned.stale", "The change was decided meanwhile, or another change to the same setting came first; read the list again."));
        }

        LearnedLog.Decided(logger, id, state, "owner");
        var row = (await store.FindAsync(id, cancellationToken))!;
        await AuditAsync(actor, row);
        return Result.Success(LearnedViews.Of(row, config, true)); // an owner decision that applied is the newest applied row of its key; a dismissed row cannot be undone either way
    }

    /// <summary>The real snapshot of the settings an owner act is made under (Auto as stored now, the guardrails, the values in force).</summary>
    public async ValueTask<(string Json, string Hash)> SnapshotAsync(IReadOnlyDictionary<string, string> applied, CancellationToken cancellationToken) =>
        LearnedSnapshot.Make(await auto.IsOnAsync(cancellationToken), options, LearnedSnapshot.Effective(config, applied));

    /// <summary>One audit row for the owner's decision. The decision stands and ignores the request token; a failed audit write is logged and does not undo it.</summary>
    public async ValueTask AuditAsync(string actor, LearnedChangeRow row)
    {
        try
        {
            var detail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"change={row.Id} key={row.Key} {row.OldValue}->{row.NewValue} {row.Direction} state={row.State}");
            events.Record(AccountEvent.Record(AccountEventType.LearnedSettingDecided, null, detail, actor, clock.GetUtcNow()));
            if (await unitOfWork.TrySaveChangesAsync(CancellationToken.None))
            {
                return;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // falls through to the log
        }

        LearnedLog.Refused(logger, "learned.audit_failed");
    }

    public Result<LearnedChangeView> Refuse(Error error)
    {
        LearnedLog.Refused(logger, error.Code);
        return Result.Failure<LearnedChangeView>(error);
    }

    public LearnedChangeView View(LearnedChangeRow row) => LearnedViews.Of(row, config, true); // used for the undo row just applied
}

internal sealed class ApproveLearnedChangeHandler(LearnedDecisionSupport support) : ICommandHandler<ApproveLearnedChangeCommand, LearnedChangeView>
{
    public async ValueTask<Result<LearnedChangeView>> HandleAsync(ApproveLearnedChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var found = await support.FindAsync(command.Id, command.Actor, "proposed", cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<LearnedChangeView>(found.Error);
        }

        var row = found.Value;
        if (LearnedRules.ApproveNeedsPassword(row, support.Config.Descriptor(row.Key)) && !command.FreshPassword)
        {
            return support.Refuse(Error.Forbidden("learned.password_required", "A loosening needs your password for this change."));
        }

        var applied = await support.Store.AppliedValuesAsync(cancellationToken);
        var current = applied.TryGetValue(row.Key, out var v) ? v : support.Config.ConfiguredValue(row.Key);
        if (current != row.OldValue)
        {
            return support.Refuse(Error.Conflict("learned.stale", "The setting changed since this was proposed; read the list again."));
        }

        if (support.CheckCandidate(applied, row.Key, row.NewValue) is { } invalid)
        {
            return support.Refuse(invalid);
        }

        if (row.Direction == "tighten" && !LearnedSettingProposer.WithinDrift(support.Config.Descriptor(row.Key), support.Config.ConfiguredValue(row.Key), row.NewValue, support.Options.MaxTotalPercent))
        {
            // the drift limit is checked again at approval: it may have been lowered since the proposal was made
            return support.Refuse(Error.Conflict("learned.total_limit", "This tightening is further from your configured value than the total limit allows now; dismiss it."));
        }

        return await support.DecideAsync(row.Id, "applied", command.Actor, applied.GetValueOrDefault(row.Key), cancellationToken);
    }
}

internal sealed class DismissLearnedChangeHandler(LearnedDecisionSupport support) : ICommandHandler<DismissLearnedChangeCommand, LearnedChangeView>
{
    public async ValueTask<Result<LearnedChangeView>> HandleAsync(DismissLearnedChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var found = await support.FindAsync(command.Id, command.Actor, "proposed", cancellationToken);
        return found.IsFailure ? Result.Failure<LearnedChangeView>(found.Error) : await support.DecideAsync(found.Value.Id, "dismissed", command.Actor, null, cancellationToken);
    }
}

internal sealed class UndoLearnedChangeHandler(LearnedDecisionSupport support) : ICommandHandler<UndoLearnedChangeCommand, LearnedChangeView>
{
    public async ValueTask<Result<LearnedChangeView>> HandleAsync(UndoLearnedChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var found = await support.FindAsync(command.Id, command.Actor, "applied", cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<LearnedChangeView>(found.Error);
        }

        var row = found.Value;
        var descriptor = support.Config.Descriptor(row.Key);
        if (LearnedRules.UndoNeedsPassword(row, descriptor) && !command.FreshPassword)
        {
            return support.Refuse(Error.Forbidden("learned.password_required", "Undoing a tightening loosens a gate and needs your password for this change."));
        }

        var applied = await support.Store.AppliedValuesAsync(cancellationToken);
        if (!applied.TryGetValue(row.Key, out var current) || current != row.NewValue)
        {
            return support.Refuse(Error.Conflict("learned.stale", "A later change replaced this one; undo that one first."));
        }

        if (support.CheckCandidate(applied, row.Key, row.OldValue) is { } invalid)
        {
            return support.Refuse(invalid);
        }

        var direction = LearnedRules.Loosens(descriptor, row.NewValue, row.OldValue) ? "loosen" : "tighten";
        var (json, hash) = await support.SnapshotAsync(applied, cancellationToken);
        var idText = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var undo = new NewLearnedChange(row.Key, row.NewValue, row.OldValue, direction, "owner", json, hash, "undo of change " + idText, "undo:" + idText, row.Id);
        var id = await support.Store.UndoAsync(row.Id, undo, command.Actor, cancellationToken);
        if (id is null)
        {
            return support.Refuse(Error.Conflict("learned.stale", "The change is not applied any more."));
        }

        var made = (await support.Store.FindAsync(id.Value, cancellationToken))!;
        await support.AuditAsync(command.Actor, made);
        return Result.Success(support.View(made));
    }
}
