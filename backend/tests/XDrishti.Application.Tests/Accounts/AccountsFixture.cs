using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>Builds the accounts handlers on in-memory ports and a controllable clock. All names, ids and numbers are made up.</summary>
internal sealed class AccountsFixture
{
    public const string Actor = "owner@example.invalid";
    public const string ClientId = "1000000001";

    /// <summary>Marks every made-up secret value: it must never appear in a response, audit entry or log line.</summary>
    public const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    public const string ApiKey = "key-" + Marker;

    public const string ApiSecret = "secret-" + Marker;

    public const string PublicBaseUrl = "http://localhost:8080";

    public static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(Start);

    public InMemoryAccountData Data { get; } = new();

    public InMemoryConnectionData Connections { get; } = new();

    public RiskProfile Standard { get; }

    public RiskProfile Conservative { get; }

    public AccountsFixture()
    {
        Standard = SeedProfile("Standard", AccountDataTestValues.Standard);
        Conservative = SeedProfile("Conservative", AccountDataTestValues.Standard with { RiskPerTradePct = 0.25m, MaxNewEntriesPerDay = 2, MaxOpenPositions = 3, DailyLossPct = 1.0m });
        foreach (var name in new[] { "Trading", "MyLongTerm", "MyChoice" })
        {
            Data.Buckets.Add(PortfolioBucket.Create(name, Start).Value);
        }
    }

    public AccountViews Views => new(Data, Data, Connections, Clock);

    public ListAccountsHandler ListAccounts() => new(Data, Views);

    public GetAccountHandler GetAccount() => new(Data, Views);

    public GetAccountActivityHandler Activity() => new(Data, Data);

    public CreateAccountHandler Create() => new(Data, Data, Data, Data, Data, Data, Views, Clock, NullLogger<CreateAccountHandler>.Instance);

    public UpdateSecretsHandler UpdateSecrets() => new(Data, Data, Data, Data, Views, Clock, NullLogger<UpdateSecretsHandler>.Instance);

    public static GetDhanRedirectHandler DhanRedirect(string publicBaseUrl = PublicBaseUrl) => new(new FixedBrokerSettings(publicBaseUrl));

    public UpdateConnectionHandler UpdateConnection() =>
        new(Data, Data, Data, new ConnectionWriter(Data, Connections, Data), Connections, Views, Clock, NullLogger<UpdateConnectionHandler>.Instance);

    public UpdateRolesHandler UpdateRoles() => new(Data, Data, Data, Views, Clock, NullLogger<UpdateRolesHandler>.Instance);

    public MakePrimaryDataHandler MakePrimary() => new(Data, Data, Data, Views, Clock, NullLogger<MakePrimaryDataHandler>.Instance);

    public UpdateTradingHandler UpdateTrading() => new(Data, Data, Data, Data, Views, Clock, NullLogger<UpdateTradingHandler>.Instance);

    public UpdatePortfolioHandler UpdatePortfolio() => new(Data, Data, Data, Data, Views, Clock, NullLogger<UpdatePortfolioHandler>.Instance);

    public UpdateDataHandler UpdateData() => new(Data, Data, Data, Views, Clock, NullLogger<UpdateDataHandler>.Instance);

    public ArchiveAccountHandler Archive() => new(Data, Data, Connections, Connections, Data, Data, Clock, NullLogger<ArchiveAccountHandler>.Instance);

    public ListRiskProfilesHandler ListProfiles() => new(Data);

    public CreateRiskProfileHandler CreateProfile() => new(Data, Data, Data, Clock, NullLogger<CreateRiskProfileHandler>.Instance);

    public UpdateRiskProfileHandler UpdateProfile() => new(Data, Data, Data, Clock, NullLogger<UpdateRiskProfileHandler>.Instance);

    public DeleteRiskProfileHandler DeleteProfile() => new(Data, Data, Data, Clock, NullLogger<DeleteRiskProfileHandler>.Instance);

    public ListBucketsHandler ListBuckets() => new(Data);

    public AddBucketHandler AddBucket() => new(Data, Data, Data, Clock, NullLogger<AddBucketHandler>.Instance);

    /// <summary>Creates an account through the real handler (so it passes every rule) and returns it from the store.</summary>
    public async Task<BrokerAccount> CreatedAsync(CreateAccountInput? input = null)
    {
        var result = await Create().HandleAsync(new CreateAccountCommand(input ?? Inputs.Account(), Actor), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message + string.Join(',', result.Error.Fields?.Select(f => f.Field) ?? []) : string.Empty);
        return Data.Accounts.Single(a => a.Id == result.Value.Id);
    }

    private RiskProfile SeedProfile(string name, RiskProfileInput values)
    {
        var limits = RiskProfileMappingForTests.Limits(values);
        var profile = RiskProfile.Create(name, limits, Start, isSeeded: true).Value;
        Data.Profiles.Add(profile);
        return profile;
    }
}

internal sealed class FixedBrokerSettings(string publicBaseUrl) : IBrokerSettings
{
    public string PublicBaseUrl { get; } = publicBaseUrl;
}
