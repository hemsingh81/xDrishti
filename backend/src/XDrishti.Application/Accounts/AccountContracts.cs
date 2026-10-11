using XDrishti.Application.Portfolio;

namespace XDrishti.Application.Accounts;

// Request and response shapes of the accounts API. Enum-like values travel as lowercase strings (see AccountCodes) and are
// validated by the handlers, so a wrong value comes back as accounts.invalid with the field name instead of a generic binding error.

/// <summary>Data role of an account: on/off and which kind.</summary>
public sealed record DataRoleView(bool Enabled, bool Primary, bool Standby);

/// <summary>Trading role. Mode is "paper" or "manual_live"; Profile is the name of the risk profile (null when none is chosen).</summary>
public sealed record TradingRoleView(bool Enabled, string Mode, string? Profile);

public sealed record PortfolioRoleView(bool Enabled);

public sealed record RolesView(DataRoleView Data, TradingRoleView Trading, PortfolioRoleView Portfolio);

/// <summary>
/// Whether one of the owner's Dhan secrets is stored (encrypted) and when it last changed. Name is the request field name ("apiKey", "apiSecret"). The value itself is never returned.
/// </summary>
public sealed record SecretStatusView(string Name, bool Set, DateTimeOffset? UpdatedAt);

/// <summary>
/// The account's connection to Dhan. Status is "not_connected", "connected", "expired" (the expiry has passed — derived when read,
/// no broker call needed). ExpiresAt is when the token stops working (as Dhan states, otherwise 24 hours after sign-in).
/// BrokerUserName and BrokerUserId (masked like the client id) are what Dhan reported for the token. LastError is a short safe text (at most
/// Dhan's error code). The token itself is never returned.
/// </summary>
public sealed record ConnectionView(
    string Status,
    DateTimeOffset? ExpiresAt,
    string? BrokerUserName,
    string? BrokerUserId,
    DateTimeOffset? LastConnectedAt,
    string? LastError);

/// <summary>
/// One row of the accounts list. Broker is "dhan"; MaskedClientId shows the first four and last two characters; Secrets lists the
/// secret fields with set / not set; ApiKeyExpiresOn is the first day the Dhan API key is no longer valid; TokenStatus repeats
/// Connection.Status (for the list's health dot).
/// </summary>
public sealed record AccountSummary(
    Guid Id,
    string Slug,
    string Name,
    string Broker,
    string MaskedClientId,
    string Color,
    RolesView Roles,
    IReadOnlyList<SecretStatusView> Secrets,
    DateOnly ApiKeyExpiresOn,
    string TokenStatus,
    ConnectionView Connection);

public sealed record InstrumentsView(IReadOnlyList<string> Intraday, IReadOnlyList<string> SwingBuy, IReadOnlyList<string> SwingSell);

/// <summary>Per-account replacements for values of the risk profile; null means "use the profile's value".</summary>
public sealed record AccountOverrides(decimal? RiskPerTradePct, int? MaxNewEntriesPerDay, int? MaxOpenPositions, decimal? DailyLossPct);

/// <summary>Trading configuration. Mode is "paper" or "manual_live", CapitalSource is "fixed" or "broker_funds", Styles holds "intraday" and/or "swing"; instruments are EQ_MIS, EQ_CNC or FUT.</summary>
public sealed record TradingSettingsView(
    bool Enabled,
    string Mode,
    Guid? RiskProfileId,
    string? RiskProfileName,
    string CapitalSource,
    decimal? CapitalAmount,
    IReadOnlyList<string> Styles,
    InstrumentsView Instruments,
    AccountOverrides Overrides);

/// <summary>
/// Portfolio configuration. DefaultBucket is the name of the bucket imports go to (the account's own default bucket unless a custom bucket was chosen);
/// AutoBucketName is the name of the account's own default bucket ("{account name} · Portfolio") — the one that exists, or, while the account has none yet,
/// the name it would get now (with a numeric suffix when the plain name is taken); it is chosen for real when the role is switched on.
/// The holdings refresh by themselves according to RefreshMode: "off" (default) or "daily" (once a day from RefreshDailyTimeIst, "HH:mm" India time, default 16:00;
/// the time is kept while the mode is off). FirstImportDate is the date holdings get when they are imported for the first time without a date of their own
/// (2025-01-01 until the owner chooses another). HasHoldings is true once the account has any holding; the first import has happened then and
/// FirstImportDate no longer affects existing holdings (ADR 0013).
/// </summary>
public sealed record PortfolioSettingsView(
    bool Enabled,
    string DefaultBucket,
    string AutoBucketName,
    bool IncludeInTotals,
    string RefreshMode,
    string RefreshDailyTimeIst,
    DateOnly FirstImportDate,
    bool HasHoldings);

public sealed record DataSettingsView(bool Enabled, bool Primary, bool Standby, int LiveFeedBudget);

/// <summary>
/// Everything about one account. Secret values and tokens are never part of it (only set / not set); the client id is masked. ApiKeyIssuedOn and
/// ApiKeyExpiresOn are the day the Dhan API key was generated and the first day it is no longer valid (12 months). TokenStatus repeats Connection.Status.
/// </summary>
public sealed record AccountDetail(
    Guid Id,
    string Slug,
    string Name,
    string Broker,
    string MaskedClientId,
    string Color,
    string? Notes,
    IReadOnlyList<SecretStatusView> Secrets,
    DateOnly ApiKeyIssuedOn,
    DateOnly ApiKeyExpiresOn,
    string TokenStatus,
    ConnectionView Connection,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RolesView Roles,
    TradingSettingsView Trading,
    PortfolioSettingsView Portfolio,
    DataSettingsView Data);

/// <summary>One audit entry of an account.</summary>
public sealed record AccountEventView(DateTimeOffset At, string Event, string? Detail, string Actor);

/// <summary>Switch the Data role. Kind ("primary" or "standby") is required when Enabled is true.</summary>
public sealed record DataRoleInput(bool Enabled, string? Kind);

public sealed record RoleToggleInput(bool Enabled);

/// <summary>Roles to switch; a role that is left out stays as it is.</summary>
public sealed record RolesInput(DataRoleInput? Data, RoleToggleInput? Trading, RoleToggleInput? Portfolio);

public sealed record InstrumentsInput(IReadOnlyList<string>? Intraday, IReadOnlyList<string>? SwingBuy, IReadOnlyList<string>? SwingSell);

/// <summary>The complete trading configuration (a replacement, not a patch); whether the role is on is changed with the roles. Mode is "paper" (default) or "manual_live"; CapitalSource is "fixed" (default) or "broker_funds"; Styles holds "intraday" and/or "swing"; allowed instruments: intraday EQ_MIS and FUT, swing buy EQ_CNC and FUT, swing sell FUT.</summary>
public sealed record TradingInput(
    string? Mode,
    Guid? RiskProfileId,
    string? CapitalSource,
    decimal? CapitalAmount,
    IReadOnlyList<string>? Styles,
    InstrumentsInput? Instruments,
    AccountOverrides? Overrides);

/// <summary>
/// The portfolio configuration to change. <b>Every field that is left out (null) keeps the account's current value</b>, on update. DefaultBucket: the name of
/// the bucket imports go to — a custom bucket's name; an empty string "" (or the name of the account's own "{account name} · Portfolio" bucket) selects the account's
/// own default bucket; null or omitted keeps the current choice. IncludeInTotals: true or false; null keeps it. At account creation, omitted means the account's own
/// bucket and true. The automatic holdings refresh: RefreshMode "off" or "daily"; RefreshDailyTimeIst "HH:mm" (India time); left out keeps the current value (a new
/// account: off, 16:00). FirstImportDate (not in the future, not before 1990-01-01) is the date the first import gives holdings without a date of their own.
/// FirstImport is honoured only when updating the portfolio settings of an account (it is ignored at account creation): see <see cref="FirstImportInput"/>.
/// </summary>
public sealed record PortfolioInput(
    string? DefaultBucket,
    bool? IncludeInTotals,
    string? RefreshMode = null,
    string? RefreshDailyTimeIst = null,
    DateOnly? FirstImportDate = null,
    FirstImportInput? FirstImport = null);

/// <summary>
/// The holdings to import when the portfolio settings are saved (ADR 0013). Rows are keys from <c>holdings/preview</c> with an optional acquisition date each
/// (a row without one gets the first import date, flagged as such); at most 5000 rows. Used only when the account is connected to Dhan and has no holdings yet;
/// an empty list imports nothing.
/// </summary>
public sealed record FirstImportInput(IReadOnlyList<ImportRowInput>? Rows);

/// <summary>
/// What a first import did. Status: "imported" (the chosen holdings were created; the counts say how many), "skipped" (the account already has holdings, nothing was
/// imported), "deferred" (the account is not connected to Dhan, or the Portfolio role is off: the first sync imports every holding, dated FirstImportDate) or
/// "failed" (the settings were saved but the import was not: ErrorCode is <c>portfolio.import_failed</c>, Cause the code of the reason, for example
/// <c>portfolio.not_connected</c>, <c>portfolio.sync_running</c>; Message says what to do; save again or press Sync now to retry). Imported counts holdings created, DefaultDated those
/// among them dated by the first import date, AlreadyImported keys the account had already, Unknown keys Dhan no longer lists.
/// </summary>
public sealed record FirstImportView(
    string Status,
    int Imported,
    int DefaultDated,
    int AlreadyImported,
    int Unknown,
    string? ErrorCode,
    string? Cause,
    string? Message);

/// <summary>The answer of saving the portfolio settings: the account as saved and, when a first import was asked for, what it did (otherwise null).</summary>
public sealed record PortfolioSaved(AccountDetail Account, FirstImportView? Import);

public sealed record DataInput(int? LiveFeedBudget);

/// <summary>
/// The Dhan API key and secret, write-only (app_id / app_secret). Surrounding spaces are
/// removed; the values are stored encrypted and never returned.
/// </summary>
public sealed record SecretsInput(string? ApiKey, string? ApiSecret)
{
    public override string ToString() => $"{nameof(SecretsInput)} {{ ApiKey = {Redact(ApiKey)}, ApiSecret = {Redact(ApiSecret)} }}";

    internal static string Redact(string? value) => value is null ? "null" : Domain.Accounts.SecretValue.Redacted;
}

/// <summary>
/// A new Dhan account. ClientId and Secrets (API key and secret) are required. ApiKeyIssuedOn is the day the API key was generated
/// (default today, India time).
/// </summary>
public sealed record CreateAccountInput(
    string? Name,
    string? ClientId,
    SecretsInput? Secrets,
    DateOnly? ApiKeyIssuedOn,
    string? Color,
    string? Notes,
    RolesInput? Roles,
    TradingInput? Trading,
    PortfolioInput? Portfolio,
    DataInput? Data);

/// <summary>
/// New connection details of an account. Leave ClientId out to keep the current client id.
/// </summary>
public sealed record ConnectionInput(string? Name, string? ClientId, string? Color, string? Notes);

/// <summary>
/// Replaces the account's API key and/or secret (write-only). A value that is left out stays as it is; an empty one is invalid.
/// ApiKeyIssuedOn is the day the key was generated: it defaults to today when a new API key is given and can also be corrected on its own.
/// </summary>
public sealed record UpdateSecretsInput(string? ApiKey, string? ApiSecret, DateOnly? ApiKeyIssuedOn)
{
    public override string ToString() =>
        $"{nameof(UpdateSecretsInput)} {{ ApiKey = {SecretsInput.Redact(ApiKey)}, ApiSecret = {SecretsInput.Redact(ApiSecret)}, ApiKeyIssuedOn = {ApiKeyIssuedOn} }}";
}
