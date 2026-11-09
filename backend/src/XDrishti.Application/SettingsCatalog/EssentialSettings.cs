using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.SettingsCatalog;

/// <summary>The five Essential settings with who changed each and when, and whether it can be reset here (AU-10, AU-09 criterion 3 and 4).</summary>
public sealed record GetEssentialSettingsQuery : IQuery<EssentialSettingsView>;

public sealed record EssentialSettingsView(IReadOnlyList<EssentialSettingView> Items);

/// <summary><c>Value</c> and <c>Default</c> are plain text (null when not known here); <c>IsDefault</c> is true when no row exists or it equals the default; <c>ChangedBy</c> and <c>ChangedAt</c> are null while never changed (or not recorded); <c>CanReset</c> and <c>ResetNote</c> say whether Reset to default works here.</summary>
public sealed record EssentialSettingView(string Key, string Label, string? Value, string? Default, bool IsDefault, string? ChangedBy, DateTimeOffset? ChangedAt, bool CanReset, string? ResetNote);

/// <summary>Sets <c>notices.failure</c> as the owner. Turning it OFF silences failure notices, so it needs <c>FreshPassword</c> (true only after the endpoint verified a fresh password for this act).</summary>
public sealed record ChangeEssentialSettingCommand(string Key, bool On, string Actor, bool FreshPassword) : ICommand;

/// <summary>Puts an Essential setting back to its default as the owner.</summary>
public sealed record ResetEssentialSettingCommand(string Key, string Actor) : ICommand;

/// <summary>The keys and defaults the owner can reset here, and the notes for the others.</summary>
internal static class EssentialKeys
{
    public const string Auto = "auto.enabled";

    public const string Scope = "suggest.scope";

    public const string Risk = "risk.profile";

    public const string Gate = "chargesheet.gate";

    public const string Failure = "notices.failure";

    /// <summary>The stored (app_settings) keys with their defaults.</summary>
    public static readonly IReadOnlyDictionary<string, string> StoredDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Auto] = "false",
        [Scope] = "eligible",
        [Failure] = "true",
    };

    public const string GateNote = "The charge-sheet switch is changed on the Charge sheet screen only; its default is Off.";

    public const string RiskNote = "Risk profiles belong to accounts and have no single default; change them on the Risk profiles screen.";
}

internal sealed class GetEssentialSettingsHandler(IEssentialSettingStore store, IChargeSheetLog chargeSheet) : IQueryHandler<GetEssentialSettingsQuery, EssentialSettingsView>
{
    public async ValueTask<EssentialSettingsView> HandleAsync(GetEssentialSettingsQuery query, CancellationToken cancellationToken)
    {
        var items = new List<EssentialSettingView>();
        foreach (var d in SettingsRegistry.All.Where(x => x.Tier == SettingTier.Essential))
        {
            if (EssentialKeys.StoredDefaults.TryGetValue(d.Key, out var def))
            {
                var row = await store.GetAsync(d.Key, cancellationToken);
                items.Add(new EssentialSettingView(d.Key, d.Label, row?.Value ?? def, def, row is null || row.Value == def, row?.UpdatedBy, row?.UpdatedAt, true, null));
            }
            else if (d.Key == EssentialKeys.Gate)
            {
                var ledger = await chargeSheet.ReadAsync(200, cancellationToken);
                var last = ledger.History.FirstOrDefault(e => e.Kind is ChargeSheetActKind.GateOn or ChargeSheetActKind.GateOff);
                items.Add(new EssentialSettingView(d.Key, d.Label, ledger.CapabilityOn ? "true" : "false", "false", !ledger.CapabilityOn, last?.Actor, last?.At, false, EssentialKeys.GateNote));
            }
            else
            {
                items.Add(new EssentialSettingView(d.Key, d.Label, null, d.Default, true, null, null, false, EssentialKeys.RiskNote));
            }
        }

        return new EssentialSettingsView(items);
    }
}

internal sealed class ChangeEssentialSettingHandler(IEssentialSettingStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeEssentialSettingHandler> logger) : ICommandHandler<ChangeEssentialSettingCommand, EssentialSettingView>
{
    public async ValueTask<Result<EssentialSettingView>> HandleAsync(ChangeEssentialSettingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<EssentialSettingView>(Error.Forbidden("settings.owner_only", "Only the signed-in owner changes a setting."));
        }

        if (command.Key != EssentialKeys.Failure)
        {
            return Result.Failure<EssentialSettingView>(Error.Conflict("settings.not_here", "This setting is changed on its own screen or is not changeable yet."));
        }

        if (!command.On && !command.FreshPassword)
        {
            return Result.Failure<EssentialSettingView>(Error.Forbidden("settings.password_required", "Turning failure notices off needs your password for this change."));
        }

        var now = clock.GetUtcNow();
        var value = command.On ? "true" : "false";
        await store.SetAsync(command.Key, value, command.Actor, now, cancellationToken);
        await EssentialAudit.RecordAsync(events, unitOfWork, AccountEventType.EssentialSettingChanged, string.Create(CultureInfo.InvariantCulture, $"{command.Key}={value}"), command.Actor, now, logger);
        return Result.Success(new EssentialSettingView(command.Key, "Failure notices", value, "true", value == "true", command.Actor, now, true, null));
    }
}

internal sealed class ResetEssentialSettingHandler(IEssentialSettingStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ResetEssentialSettingHandler> logger) : ICommandHandler<ResetEssentialSettingCommand, EssentialSettingView>
{
    public async ValueTask<Result<EssentialSettingView>> HandleAsync(ResetEssentialSettingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<EssentialSettingView>(Error.Forbidden("settings.owner_only", "Only the signed-in owner resets a setting."));
        }

        var now = clock.GetUtcNow();
        var descriptor = SettingsRegistry.All.FirstOrDefault(d => d.Tier == SettingTier.Essential && d.Key == command.Key);
        if (descriptor is null)
        {
            return Result.Failure<EssentialSettingView>(Error.NotFound("settings.not_found", "No such Essential setting."));
        }

        if (EssentialKeys.StoredDefaults.TryGetValue(command.Key, out var def))
        {
            await store.SetAsync(command.Key, def, command.Actor, now, cancellationToken);
            await EssentialAudit.RecordAsync(events, unitOfWork, AccountEventType.EssentialSettingReset, $"{command.Key} reset to default", command.Actor, now, logger);
            return Result.Success(new EssentialSettingView(command.Key, descriptor.Label, def, def, true, command.Actor, now, true, null));
        }

        if (command.Key == EssentialKeys.Gate)
        {
            // the charge-sheet confirmation is switched only by its own owner act (an architecture test pins that nothing else names it): Reset to default there is Off
            return Result.Failure<EssentialSettingView>(Error.Conflict("settings.reset_elsewhere", EssentialKeys.GateNote));
        }

        return Result.Failure<EssentialSettingView>(Error.Conflict("settings.reset_not_single", EssentialKeys.RiskNote));
    }
}

internal static partial class EssentialAudit
{
    public static bool IsOwner(string actor) => actor.StartsWith("owner:", StringComparison.Ordinal) && actor.Length > "owner:".Length;

    /// <summary>One audit row; the change stands and a failed audit write is logged, not hidden.</summary>
    public static async ValueTask RecordAsync(IAccountEventLog events, IAccountUnitOfWork unitOfWork, AccountEventType type, string detail, string actor, DateTimeOffset now, ILogger logger)
    {
        try
        {
            events.Record(AccountEvent.Record(type, null, detail, actor, now));
            if (await unitOfWork.TrySaveChangesAsync(CancellationToken.None))
            {
                return;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // falls through to the log
        }

        AuditFailed(logger, type.ToString());
    }

    [LoggerMessage(EventId = 5566, Level = LogLevel.Warning, Message = "settings.audit_failed {Type}: the change stands, its audit row was not written")]
    private static partial void AuditFailed(ILogger logger, string type);
}
