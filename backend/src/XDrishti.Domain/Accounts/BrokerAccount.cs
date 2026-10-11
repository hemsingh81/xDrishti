using System.Globalization;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// A Dhan broker account of the owner with its roles (Data, Trading, Portfolio). The broker's API key, secret and tokens are not part of it:
/// they live encrypted in the broker vault (ADR 0008). The account only knows when its API key was generated (Dhan keys expire).
/// Archived accounts keep their history but are inactive.
/// </summary>
public sealed class BrokerAccount : Entity<Guid>
{
    public const int MaxNameLength = 80;

    public const int MaxSlugLength = 60;

    /// <summary>How many months a Dhan API key stays valid after it was generated.</summary>
    public const int ApiKeyValidityMonths = 12;

    public const int MinClientIdLength = 4;

    public const int MaxClientIdLength = 32;

    public const int MaxNotesLength = 500;

    public const string DefaultColor = "#5B8DEF";

    public const int DefaultLiveFeedBudget = 200;

    public const int MaxLiveFeedBudget = 5000;

    // Parameter names match property names so EF Core can bind this constructor when materialising (the owned settings are set by EF afterwards).
    private BrokerAccount(
        Guid id,
        string slug,
        string name,
        string clientId,
        DateOnly apiKeyIssuedOn,
        string color,
        string? notes,
        DateTimeOffset createdAt)
        : base(id)
    {
        Slug = slug;
        Name = name;
        Broker = Broker.Dhan;
        ClientId = clientId;
        ApiKeyIssuedOn = apiKeyIssuedOn;
        Color = color;
        Notes = notes;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        LiveFeedBudget = DefaultLiveFeedBudget;
        Trading = TradingSettings.CreateDefault();
        Portfolio = PortfolioSettings.CreateDefault();
    }

    /// <summary>Stable short identifier derived from the name when the account was created; unique among active accounts.</summary>
    public string Slug { get; private set; }

    public string Name { get; private set; }

    public Broker Broker { get; private set; }

    /// <summary>The Dhan client id. Not a secret, but shown masked everywhere (<see cref="MaskedClientId"/>).</summary>
    public string ClientId { get; private set; }

    /// <summary>The day the owner generated the API key (Dhan keys expire after <see cref="ApiKeyValidityMonths"/> months).</summary>
    public DateOnly ApiKeyIssuedOn { get; private set; }

    public string Color { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Data role on. A primary or standby account always has it on.</summary>
    public bool DataEnabled { get; private set; }

    public bool DataPrimary { get; private set; }

    public bool DataStandby { get; private set; }

    /// <summary>How many instruments the live feed may stream for this account (kept when the Data role is off).</summary>
    public int LiveFeedBudget { get; private set; }

    public TradingSettings Trading { get; private set; }

    public PortfolioSettings Portfolio { get; private set; }

    /// <summary>First four and last two characters; the rest is hidden. Short ids are hidden completely.</summary>
    public string MaskedClientId => MaskClientId(ClientId);

    /// <summary>The first day the API key is no longer valid.</summary>
    public DateOnly ApiKeyExpiresOn => ExpiryOf(ApiKeyIssuedOn);

    public static string MaskClientId(string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        return clientId.Length <= 6
            ? new string('•', clientId.Length)
            : string.Concat(clientId.AsSpan(0, 4), new string('•', clientId.Length - 6), clientId.AsSpan(clientId.Length - 2));
    }

    /// <summary>Lower-case letters and digits separated by single hyphens; empty when the name has none.</summary>
    public static string Slugify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        return slug.Length <= MaxSlugLength ? slug : slug[..MaxSlugLength].TrimEnd('-');
    }

    /// <summary>
    /// A new Dhan account. The client id is required. <paramref name="apiKeyIssuedOn"/> is the day the API key was generated (default
    /// <paramref name="today"/>). <paramref name="today"/> is the owner's calendar day (India).
    /// </summary>
    public static Result<BrokerAccount> Create(
        string? name, string? clientId, DateOnly? apiKeyIssuedOn, string? color, string? notes, DateOnly today, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var clean = ValidateConnection(name, clientId, requireClientId: true, color, notes, errors);
        var issuedOn = ValidateIssuedOn(apiKeyIssuedOn, today, errors);
        if (errors.HasErrors)
        {
            return Result.Failure<BrokerAccount>(AccountErrors.Invalid(errors));
        }

        var slug = Slugify(clean.Name);
        return Result.Success(new BrokerAccount(
            Guid.CreateVersion7(now), slug, clean.Name, clean.ClientId!, issuedOn, clean.Color, clean.Notes, now));
    }

    /// <summary>
    /// Changes name, colour and notes; the client id only when one is given (it cannot be removed). The slug stays.
    /// </summary>
    public Result UpdateConnection(string? name, string? clientId, string? color, string? notes, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var clean = ValidateConnection(name, clientId, requireClientId: false, color, notes, errors);
        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        Name = clean.Name;
        ClientId = clean.ClientId ?? ClientId;
        Color = clean.Color;
        Notes = clean.Notes;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Records the day a (new) API key was generated. Not in the future, and the key must not have
    /// expired already. Field name of a rejection: <c>apiKeyIssuedOn</c>.
    /// </summary>
    public Result ChangeApiKeyIssuedOn(DateOnly issuedOn, DateOnly today, DateTimeOffset now)
    {
        var errors = new FieldErrors();
        var clean = ValidateIssuedOn(issuedOn, today, errors);
        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        ApiKeyIssuedOn = clean;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Notes that secrets of the account were replaced in the vault (the values themselves are never part of the account).</summary>
    public void SecretsReplaced(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>True when Dhan's client id names this account's client id (letters compared without case).</summary>
    public bool IsClientId(string? brokerUserId) =>
        brokerUserId is not null && string.Equals(ClientId, brokerUserId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The first day an API key generated on <paramref name="issuedOn"/> is no longer valid.</summary>
    public static DateOnly ExpiryOf(DateOnly issuedOn) => issuedOn.AddMonths(ApiKeyValidityMonths);

    /// <summary>
    /// Switches roles on or off, all or nothing. Needs to know the current primary and standby Data accounts (other than this one
    /// is fine too): there is at most one of each, and the primary can only be replaced with "make primary".
    /// </summary>
    public Result ChangeRoles(RolesChange change, BrokerAccount? currentPrimary, BrokerAccount? currentStandby, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);
        var errors = new FieldErrors();
        Result dataCheck = Result.Success();
        if (change.Data is { } data)
        {
            dataCheck = CheckDataChange(data, currentPrimary, currentStandby, errors);
        }

        if (change.Trading == true)
        {
            errors.AddRange(Trading.CheckCanEnable());
        }

        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        if (dataCheck.IsFailure)
        {
            return dataCheck;
        }

        if (change.Data is { } apply)
        {
            if (!apply.Enabled)
            {
                DisableData(now);
            }
            else
            {
                SetDataKind(apply.Kind!.Value, now);
            }
        }

        if (change.Trading is { } trading)
        {
            Trading.SetEnabled(trading);
        }

        if (change.Portfolio is { } portfolio)
        {
            Portfolio.SetEnabled(portfolio);
        }

        UpdatedAt = now;
        return Result.Success();
    }

    public Result ConfigureTrading(TradingSpec spec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var result = Trading.Configure(spec);
        if (result.IsSuccess)
        {
            UpdatedAt = now;
        }

        return result;
    }

    /// <summary>Stores the portfolio settings. A default bucket in <paramref name="spec"/> must be a custom bucket (checked by the caller).</summary>
    public void ConfigurePortfolio(PortfolioSpec spec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Portfolio.Configure(spec);
        UpdatedAt = now;
    }

    public Result ConfigureData(int? liveFeedBudget, DateTimeOffset now)
    {
        if (liveFeedBudget is not { } budget || budget < 0 || budget > MaxLiveFeedBudget)
        {
            return Result.Failure(AccountErrors.Invalid("liveFeedBudget", $"Must be between 0 and {MaxLiveFeedBudget}."));
        }

        LiveFeedBudget = budget;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Removes the account from lists and services; its history and audit trail stay. The primary Data account cannot be archived.</summary>
    public Result Archive(DateTimeOffset now)
    {
        if (ArchivedAt is not null)
        {
            return Result.Failure(AccountErrors.NotFound);
        }

        if (DataPrimary)
        {
            return Result.Failure(AccountErrors.Conflict("The primary Data account cannot be removed. Make another account primary first."));
        }

        ArchivedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    internal void ClearDataKind(DateTimeOffset now)
    {
        DataPrimary = false;
        DataStandby = false;
        UpdatedAt = now;
    }

    internal void SetDataKind(DataRoleKind kind, DateTimeOffset now)
    {
        DataEnabled = true;
        DataPrimary = kind == DataRoleKind.Primary;
        DataStandby = kind == DataRoleKind.Standby;
        UpdatedAt = now;
    }

    internal void DisableData(DateTimeOffset now)
    {
        DataEnabled = false;
        DataPrimary = false;
        DataStandby = false;
        UpdatedAt = now;
    }

    private Result CheckDataChange(DataRoleChange data, BrokerAccount? primary, BrokerAccount? standby, FieldErrors errors)
    {
        if (!data.Enabled)
        {
            return DataPrimary
                ? Result.Failure(AccountErrors.Conflict("The primary Data account cannot lose the Data role. Make another account primary first."))
                : Result.Success();
        }

        if (data.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            errors.Add("roles.data.kind", "Choose primary or standby.");
            return Result.Success();
        }

        if (kind == DataRoleKind.Primary && !DataPrimary && primary is not null && primary.Id != Id)
        {
            return Result.Failure(AccountErrors.Conflict($"'{primary.Name}' is already the primary Data account. Use \"make primary\" to switch."));
        }

        if (kind == DataRoleKind.Standby)
        {
            if (DataPrimary)
            {
                return Result.Failure(AccountErrors.Conflict("The primary Data account cannot become standby. Make another account primary first."));
            }

            if (standby is not null && standby.Id != Id)
            {
                return Result.Failure(AccountErrors.Conflict($"'{standby.Name}' is already the standby Data account."));
            }
        }

        return Result.Success();
    }

    private readonly record struct CleanConnection(string Name, string? ClientId, string Color, string? Notes);

    private static CleanConnection ValidateConnection(string? name, string? clientId, bool requireClientId, string? color, string? notes, FieldErrors errors)
    {
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is 0 or > MaxNameLength || cleanName.Any(char.IsControl))
        {
            errors.Add("name", $"Required, at most {MaxNameLength} characters.");
        }
        else if (Slugify(cleanName).Length == 0)
        {
            errors.Add("name", "Use at least one letter or digit.");
        }

        var cleanClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        if (cleanClientId is null ? requireClientId : !IsValidClientId(cleanClientId))
        {
            errors.Add("clientId", $"{(requireClientId ? "Required: " : "Use ")}{MinClientIdLength} to {MaxClientIdLength} letters, digits, '_' or '-'.");
        }

        var cleanColor = string.IsNullOrWhiteSpace(color) ? DefaultColor : color.Trim();
        if (!IsValidColor(cleanColor))
        {
            errors.Add("color", "Use a colour like #1A2B3C.");
        }

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (cleanNotes is { Length: > MaxNotesLength })
        {
            errors.Add("notes", $"At most {MaxNotesLength} characters.");
        }

        return new CleanConnection(cleanName, cleanClientId, cleanColor.ToUpper(CultureInfo.InvariantCulture), cleanNotes);
    }

    /// <summary>The issue date to store: the given one, default today.</summary>
    private static DateOnly ValidateIssuedOn(DateOnly? issuedOn, DateOnly today, FieldErrors errors)
    {
        var day = issuedOn ?? today;
        if (day > today)
        {
            errors.Add("apiKeyIssuedOn", "Cannot be in the future.");
        }
        else if (ExpiryOf(day) <= today)
        {
            errors.Add("apiKeyIssuedOn", "A key generated on that day has expired already. Generate a new one.");
        }

        return day;
    }

    private static bool IsValidClientId(string value) =>
        value.Length is >= MinClientIdLength and <= MaxClientIdLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool IsValidColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);
}
