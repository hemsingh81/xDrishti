using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts;

/// <summary>The lowercase wire names of the account enums, and parsing of them. One place, so the API and the audit text agree.</summary>
internal static class AccountCodes
{
    public const string Intraday = "intraday";

    public const string Swing = "swing";

    public static string Name(Broker broker) => broker switch
    {
        Broker.Dhan => "dhan",
        _ => throw new ArgumentOutOfRangeException(nameof(broker)),
    };

    /// <summary>The camelCase field name of a secret, as in requests (<c>secrets.apiKey</c>) and responses.</summary>
    public static string Field(BrokerSecretName name) => name switch
    {
        BrokerSecretName.ApiKey => "apiKey",
        BrokerSecretName.ApiSecret => "apiSecret",
        BrokerSecretName.AccessToken => "accessToken",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>For the audit trail: "API key", "API secret", …</summary>
    public static string Label(BrokerSecretName name) => name switch
    {
        BrokerSecretName.ApiKey => "API key",
        BrokerSecretName.ApiSecret => "API secret",
        BrokerSecretName.AccessToken => "access token",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static string Name(ConnectionStatus status) => status switch
    {
        ConnectionStatus.NotConnected => "not_connected",
        ConnectionStatus.Connected => "connected",
        ConnectionStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Name(TradingMode mode) => mode switch
    {
        TradingMode.Paper => "paper",
        TradingMode.ManualLive => "manual_live",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string Name(CapitalSource source) => source switch
    {
        CapitalSource.Fixed => "fixed",
        CapitalSource.BrokerFunds => "broker_funds",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public static string Name(WeeklyLossAction action) => action switch
    {
        WeeklyLossAction.HalveSize => "halve_size",
        WeeklyLossAction.StopNewEntries => "stop_new_entries",
        WeeklyLossAction.AlertOnly => "alert_only",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static string Name(DataRoleKind kind) => kind switch
    {
        DataRoleKind.Primary => "primary",
        DataRoleKind.Standby => "standby",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static IReadOnlyList<string> Names(Instruments instruments)
    {
        var names = new List<string>(3);
        if (instruments.HasFlag(Instruments.EquityIntraday))
        {
            names.Add("EQ_MIS");
        }

        if (instruments.HasFlag(Instruments.EquityDelivery))
        {
            names.Add("EQ_CNC");
        }

        if (instruments.HasFlag(Instruments.Futures))
        {
            names.Add("FUT");
        }

        return names;
    }

    public static bool TryParseMode(string? text, out TradingMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "paper":
                mode = TradingMode.Paper;
                return true;
            case "manual_live":
                mode = TradingMode.ManualLive;
                return true;
            default:
                mode = TradingMode.Paper;
                return false;
        }
    }

    public static bool TryParseCapitalSource(string? text, out CapitalSource source)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "fixed":
                source = CapitalSource.Fixed;
                return true;
            case "broker_funds":
                source = CapitalSource.BrokerFunds;
                return true;
            default:
                source = CapitalSource.Fixed;
                return false;
        }
    }

    public static bool TryParseWeeklyLossAction(string? text, out WeeklyLossAction action)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "halve_size":
                action = WeeklyLossAction.HalveSize;
                return true;
            case "stop_new_entries":
                action = WeeklyLossAction.StopNewEntries;
                return true;
            case "alert_only":
                action = WeeklyLossAction.AlertOnly;
                return true;
            default:
                action = WeeklyLossAction.HalveSize;
                return false;
        }
    }

    public static bool TryParseDataKind(string? text, out DataRoleKind kind)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "primary":
                kind = DataRoleKind.Primary;
                return true;
            case "standby":
                kind = DataRoleKind.Standby;
                return true;
            default:
                kind = DataRoleKind.Primary;
                return false;
        }
    }

    /// <summary>Parses a list of instrument codes (EQ_MIS, EQ_CNC, FUT); unknown codes are reported by <paramref name="unknown"/>.</summary>
    public static Instruments ParseInstruments(IReadOnlyList<string>? codes, out string? unknown)
    {
        unknown = null;
        var result = Instruments.None;
        foreach (var code in codes ?? [])
        {
            switch (code?.Trim().ToUpperInvariant())
            {
                case "EQ_MIS":
                    result |= Instruments.EquityIntraday;
                    break;
                case "EQ_CNC":
                    result |= Instruments.EquityDelivery;
                    break;
                case "FUT":
                    result |= Instruments.Futures;
                    break;
                default:
                    unknown ??= "unknown";
                    break;
            }
        }

        return result;
    }
}
