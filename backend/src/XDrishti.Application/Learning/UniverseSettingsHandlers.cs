using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The universe settings in force with their history (BK-11).</summary>
/// <param name="MinTradesChild"><c>learning.universe_min_trades_child</c>.</param>
/// <param name="LinkMinTrades"><c>learning.universe_link_min_trades</c>.</param>
/// <param name="RegisteredBaskets"><c>learning.universe_registered_baskets</c>.</param>
/// <param name="AutoPropose"><c>learning.universe_link.auto_propose</c> (BK-11b): off by default; when on, observations are proposed as hypotheses by the actor auto. Proposes only.</param>
/// <param name="History">The latest 50 changes, newest first.</param>
/// <param name="Version">The newest change id (0 when none); a change names it as <c>expectedVersion</c>.</param>
/// <param name="Notice">What the settings do and do not do.</param>
public sealed record UniverseSettingsView(int MinTradesChild, int LinkMinTrades, IReadOnlyList<string> RegisteredBaskets, IReadOnlyList<UniverseSettingChangeView> History, long Version, string Notice, bool AutoPropose = false)
{
    /// <summary>Printed with the settings.</summary>
    public const string NoticeText = "These settings decide what the read side calls a tier cell, an observation and a registered basket. None of them applies a restriction, changes a scan scope or places an order. The auto option (off by default) only proposes a hypothesis, as the actor auto.";
}

/// <summary>One change in the history.</summary>
public sealed record UniverseSettingChangeView(long Id, string Key, string OldValue, string NewValue, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>Reads the universe settings.</summary>
public sealed record GetUniverseSettingsQuery : IQuery<UniverseSettingsView>;

/// <summary>The owner's change of the universe settings; a value left null stays. The fresh password is checked for every change.</summary>
public sealed record ChangeUniverseSettingsCommand(string Actor, Guid SessionId, ClientInfo Client, int? MinTradesChild, int? LinkMinTrades, string? RegisteredBaskets, string? Password, long? ExpectedVersion = null, bool? AutoPropose = null) : ICommand;

internal static class UniverseSettingsViews
{
    public static UniverseSettingsView Of(UniverseSettingsState state) => new(
        state.Settings.MinTradesChild, state.Settings.LinkMinTrades, state.Settings.RegisteredBaskets,
        [.. state.History.Select(h => new UniverseSettingChangeView(h.Id, h.Key, h.OldValue, h.NewValue, h.Actor, h.ChangedAt, h.Hash))], state.Version, UniverseSettingsView.NoticeText, state.Settings.AutoPropose);
}

internal sealed class GetUniverseSettingsHandler(IUniverseSettingsStore store) : IQueryHandler<GetUniverseSettingsQuery, UniverseSettingsView>
{
    public async ValueTask<UniverseSettingsView> HandleAsync(GetUniverseSettingsQuery query, CancellationToken cancellationToken) =>
        UniverseSettingsViews.Of(await store.CurrentAsync(cancellationToken));
}

/// <summary>
/// The owner's change of the universe settings (BK-11, the precedent of BK-10's sizing settings). Order: the actor is an owner; the fresh password for THIS change (401 / 423 from the step-up, the elevation window untouched); the values are checked; each key that changes is appended as one
/// row; one account event per change. No rule, job or model calls this, and nothing here applies a restriction or places an order.
/// </summary>
internal sealed class ChangeUniverseSettingsHandler(IUniverseSettingsStore store, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeUniverseSettingsHandler> logger)
    : ICommandHandler<ChangeUniverseSettingsCommand, UniverseSettingsView>
{
    public const string OwnerOnlyCode = "learning.owner_only";

    public const string InvalidCode = "learning.invalid";

    public const string StaleCode = "learning.stale";

    public async ValueTask<Result<UniverseSettingsView>> HandleAsync(ChangeUniverseSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure<UniverseSettingsView>(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner changes the universe settings."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Result.Failure<UniverseSettingsView>(checkedPassword.Error);
        }

        if (command.ExpectedVersion is not { } expected)
        {
            return Result.Failure<UniverseSettingsView>(Error.Validation(InvalidCode, "expectedVersion: name the version of the settings the form was read at."));
        }

        var state = await store.CurrentAsync(cancellationToken);
        var now = state.Settings;
        var rows = new List<UniverseSettingChange>();
        var problems = new List<FieldError>();
        if (command.MinTradesChild is { } child)
        {
            if (child is < 1 or > 1000)
            {
                problems.Add(new FieldError("minTradesChild", "At least 1 and at most 1000."));
            }
            else if (child != now.MinTradesChild)
            {
                rows.Add(Row(UniverseSettingsRules.MinTradesChildKey, now.MinTradesChild.ToString(CultureInfo.InvariantCulture), child.ToString(CultureInfo.InvariantCulture), command.Actor));
            }
        }

        if (command.LinkMinTrades is { } link)
        {
            if (link is < 10 or > 10_000)
            {
                problems.Add(new FieldError("linkMinTrades", "At least 10 and at most 10000."));
            }
            else if (link != now.LinkMinTrades)
            {
                rows.Add(Row(UniverseSettingsRules.LinkMinTradesKey, now.LinkMinTrades.ToString(CultureInfo.InvariantCulture), link.ToString(CultureInfo.InvariantCulture), command.Actor));
            }
        }

        if (command.RegisteredBaskets is { } text)
        {
            var names = UniverseSettingsRules.ParseList(text);
            if (UniverseSettingsRules.ListProblem(names) is { } problem)
            {
                problems.Add(new FieldError("registeredBaskets", problem));
            }
            else if (!names.SequenceEqual(now.RegisteredBaskets, StringComparer.Ordinal))
            {
                rows.Add(Row(UniverseSettingsRules.RegisteredBasketsKey, now.RegisteredText, string.Join(',', names), command.Actor));
            }
        }

        if (command.AutoPropose is { } auto && auto != now.AutoPropose)
        {
            // BK-11b: the auto option of the tier proposer; it proposes a hypothesis and applies nothing
            rows.Add(Row(UniverseSettingsRules.AutoProposeKey, now.AutoPropose ? "true" : "false", auto ? "true" : "false", command.Actor));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<UniverseSettingsView>(Error.Validation(InvalidCode, string.Join(" ", problems.Select(p => p.Field + ": " + p.Message))));
        }

        if (rows.Count > 0)
        {
            if (!await store.AppendAsync(rows, expected, cancellationToken))
            {
                return Result.Failure<UniverseSettingsView>(Error.Conflict(StaleCode, "The universe settings were changed meanwhile; read them again."));
            }

            foreach (var r in rows)
            {
                await EssentialAudit.RecordAsync(events, unitOfWork, AccountEventType.EssentialSettingChanged, string.Create(CultureInfo.InvariantCulture, $"{r.Key} hash={r.Hash[..12]}"), command.Actor, clock.GetUtcNow(), logger);
            }
        }

        return Result.Success(UniverseSettingsViews.Of(await store.CurrentAsync(cancellationToken)));
    }

    private static UniverseSettingChange Row(string key, string oldValue, string newValue, string actor) => new(key, oldValue, newValue, actor, UniverseSettingsRules.Hash(key, oldValue, newValue, actor));
}
