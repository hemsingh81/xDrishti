using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Portfolio;
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
        PortfolioData = new InMemoryPortfolioData(Data);
        Standard = SeedProfile("Standard", AccountDataTestValues.Standard);
        Conservative = SeedProfile("Conservative", AccountDataTestValues.Standard with { RiskPerTradePct = 0.25m, MaxNewEntriesPerDay = 2, MaxOpenPositions = 3, DailyLossPct = 1.0m });
    }

    /// <summary>The holdings persistence ports on the same accounts data (the portfolio handlers use them; the accounts views ask whether holdings exist).</summary>
    public InMemoryPortfolioData PortfolioData { get; }

    public FakeBrokerHoldings HoldingsDhan { get; } = new();

    public FakeInstrumentMapper HoldingsInstruments { get; } = new();

    public HoldingsFetcher Fetcher => new(Data, Data, Connections, Data, HoldingsDhan, HoldingsInstruments, Clock);

    public SyncRuns Runs => new(PortfolioData, PortfolioData, HealthRecorder, Clock, NullLogger<SyncRuns>.Instance);

    /// <summary>What the recorder calls when a sync ends: it only counts (the items themselves are tested on their own).</summary>
    public XDrishti.Application.Tests.Portfolio.Heal.RecordingAttentionReconciler Attention { get; } = new();

    public XDrishti.Application.Portfolio.Heal.SyncHealthRecorder HealthRecorder => new(PortfolioData.Health, Attention, Clock, NullLogger<XDrishti.Application.Portfolio.Heal.SyncHealthRecorder>.Instance);

    public HoldingsImporter Importer => new(Fetcher, Runs, PortfolioData, Data, Clock, NullLogger<HoldingsImporter>.Instance);

    public AccountViews Views => new(Data, Data, Data, Connections, PortfolioData, Clock);

    public DefaultBuckets DefaultBuckets => new(Data, Data);

    public ListAccountsHandler ListAccounts() => new(Data, Views);

    public GetAccountHandler GetAccount() => new(Data, Views);

    public GetAccountActivityHandler Activity() => new(Data, Data);

    public CreateAccountHandler Create() => new(Data, Data, DefaultBuckets, Data, Data, Data, Views, Clock, NullLogger<CreateAccountHandler>.Instance);

    public UpdateSecretsHandler UpdateSecrets() => new(Data, Data, Data, Data, Views, Clock, NullLogger<UpdateSecretsHandler>.Instance);

    public static GetDhanRedirectHandler DhanRedirect(string publicBaseUrl = PublicBaseUrl) => new(new FixedBrokerSettings(publicBaseUrl));

    public UpdateConnectionHandler UpdateConnection() =>
        new(Data, DefaultBuckets, Data, Data, new ConnectionWriter(Data, Connections, Data, Data), Connections, Views, Clock, NullLogger<UpdateConnectionHandler>.Instance);

    public UpdateRolesHandler UpdateRoles() => new(Data, DefaultBuckets, Data, Data, Views, Clock, NullLogger<UpdateRolesHandler>.Instance);

    public MakePrimaryDataHandler MakePrimary() => new(Data, Data, Data, Views, Clock, NullLogger<MakePrimaryDataHandler>.Instance);

    /// <summary>What the live-ready gate reads outside the account; by default nothing is ready (no step, services not registered).</summary>
    public XDrishti.Application.Abstractions.LiveTrading.LiveSourceFacts LiveFacts { get; set; } = new(false, XDrishti.Domain.LiveTrading.LiveServiceState.NotRegistered, XDrishti.Domain.LiveTrading.LiveServiceState.NotRegistered, null, null);

    public UpdateTradingHandler UpdateTrading() => new(Data, Data, Connections, new FakeReadiness(this), Data, Data, Views, Clock, NullLogger<UpdateTradingHandler>.Instance);

    private sealed class FakeReadiness(AccountsFixture fixture) : XDrishti.Application.Abstractions.LiveTrading.ILiveReadinessSource
    {
        public ValueTask<XDrishti.Application.Abstractions.LiveTrading.LiveSourceFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken) => ValueTask.FromResult(fixture.LiveFacts);
    }

    public UpdatePortfolioHandler UpdatePortfolio() => new(Data, DefaultBuckets, Fetcher, Importer, PortfolioData, Data, Data, Views, Clock, NullLogger<UpdatePortfolioHandler>.Instance);

    public UpdateDataHandler UpdateData() => new(Data, Data, Data, Views, Clock, NullLogger<UpdateDataHandler>.Instance);

    public ArchiveAccountHandler Archive() => new(Data, DefaultBuckets, Data, Data, Connections, Connections, Data, Data, Clock, NullLogger<ArchiveAccountHandler>.Instance);

    public ListRiskProfilesHandler ListProfiles() => new(Data);

    public CreateRiskProfileHandler CreateProfile() => new(Data, Data, Data, Clock, NullLogger<CreateRiskProfileHandler>.Instance);

    public UpdateRiskProfileHandler UpdateProfile() => new(Data, Data, Data, Clock, NullLogger<UpdateRiskProfileHandler>.Instance);

    public DeleteRiskProfileHandler DeleteProfile() => new(Data, Data, Data, Clock, NullLogger<DeleteRiskProfileHandler>.Instance);

    public ListBucketsHandler ListBuckets() => new(Data, Data);

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
