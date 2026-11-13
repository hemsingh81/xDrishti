using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The system step of the evening seal service (SR-03 D5, ADR 0282), order 40. With <c>replay.holdout.auto_run</c> ON it claims, recorded as <c>auto</c>, the system look of a sealed block in force whose newest registration has no result (the run itself is done by
/// <see cref="HoldOutSystemRunLoop"/>). With <c>replay.holdout.auto_register</c> ON, once the block in force is sealed and nothing targets the block after it, it registers the current configuration for that block as <c>auto</c> (the last evening before it begins). An
/// <c>unavailable</c> switch never acts; a drift or an invalid registration only says so (the bell notice shows it). Never claims block 1 (a registration cannot target it) and places no order.
/// </summary>
internal sealed class SystemReplaySealStep(
    HoldOutEpochs epochs,
    IHoldOutAutoSettings settings,
    IHoldOutRegistrationStore store,
    ICommandHandler<RegisterHoldOutCommand, RegisteredHoldOut> register,
    ICommandHandler<RunHoldOutRegistrationCommand, StartedHoldOutRun> run,
    HoldOutOptions options,
    TimeProvider time) : IHoldOutSealStep
{
    public const string Actor = "system:holdout-replay";

    public string Name => "system replay";

    public int Order => 40;

    public async ValueTask<string> RunAsync(CancellationToken cancellationToken)
    {
        var switches = await settings.ReadAsync(cancellationToken);
        if (await epochs.InForceAsync(cancellationToken) is not { } current || current.State is not (HoldOutBlockState.Sealed or HoldOutBlockState.Looked))
        {
            return "no sealed block";
        }

        var parts = new List<string>
        {
            switches.AutoRun switch
            {
                null => "run unavailable",
                false => "auto run off",
                true => await ClaimAsync(current.Block.Start, switches.AutoRunStamp, cancellationToken),
            },
        };

        var next = HoldOutChain.Next(current.End, options.BlockMonths);
        if (switches.AutoRegister is null)
        {
            parts.Add("register unavailable");
        }
        else if (switches.AutoRegister == false)
        {
            parts.Add("auto register off");
        }
        else if (next.Start <= IndiaTime.Today(time.GetUtcNow()))
        {
            parts.Add("next block begun");
        }
        else if ((await store.ForBlockAsync(next.Start, cancellationToken)).Count > 0)
        {
            parts.Add("registration exists");
        }
        else
        {
            var made = await register.HandleAsync(new RegisterHoldOutCommand(null, null, null, null, null, null, RegisterHoldOutHandler.AutoMode, Actor), cancellationToken);
            parts.Add(made.IsSuccess ? $"registered for {next.Start:yyyy-MM-dd}" : "not registered: " + made.Error.Message);
        }

        return string.Join(", ", parts);
    }

    private async ValueTask<string> ClaimAsync(DateOnly blockStart, string? stamp, CancellationToken cancellationToken)
    {
        if (await store.NewestForBlockAsync(blockStart, cancellationToken) is not { } newest)
        {
            return "no registration";
        }

        if (await store.GetResultAsync(newest.Id, cancellationToken) is not null)
        {
            return "run done";
        }

        var claimed = await run.HandleAsync(new RunHoldOutRegistrationCommand(newest.Id, RegisterHoldOutHandler.AutoMode, Actor, stamp), cancellationToken);
        return claimed.IsSuccess ? "look claimed, run follows" : "not run: " + claimed.Error.Code;
    }
}
