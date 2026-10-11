using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Accounts;

/// <summary>Turns the wire shapes of a request into domain values, reporting unknown codes as field errors.</summary>
internal static class AccountInputs
{
    /// <summary>The trading configuration of a request; fields that are left out take their defaults. Null (with errors) when a code is unknown.</summary>
    public static TradingSpec? ParseTrading(TradingInput? input, FieldErrors errors)
    {
        var local = new FieldErrors();
        var mode = TradingMode.Paper;
        if (!string.IsNullOrWhiteSpace(input?.Mode) && !AccountCodes.TryParseMode(input.Mode, out mode))
        {
            local.Add("mode", "Use paper or manual_live.");
        }

        var source = CapitalSource.Fixed;
        if (!string.IsNullOrWhiteSpace(input?.CapitalSource) && !AccountCodes.TryParseCapitalSource(input.CapitalSource, out source))
        {
            local.Add("capitalSource", "Use fixed or broker_funds.");
        }

        var intraday = false;
        var swing = false;
        foreach (var style in input?.Styles ?? [])
        {
            switch (style?.Trim().ToLowerInvariant())
            {
                case AccountCodes.Intraday:
                    intraday = true;
                    break;
                case AccountCodes.Swing:
                    swing = true;
                    break;
                default:
                    local.Add("styles", "Use intraday or swing.");
                    break;
            }
        }

        var intradayInstruments = ParseInstruments(input?.Instruments?.Intraday, "instruments.intraday", local);
        var swingBuy = ParseInstruments(input?.Instruments?.SwingBuy, "instruments.swingBuy", local);
        var swingSell = ParseInstruments(input?.Instruments?.SwingSell, "instruments.swingSell", local);

        if (local.HasErrors)
        {
            errors.AddRange(local);
            return null;
        }

        var overrides = input?.Overrides is { } o
            ? new TradingOverrides(o.RiskPerTradePct, o.MaxNewEntriesPerDay, o.MaxOpenPositions, o.DailyLossPct)
            : TradingOverrides.None;
        return new TradingSpec(mode, input?.RiskProfileId, source, input?.CapitalAmount, intraday, swing, intradayInstruments, swingBuy, swingSell, overrides);
    }

    /// <summary>
    /// The automatic refresh of a request: values that are left out keep those of <paramref name="current"/>. Null (nothing to change) when none is given;
    /// null with errors (<c>refreshMode</c>, <c>refreshDailyTimeIst</c>) when a value is not valid.
    /// </summary>
    public static PortfolioRefresh? ParseRefresh(PortfolioInput? input, PortfolioRefresh current, FieldErrors errors, string prefix = "")
    {
        if (input is null || (input.RefreshMode is null && input.RefreshDailyTimeIst is null))
        {
            return null;
        }

        var local = new FieldErrors();
        var mode = current.Mode;
        if (input.RefreshMode is not null && !AccountCodes.TryParseRefreshMode(input.RefreshMode, out mode))
        {
            local.Add("refreshMode", "Use off or daily.");
        }

        var time = current.DailyTimeIst;
        if (input.RefreshDailyTimeIst is not null && !AccountCodes.TryParseTime(input.RefreshDailyTimeIst, out time))
        {
            local.Add("refreshDailyTimeIst", "Use a time like 16:00 (HH:mm, India time).");
        }

        var created = PortfolioRefresh.Create(mode, time);
        if (created.IsFailure)
        {
            Collect(created, local);
        }

        if (local.HasErrors)
        {
            errors.AddRange(local, prefix);
            return null;
        }

        return created.Value;
    }

    /// <summary>
    /// The first import date of a request (ADR 0013): null when none is given (keep the current one); a date in the future or before 1990-01-01 is the field
    /// error <c>firstImportDate</c>.
    /// </summary>
    public static DateOnly? ParseFirstImportDate(PortfolioInput? input, DateOnly today, FieldErrors errors, string prefix = "")
    {
        if (input?.FirstImportDate is not { } date)
        {
            return null;
        }

        var local = new FieldErrors();
        HoldingRules.CheckDate(local, "firstImportDate", date, today);
        errors.AddRange(local, prefix);
        return local.HasErrors ? null : date;
    }

    /// <summary>The roles of a request. Null (with errors) when a code is unknown.</summary>
    public static RolesChange? ParseRoles(RolesInput? input, FieldErrors errors)
    {
        if (input is null)
        {
            return new RolesChange(null, null, null);
        }

        DataRoleChange? data = null;
        if (input.Data is { } d)
        {
            DataRoleKind? kind = null;
            if (!string.IsNullOrWhiteSpace(d.Kind))
            {
                if (!AccountCodes.TryParseDataKind(d.Kind, out var parsed))
                {
                    errors.Add("roles.data.kind", "Use primary or standby.");
                    return null;
                }

                kind = parsed;
            }

            data = new DataRoleChange(d.Enabled, kind);
        }

        return new RolesChange(data, input.Trading?.Enabled, input.Portfolio?.Enabled);
    }

    /// <summary>
    /// The write-only API key and secret of a request, checked but never echoed. When <paramref name="required"/> both must be given;
    /// otherwise a value that is left out (null) means "unchanged", while an empty one is an error. Field names are <c>apiKey</c> and <c>apiSecret</c>.
    /// </summary>
    public static List<KeyValuePair<BrokerSecretName, SecretValue>> ParseSecrets(string? apiKey, string? apiSecret, bool required, FieldErrors errors)
    {
        var result = new List<KeyValuePair<BrokerSecretName, SecretValue>>(2);
        Parse(BrokerSecretName.ApiKey, apiKey);
        Parse(BrokerSecretName.ApiSecret, apiSecret);
        return result;

        void Parse(BrokerSecretName name, string? text)
        {
            if (text is null && !required)
            {
                return;
            }

            if (SecretValue.TryCreate(text, out var value, out var error))
            {
                result.Add(new(name, value));
            }
            else
            {
                errors.Add(AccountCodes.Field(name), !required && string.IsNullOrWhiteSpace(text) ? "Must not be empty. Leave it out to keep the current value." : error);
            }
        }
    }

    /// <summary>
    /// Adds the fields of a failed domain result to <paramref name="errors"/> and returns null. Returns the error itself for failures that
    /// are not field errors (conflicts, not found): the caller returns those unchanged. A success returns null and adds nothing.
    /// </summary>
    public static Error? Collect(Result result, FieldErrors errors, string prefix = "")
    {
        if (result.IsSuccess)
        {
            return null;
        }

        if (result.Error is not { Kind: ErrorKind.Validation, Fields: { } fields })
        {
            return result.Error;
        }

        var local = new FieldErrors();
        foreach (var field in fields)
        {
            local.Add(field.Field, field.Message);
        }

        errors.AddRange(local, prefix);
        return null;
    }

    private static Instruments ParseInstruments(IReadOnlyList<string>? codes, string field, FieldErrors errors)
    {
        var parsed = AccountCodes.ParseInstruments(codes, out var unknown);
        if (unknown is not null)
        {
            errors.Add(field, "Use EQ_MIS, EQ_CNC or FUT.");
        }

        return parsed;
    }
}
