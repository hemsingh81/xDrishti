using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>Builds the view of the automatic dismissal setting.</summary>
internal static class LooseningSettingsViews
{
    public const int HistoryLimit = 50;

    public const string Notice = "OFF by default. When ON, a loosening that a published plan already showed is hidden at the build of the next plan, recorded as automatic with this setting. Only the owner switches it, with the password. Advisory: no limit is changed and no order is placed.";

    public static async ValueTask<LooseningSettingsView> ReadAsync(ILooseningStore store, CancellationToken cancellationToken)
    {
        var current = await store.CurrentSettingAsync(cancellationToken);
        var history = await store.SettingHistoryAsync(HistoryLimit, cancellationToken);
        return new LooseningSettingsView(current.AutoDismiss, current.Id ?? 0, current.Hash, Notice, [.. history.Select(r => new LooseningSettingChangeView(r.Id, r.AutoDismiss, r.ChangedBy, r.ChangedAt, r.Hash))]);
    }
}

/// <summary>Reads the automatic dismissal setting (OFF while no row exists). Read only.</summary>
internal sealed class GetLooseningSettingsHandler(ILooseningStore store) : IQueryHandler<GetLooseningSettingsQuery, LooseningSettingsView>
{
    public async ValueTask<LooseningSettingsView> HandleAsync(GetLooseningSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await LooseningSettingsViews.ReadAsync(store, cancellationToken);
    }
}

/// <summary>
/// The owner's change of <c>plans.loosening_banner.auto_dismiss</c> (BK-10b, the mechanism of ADR 0219): the endpoint checked the session, the CSRF token and a fresh password for THIS change, ON and OFF alike, and built the actor from the session. A row is appended
/// if the newest row is still the version the owner read (409 <c>plans.stale</c> otherwise). Nothing here touches a limit, a plan or a dismissal.
/// </summary>
internal sealed class ChangeLooseningSettingsHandler(ILooseningStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeLooseningSettingsHandler> logger) : ICommandHandler<ChangeLooseningSettingsCommand, LooseningSettingsView>
{
    public async ValueTask<Result<LooseningSettingsView>> HandleAsync(ChangeLooseningSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(Error.Forbidden("plans.owner_only", "Only the signed-in owner changes this setting."));
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"plans.loosening_banner.auto_dismiss|{command.AutoDismiss}|{command.Actor}|{command.ExpectedVersion}"))));
        if (!await store.AppendSettingAsync(command.AutoDismiss, command.Actor, hash, command.ExpectedVersion, cancellationToken))
        {
            return Refuse(Error.Conflict(PlanErrors.StaleCode, "The setting was changed meanwhile; read it again."));
        }

        var view = await LooseningSettingsViews.ReadAsync(store, cancellationToken);
        await LooseningAudit.RecordAsync(events, unitOfWork, clock, logger, AccountEventType.LooseningSettingsChanged, string.Create(CultureInfo.InvariantCulture, $"auto_dismiss={command.AutoDismiss} version={view.Version} hash={hash}"), command.Actor);
        LooseningLog.SettingChanged(logger, command.AutoDismiss, view.Version);
        return Result.Success(view);
    }

    private Result<LooseningSettingsView> Refuse(Error error)
    {
        LooseningLog.SettingRefused(logger, error.Code);
        return Result.Failure<LooseningSettingsView>(error);
    }
}
