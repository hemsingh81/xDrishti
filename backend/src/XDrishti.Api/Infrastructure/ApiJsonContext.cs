using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Auth;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Portfolio;

namespace XDrishti.Api.Infrastructure;

/// <summary>
/// Source-generated JSON metadata: no runtime reflection for serialisation (faster, less memory).
/// Add every response/request type returned by an endpoint here.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(SystemStatusResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(StepUpRequest))]
[JsonSerializable(typeof(StepUpResult))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(IReadOnlyList<AuditEventResponse>))]
[JsonSerializable(typeof(IReadOnlyList<AccountSummary>))]
[JsonSerializable(typeof(AccountDetail))]
[JsonSerializable(typeof(CreateAccountInput))]
[JsonSerializable(typeof(ConnectionInput))]
[JsonSerializable(typeof(UpdateSecretsInput))]
[JsonSerializable(typeof(RolesInput))]
[JsonSerializable(typeof(TradingInput))]
[JsonSerializable(typeof(PortfolioInput))]
[JsonSerializable(typeof(PortfolioSaved))]
[JsonSerializable(typeof(DataInput))]
[JsonSerializable(typeof(ConnectStartView))]
[JsonSerializable(typeof(DhanRedirectView))]
[JsonSerializable(typeof(IReadOnlyList<AccountEventView>))]
[JsonSerializable(typeof(IReadOnlyList<RiskProfileView>))]
[JsonSerializable(typeof(RiskProfileView))]
[JsonSerializable(typeof(RiskProfileInput))]
[JsonSerializable(typeof(IReadOnlyList<BucketView>))]
[JsonSerializable(typeof(BucketView))]
[JsonSerializable(typeof(BucketInput))]
[JsonSerializable(typeof(InstrumentPage))]
[JsonSerializable(typeof(InstrumentFacets))]
[JsonSerializable(typeof(InstrumentDetail))]
[JsonSerializable(typeof(StartRefreshRequest))]
[JsonSerializable(typeof(RefreshStarted))]
[JsonSerializable(typeof(RefreshStatusView))]
[JsonSerializable(typeof(IReadOnlyList<RefreshRunView>))]
[JsonSerializable(typeof(HoldingPage))]
[JsonSerializable(typeof(HoldingsSummary))]
[JsonSerializable(typeof(HoldingLots))]
[JsonSerializable(typeof(LotRow))]
[JsonSerializable(typeof(LotDateInput))]
[JsonSerializable(typeof(ImportPreview))]
[JsonSerializable(typeof(SyncInput))]
[JsonSerializable(typeof(SyncRunView))]
[JsonSerializable(typeof(SyncStatusView))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
