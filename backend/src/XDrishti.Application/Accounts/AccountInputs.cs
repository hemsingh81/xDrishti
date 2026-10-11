using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

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

    public static PortfolioSpec ParsePortfolio(PortfolioInput? input) =>
        new(input?.DefaultBucket, input?.HistoryFrom, input?.IncludeInTotals ?? true);

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
