using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The two automatic switches as served; a null is unavailable (the table could not be read).</summary>
public sealed record HoldOutAutoSettingsView(bool? AutoRegister, bool? AutoRun);

/// <summary>Reads the switches.</summary>
public sealed record GetHoldOutAutoSettingsQuery : IQuery<HoldOutAutoSettingsView>;

/// <summary>Sets either switch (a null leaves it); the owner after a fresh password. Appends a row per change, with the reason.</summary>
public sealed record SetHoldOutAutoSettingsCommand(bool? AutoRegister, bool? AutoRun, string Actor) : ICommand;

/// <summary>Reads the switches.</summary>
internal sealed class GetHoldOutAutoSettingsHandler(IHoldOutAutoSettings settings) : IQueryHandler<GetHoldOutAutoSettingsQuery, HoldOutAutoSettingsView>
{
    public async ValueTask<HoldOutAutoSettingsView> HandleAsync(GetHoldOutAutoSettingsQuery query, CancellationToken cancellationToken)
    {
        var s = await settings.ReadAsync(cancellationToken);
        return new HoldOutAutoSettingsView(s.AutoRegister, s.AutoRun);
    }
}

/// <summary>Appends the changes (SR-03 D5): turning either OFF or back ON is a fresh-password act, checked at the endpoint; the row records who and why.</summary>
internal sealed class SetHoldOutAutoSettingsHandler(IHoldOutAutoSettings settings, TimeProvider time) : ICommandHandler<SetHoldOutAutoSettingsCommand, HoldOutAutoSettingsView>
{
    public async ValueTask<Result<HoldOutAutoSettingsView>> HandleAsync(SetHoldOutAutoSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<HoldOutAutoSettingsView>(HoldOutRegistrationErrors.Invalid("bad_mode", "a change needs an actor of at most 100 characters"));
        }

        var now = time.GetUtcNow();
        if (command.AutoRegister is { } register)
        {
            await settings.AppendAsync(HoldOutRegistrationErrors.AutoRegister, register, command.Actor, "owner setting", now, cancellationToken);
        }

        if (command.AutoRun is { } run)
        {
            await settings.AppendAsync(HoldOutRegistrationErrors.AutoRun, run, command.Actor, "owner setting", now, cancellationToken);
        }

        var s = await settings.ReadAsync(cancellationToken);
        return Result.Success(new HoldOutAutoSettingsView(s.AutoRegister, s.AutoRun));
    }
}
