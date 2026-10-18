using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// The refusals of a request before anything is read (spec P3-04a, D1). A number is tested for being finite before it is tested for a range: a range test written with <c>&lt;</c> or <c>&gt;</c> lets NaN through. The fill rules are only
/// checked for finiteness here; their ranges are the business of <c>SlippageConfig.Create</c> and <c>FillRules.Create</c>, whose codes pass through.
/// </summary>
internal static class SimulationRequestChecks
{
    /// <summary>The most instruments an explicit list may name (the bar read dominates the cost).</summary>
    public const int MaxInstruments = 2_000;

    /// <summary>The years of the importer's range: from 2000-01-01 up to but not including 2100-01-01 (<c>CsvBarReader</c>).</summary>
    public const int FirstYear = 2000;

    public const int LastYear = 2099;

    /// <summary>One entry per rejected field; empty when the request is valid. <paramref name="timeframe"/> is the parsed evaluation timeframe when that field is valid.</summary>
    public static FieldErrors Validate(SimulationRequest request, out Timeframe timeframe)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(request.StrategyId))
        {
            errors.Add("strategyId", "Name a built-in strategy.");
        }

        if (!Timeframes.TryParse(request.EvaluationTimeframe, out timeframe))
        {
            errors.Add("evaluationTimeframe", "Use a timeframe code such as 5m or 15m.");
        }

        CheckDates(request, errors);
        CheckUniverse(request, errors);
        CheckNumbers(request, errors);
        return errors;
    }

    private static void CheckDates(SimulationRequest request, FieldErrors errors)
    {
        var fromOk = request.From.Year is >= FirstYear and <= LastYear;
        var toOk = request.To.Year is >= FirstYear and <= LastYear;
        if (!fromOk)
        {
            errors.Add("from", $"Use a date in the years {FirstYear} to {LastYear}.");
        }

        if (!toOk)
        {
            errors.Add("to", $"Use a date in the years {FirstYear} to {LastYear}.");
        }

        if (fromOk && toOk && request.From > request.To)
        {
            errors.Add("from", "The first date must not be after the last date.");
        }
    }

    private static void CheckUniverse(SimulationRequest request, FieldErrors errors)
    {
        var hasList = request.InstrumentIds is not null;
        var hasBasket = request.BasketId is not null;
        if (hasList == hasBasket)
        {
            errors.Add(hasList ? "basketId" : "instrumentIds", "Give either a list of instruments or a basket, not both and not neither.");
            return;
        }

        if (request.InstrumentIds is { } ids)
        {
            if (ids.Count is < 1 or > MaxInstruments)
            {
                errors.Add("instrumentIds", $"Give 1 to {MaxInstruments} instruments.");
            }
            else if (ids.Distinct().Count() != ids.Count)
            {
                errors.Add("instrumentIds", "An instrument is listed twice.");
            }
        }
    }

    private static void CheckNumbers(SimulationRequest request, FieldErrors errors)
    {
        foreach (var (name, value) in request.Parameters ?? new Dictionary<string, double>())
        {
            if (!double.IsFinite(value))
            {
                errors.Add("parameters." + name, "Use a finite number.");
            }
        }

        if (request.Options is { } options)
        {
            var defaults = ReplayOptions.Default;
            if (new ReplayOptions(options.MinSessionCompleteness, defaults.DiscontinuityThreshold).Problem() is { } completeness)
            {
                errors.Add("options.minSessionCompleteness", completeness);
            }

            if (new ReplayOptions(defaults.MinSessionCompleteness, options.DiscontinuityThreshold).Problem() is { } threshold)
            {
                errors.Add("options.discontinuityThreshold", threshold);
            }
        }

        if (request.Fills is { } fills)
        {
            Finite(errors, "fills.entryLimitPercent", fills.EntryLimitPercent);
            Finite(errors, "fills.ticks", fills.Ticks);
            Finite(errors, "fills.barRangeFrac", fills.BarRangeFrac);
            Finite(errors, "fills.stopExitExtraTicks", fills.StopExitExtraTicks);
        }
    }

    private static void Finite(FieldErrors errors, string field, double value)
    {
        if (!double.IsFinite(value))
        {
            errors.Add(field, "Use a finite number.");
        }
    }
}
