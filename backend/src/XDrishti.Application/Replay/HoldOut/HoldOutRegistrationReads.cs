using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>One registration with its state.</summary>
public sealed record GetHoldOutRegistrationQuery(Guid Id) : IQuery<Result<HoldOutRegistrationView>>;

/// <summary>The registrations, newest first (no live input check: a state of <c>waiting</c> may still drift).</summary>
public sealed record ListHoldOutRegistrationsQuery : IQuery<Result<IReadOnlyList<HoldOutRegistrationView>>>;

/// <summary>Builds the served view of a stored registration (shared by the two reads).</summary>
internal sealed class HoldOutRegistrationViews(IHoldOutRegistrationStore store, IHoldOutStore blocks, HoldOutInputCheck inputs)
{
    public async ValueTask<HoldOutRegistrationView?> ViewAsync(StoredHoldOutRegistration row, bool checkDrift, CancellationToken cancellationToken)
    {
        var config = ReplayJson.Read<HoldOutRegistrationConfig>(row.ConfigJson);
        if (config is null)
        {
            return null;
        }

        var result = await store.GetResultAsync(row.Id, cancellationToken);
        string state;
        string? drift = null;
        if (result is not null)
        {
            state = "run";
        }
        else if (XDrishti.Domain.Market.IndiaTime.Today(row.CreatedAt) >= row.BlockStart)
        {
            state = "late"; // stored on or after the block began: it can never take a fresh look
        }
        else if (await store.NewestForBlockAsync(row.BlockStart, cancellationToken) is { } newest && newest.Id != row.Id)
        {
            state = "superseded";
        }
        else if (await blocks.GetUnlockAsync(HoldOutRegistrationIds.LookKey(row.Id), HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, row.BlockStart, cancellationToken) is not null)
        {
            state = "claimed";
        }
        else
        {
            drift = checkDrift ? await inputs.DriftAsync(config, cancellationToken) : null;
            state = drift is null ? "waiting" : "drift";
        }

        HoldOutRegistrationResultView? view = null;
        if (result is not null && ReplayJson.Read<HoldOutResultBody>(result.FiguresJson) is { } body)
        {
            view = new HoldOutRegistrationResultView(result.Verdict, body.Verdict.Reason, body.Figures, result.RunIds, result.EngineVersions, result.Mode, result.Actor, result.At);
        }

        return new HoldOutRegistrationView(
            row.Id, row.BlockStart, config.BlockEnd, row.ConfigHash, row.Mode, row.Actor, row.CreatedAt, state, drift, config.PassRule, config.Parameters.CostSheetId ?? string.Empty, config.CostSheetRatesHash,
            config.Parameters.Capital, config.Instruments, config.EngineVersion, config.SettingsHash, HoldOutRegistrationView.HindsightText, view);
    }
}

/// <summary>Reads one registration, with the live input check when it is still waiting.</summary>
internal sealed class GetHoldOutRegistrationHandler(IHoldOutRegistrationStore store, HoldOutRegistrationViews views) : IQueryHandler<GetHoldOutRegistrationQuery, Result<HoldOutRegistrationView>>
{
    public async ValueTask<Result<HoldOutRegistrationView>> HandleAsync(GetHoldOutRegistrationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.GetAsync(query.Id, cancellationToken) is { } row && await views.ViewAsync(row, true, cancellationToken) is { } view
            ? Result.Success(view)
            : Result.Failure<HoldOutRegistrationView>(HoldOutRegistrationErrors.NotFound(query.Id));
    }
}

/// <summary>Reads the registrations, newest first, at most 50.</summary>
internal sealed class ListHoldOutRegistrationsHandler(IHoldOutRegistrationStore store, HoldOutRegistrationViews views) : IQueryHandler<ListHoldOutRegistrationsQuery, Result<IReadOnlyList<HoldOutRegistrationView>>>
{
    public async ValueTask<Result<IReadOnlyList<HoldOutRegistrationView>>> HandleAsync(ListHoldOutRegistrationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = new List<HoldOutRegistrationView>();
        foreach (var row in await store.ListAsync(50, cancellationToken))
        {
            if (await views.ViewAsync(row, false, cancellationToken) is { } view)
            {
                rows.Add(view);
            }
        }

        return Result.Success<IReadOnlyList<HoldOutRegistrationView>>(rows);
    }
}
