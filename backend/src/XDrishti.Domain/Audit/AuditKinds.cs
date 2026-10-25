using System.Text;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Auth;

namespace XDrishti.Domain.Audit;

/// <summary>
/// The category and outcome of every kind the audit screen shows (spec P8-22). The category of a kind is a table here, not a guess: a test fails when an <see cref="AuditEventType"/> or <see cref="AccountEventType"/> value has no row, so a later writer
/// slice cannot ship an unclassified kind. No store records success or failure; the outcome is derived from the kind and <c>ok</c> means "recorded", not "verified". Pure.
/// </summary>
public static class AuditKinds
{
    public const string SignIn = "sign_in";
    public const string Security = "security";
    public const string Broker = "broker";
    public const string Service = "service";
    public const string Alert = "alert";
    public const string Configuration = "configuration";
    public const string MarketData = "market_data";
    public const string PortfolioData = "portfolio_data";
    public const string PlanDecision = "plan_decision";
    public const string PlanState = "plan_state";
    public const string Autonomy = "autonomy";
    public const string Learning = "learning";

    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string Refused = "refused";

    private static readonly Dictionary<AuditEventType, string> AuthCategories = Build<AuditEventType>(
        (SignIn, [AuditEventType.LoginSucceeded, AuditEventType.LoginFailed, AuditEventType.AccountLocked, AuditEventType.LoginRefusedLocked, AuditEventType.Logout, AuditEventType.SessionExpiredIdle, AuditEventType.SessionExpiredAbsolute]),
        (Security, [AuditEventType.OwnerProvisioned, AuditEventType.OwnerPasswordReset, AuditEventType.StepUpSucceeded, AuditEventType.StepUpFailed, AuditEventType.PasswordChanged, AuditEventType.ProfileUpdated]));

    private static readonly Dictionary<AccountEventType, string> AccountCategories = Build<AccountEventType>(
        (Security, [AccountEventType.RolesChanged, AccountEventType.PrimaryDataChanged, AccountEventType.SecretsReplaced]),
        (Broker, [AccountEventType.ConnectionUpdated, AccountEventType.BrokerConnected, AccountEventType.BrokerConnectFailed, AccountEventType.BrokerDisconnected]),
        (Service, [AccountEventType.ServiceScheduleChanged, AccountEventType.ServicePaused, AccountEventType.ServiceResumed, AccountEventType.ServiceRunRequested]),
        (Alert, [AccountEventType.ActionSnoozed, AccountEventType.ActionDone, AccountEventType.ActionReopened, AccountEventType.ActionThresholdsChanged, AccountEventType.PriceAlertCreated, AccountEventType.PriceAlertTriggered, AccountEventType.PriceAlertCancelled]),
        (Configuration,
        [
            AccountEventType.AccountCreated, AccountEventType.AccountArchived, AccountEventType.TradingUpdated, AccountEventType.PortfolioUpdated, AccountEventType.DataUpdated, AccountEventType.RiskProfileCreated, AccountEventType.RiskProfileUpdated, AccountEventType.RiskProfileDeleted,
            AccountEventType.BucketAdded, AccountEventType.BucketCreated, AccountEventType.BucketRenamed, AccountEventType.PortfolioRefreshSettingsChanged, AccountEventType.PortfolioBenchmarkChanged, AccountEventType.FirstImportDateChanged,
        ]),
        (MarketData,
        [
            AccountEventType.BasketCreated, AccountEventType.BasketRenamed, AccountEventType.BasketUpdated, AccountEventType.BasketArchived, AccountEventType.BasketsReordered, AccountEventType.BasketMembersAdded, AccountEventType.BasketMembersRemoved, AccountEventType.BasketMembersReordered,
            AccountEventType.BasketMembersTransferred, AccountEventType.InstrumentsTracked, AccountEventType.InstrumentsUntracked, AccountEventType.CalendarDayAdded, AccountEventType.CalendarDayConfirmed, AccountEventType.CalendarDayRemoved, AccountEventType.CalendarRefreshedFromNse,
            AccountEventType.CorporateActionAdded, AccountEventType.CorporateActionConfirmed, AccountEventType.CorporateActionEdited, AccountEventType.CorporateActionIgnored, AccountEventType.CorporateActionsImported, AccountEventType.CsvLoadUndone,
        ]),
        (PortfolioData,
        [
            AccountEventType.HoldingsImported, AccountEventType.HoldingsSynced, AccountEventType.LotDateChanged, AccountEventType.TradesImported, AccountEventType.TradesUndone, AccountEventType.TradeAdded, AccountEventType.TradeVoided, AccountEventType.TradesSuperseded, AccountEventType.LotsRebuilt,
            AccountEventType.BrokerHistorySynced, AccountEventType.DividendAdded, AccountEventType.DividendVoided, AccountEventType.DividendSuggestionAccepted, AccountEventType.DividendSuggestionDismissed, AccountEventType.DividendsImported, AccountEventType.DividendsUndone,
            AccountEventType.DividendAliasSaved, AccountEventType.CostsImported, AccountEventType.CostsUndone, AccountEventType.SecurityNamesLearned,
        ]));

    public static IReadOnlyList<string> Categories { get; } = [SignIn, Security, Broker, Service, Alert, Configuration, MarketData, PortfolioData, PlanDecision, PlanState, Autonomy, Learning];

    public static IReadOnlyList<string> Outcomes { get; } = [Ok, Failed, Refused];

    /// <summary>The kinds of the four stores whose rows carry no event name, and what they are called on screen.</summary>
    public static IReadOnlyDictionary<string, (AuditSource Source, string Category, string Label)> Synthetic { get; } = new Dictionary<string, (AuditSource, string, string)>(StringComparer.Ordinal)
    {
        ["approval.approve"] = (AuditSource.Approval, PlanDecision, "Ticket approved"),
        ["approval.modify"] = (AuditSource.Approval, PlanDecision, "Ticket modified"),
        ["approval.reject"] = (AuditSource.Approval, PlanDecision, "Ticket rejected"),
        ["plan.moved"] = (AuditSource.Plan, PlanState, "Plan moved"),
        ["plan.ticket_moved"] = (AuditSource.Plan, PlanState, "Ticket moved"),
        ["autonomy.changed"] = (AuditSource.Autonomy, Autonomy, "Autonomy settings changed"),
        ["learning.transition"] = (AuditSource.Learning, Learning, "Cell moved"),
        ["learning.designation"] = (AuditSource.Learning, Learning, "Model designated"),
        ["learning.revert"] = (AuditSource.Learning, Learning, "Move reverted"),
    };

    /// <summary>The category of a sign-in or security event; null for a value that has no row.</summary>
    public static string? CategoryOf(AuditEventType type) => AuthCategories.GetValueOrDefault(type);

    /// <summary>The category of an account event; null for a value that has no row.</summary>
    public static string? CategoryOf(AccountEventType type) => AccountCategories.GetValueOrDefault(type);

    public static string OutcomeOf(AuditEventType type) => type switch
    {
        AuditEventType.LoginFailed or AuditEventType.StepUpFailed => Failed,
        AuditEventType.AccountLocked or AuditEventType.LoginRefusedLocked => Refused,
        _ => Ok,
    };

    public static string OutcomeOf(AccountEventType type) => type == AccountEventType.BrokerConnectFailed ? Failed : Ok;

    /// <summary>The category of a stored kind of a source (the event name, or a synthetic kind); "other" for a name the table does not know (a value added by a newer writer than this reader).</summary>
    public static string CategoryOf(AuditSource source, string kind) => source switch
    {
        AuditSource.Auth => Enum.TryParse<AuditEventType>(kind, out var auth) && Enum.IsDefined(auth) ? CategoryOf(auth) ?? Other : Other,
        AuditSource.Account => Enum.TryParse<AccountEventType>(kind, out var account) && Enum.IsDefined(account) ? CategoryOf(account) ?? Other : Other,
        _ => Synthetic.TryGetValue(kind, out var synthetic) ? synthetic.Category : Other,
    };

    public static string OutcomeOf(AuditSource source, string kind) => source switch
    {
        AuditSource.Auth => Enum.TryParse<AuditEventType>(kind, out var auth) ? OutcomeOf(auth) : Ok,
        AuditSource.Account => Enum.TryParse<AccountEventType>(kind, out var account) ? OutcomeOf(account) : Ok,
        _ => Ok,
    };

    /// <summary>Every kind of a source with its category, for the filter list.</summary>
    public static IReadOnlyList<(string Kind, string Category)> KindsOf(AuditSource source) => source switch
    {
        AuditSource.Auth => [.. AuthCategories.Select(p => (p.Key.ToString(), p.Value))],
        AuditSource.Account => [.. AccountCategories.Select(p => (p.Key.ToString(), p.Value))],
        _ => [.. Synthetic.Where(p => p.Value.Source == source).Select(p => (p.Key, p.Value.Category))],
    };

    /// <summary>The words shown for a kind: "LoginFailed" is "Login failed".</summary>
    public static string Label(AuditSource source, string kind)
    {
        if (Synthetic.TryGetValue(kind, out var synthetic) && synthetic.Source == source)
        {
            return synthetic.Label;
        }

        var words = new StringBuilder(kind.Length + 6);
        foreach (var c in kind)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                words.Append(c);
            }
        }

        return words.ToString();
    }

    /// <summary>The stored kinds of a source that match the category, outcome and kind filters (each optional).</summary>
    public static IReadOnlyList<string> KindsMatching(AuditSource source, IReadOnlyCollection<string>? categories, IReadOnlyCollection<string>? outcomes, IReadOnlyCollection<string>? kinds) =>
    [
        .. KindsOf(source)
            .Where(k => categories is null || categories.Contains(k.Category))
            .Where(k => outcomes is null || outcomes.Contains(OutcomeOf(source, k.Kind)))
            .Where(k => kinds is null || kinds.Contains(k.Kind))
            .Select(k => k.Kind),
    ];

    private const string Other = "other";

    private static Dictionary<TEnum, string> Build<TEnum>(params (string Category, TEnum[] Kinds)[] groups)
        where TEnum : struct, Enum
    {
        var table = new Dictionary<TEnum, string>();
        foreach (var (category, kinds) in groups)
        {
            foreach (var kind in kinds)
            {
                table.Add(kind, category);
            }
        }

        return table;
    }
}
