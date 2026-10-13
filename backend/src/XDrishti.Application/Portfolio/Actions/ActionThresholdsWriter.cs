using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>Checks the thresholds a request carries and writes a change with its audit event (the changed fields and their old and new values: no figures of the portfolio). Writing the values already in force changes and records nothing. After a change the done decisions whose condition the new thresholds ended are cleared at once (<see cref="ActionReconciler"/>), so that an item the owner had finished is open again when the threshold is lowered back (the condition did stop holding while it was higher), with no read or tick in between. Basket and lot edits are reconciled at the next tick, sync or Action-center read.</summary>
internal sealed class ActionThresholdsWriter(IActionSettings settings, IAccountEventLog events, IPortfolioUnitOfWork unitOfWork, ActionReconciler reconciler, TimeProvider clock, ILogger<ActionThresholdsWriter> logger)
{
    public static ActionThresholdsView ViewOf(ActionThresholdSetting setting) =>
        new(setting.Values, ActionThresholds.Defaults, setting.Values == ActionThresholds.Defaults, setting.UpdatedBy, setting.UpdatedAt);

    /// <summary>Every value is required; the three counts must be whole numbers; the rest is the range check of <see cref="ActionThresholds.Validate"/>. All problems come back at once.</summary>
    public static Result<ActionThresholds> Parse(ActionThresholdsInput? input)
    {
        var errors = new FieldErrors();
        if (input is null)
        {
            errors.Add("belowCostPct", "Required.");
            return Result.Failure<ActionThresholds>(PortfolioErrors.Invalid(errors));
        }

        var belowCost = Required(input.BelowCostPct, "belowCostPct", errors);
        var high = Required(input.BelowCostHighPct, "belowCostHighPct", errors);
        var gain = Required(input.GainPct, "gainPct", errors);
        var floor = Required(input.OverweightFloorPct, "overweightFloorPct", errors);
        var multiple = Required(input.OverweightMultiple, "overweightMultiple", errors);
        var positions = Whole(input.MinPositions, "minPositions", errors);
        var concentration = Required(input.ConcentrationPct, "concentrationPct", errors);
        var lag = Required(input.LagPp, "lagPp", errors);
        var lagDays = Whole(input.LagDays, "lagDays", errors);
        var window = Whole(input.LongTermWindowDays, "longTermWindowDays", errors);
        if (errors.HasErrors)
        {
            return Result.Failure<ActionThresholds>(PortfolioErrors.Invalid(errors));
        }

        var thresholds = new ActionThresholds(belowCost, high, gain, floor, multiple, positions, concentration, lag, lagDays, window);
        thresholds.Validate(errors);
        return errors.HasErrors ? Result.Failure<ActionThresholds>(PortfolioErrors.Invalid(errors)) : Result.Success(thresholds);
    }

    /// <summary>Saves the thresholds when they differ from those in force; the answer is the view of what is in force afterwards.</summary>
    public async ValueTask<Result<ActionThresholdsView>> WriteAsync(ActionThresholds wanted, string actor, bool reset, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var changes = Changes(current.Values, wanted);
        if (changes.Count == 0)
        {
            return Result.Success(ViewOf(current));
        }

        var now = clock.GetUtcNow();
        await settings.SetAsync(wanted, actor, now, cancellationToken);
        events.Record(AccountEvent.Record(
            AccountEventType.ActionThresholdsChanged,
            null,
            reset ? "Action center thresholds restored to the defaults" : "Action center thresholds changed: " + string.Join(", ", changes),
            actor,
            now));
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<ActionThresholdsView>(PortfolioErrors.ActionChanged);
        }

        ActionsLog.ThresholdsChanged(logger, changes.Count);
        await reconciler.ReconcileAsync("thresholds", cancellationToken);
        return Result.Success(ViewOf(await settings.GetAsync(cancellationToken)));
    }

    private static List<string> Changes(ActionThresholds from, ActionThresholds to)
    {
        List<string> changes = [];
        Add(changes, "belowCostPct", from.BelowCostPct, to.BelowCostPct);
        Add(changes, "belowCostHighPct", from.BelowCostHighPct, to.BelowCostHighPct);
        Add(changes, "gainPct", from.GainPct, to.GainPct);
        Add(changes, "overweightFloorPct", from.OverweightFloorPct, to.OverweightFloorPct);
        Add(changes, "overweightMultiple", from.OverweightMultiple, to.OverweightMultiple);
        Add(changes, "minPositions", from.MinPositions, to.MinPositions);
        Add(changes, "concentrationPct", from.ConcentrationPct, to.ConcentrationPct);
        Add(changes, "lagPp", from.LagPp, to.LagPp);
        Add(changes, "lagDays", from.LagDays, to.LagDays);
        Add(changes, "longTermWindowDays", from.LongTermWindowDays, to.LongTermWindowDays);
        return changes;
    }

    private static void Add(List<string> changes, string name, decimal from, decimal to)
    {
        if (from != to)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {from:0.##} to {to:0.##}"));
        }
    }

    private static decimal Required(decimal? value, string field, FieldErrors errors)
    {
        if (value is { } v)
        {
            return v;
        }

        errors.Add(field, "Required.");
        return 0m;
    }

    private static int Whole(decimal? value, string field, FieldErrors errors)
    {
        if (value is not { } v)
        {
            errors.Add(field, "Required.");
            return 0;
        }

        if (v != decimal.Truncate(v))
        {
            errors.Add(field, "Use a whole number.");
            return 0;
        }

        if (v is < 0m or > 100_000m)
        {
            errors.Add(field, "Out of range.");
            return 0;
        }

        return (int)v;
    }
}
