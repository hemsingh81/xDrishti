using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Api.Endpoints;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Application.Auth;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Calendar;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Application.Portfolio.Groups;

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
[JsonSerializable(typeof(TrackingInput))]
[JsonSerializable(typeof(TrackResult))]
[JsonSerializable(typeof(UntrackResult))]
[JsonSerializable(typeof(ResolveInput))]
[JsonSerializable(typeof(ResolveResult))]
[JsonSerializable(typeof(BasketView))]
[JsonSerializable(typeof(IReadOnlyList<BasketView>))]
[JsonSerializable(typeof(BasketInput))]
[JsonSerializable(typeof(BasketOrderInput))]
[JsonSerializable(typeof(BasketMembers))]
[JsonSerializable(typeof(AddMembersInput))]
[JsonSerializable(typeof(AddMembersResult))]
[JsonSerializable(typeof(RemoveMembersInput))]
[JsonSerializable(typeof(RemoveMembersResult))]
[JsonSerializable(typeof(MembersOrderInput))]
[JsonSerializable(typeof(TransferInput))]
[JsonSerializable(typeof(TransferResult))]
[JsonSerializable(typeof(BasketHistory))]
[JsonSerializable(typeof(IReadOnlyList<InstrumentBasketView>))]
[JsonSerializable(typeof(StartRefreshRequest))]
[JsonSerializable(typeof(RefreshStarted))]
[JsonSerializable(typeof(RefreshStatusView))]
[JsonSerializable(typeof(IReadOnlyList<RefreshRunView>))]
[JsonSerializable(typeof(StartDataJobRequest))]
[JsonSerializable(typeof(DataJobView))]
[JsonSerializable(typeof(IReadOnlyList<DataJobView>))]
[JsonSerializable(typeof(DataOverview))]
[JsonSerializable(typeof(CsvFilePage))]
[JsonSerializable(typeof(PreviewCsvFilesRequest))]
[JsonSerializable(typeof(CsvPreviewResult))]
[JsonSerializable(typeof(DataInstrumentPage))]
[JsonSerializable(typeof(GapPage))]
[JsonSerializable(typeof(DataNotices))]
[JsonSerializable(typeof(InstrumentBars))]
[JsonSerializable(typeof(StartQualityRunRequest))]
[JsonSerializable(typeof(QualityOverview))]
[JsonSerializable(typeof(QualityInstrumentPage))]
[JsonSerializable(typeof(QualityInstrumentDetail))]
[JsonSerializable(typeof(QualityGateView))]
[JsonSerializable(typeof(IReadOnlyList<QualityRunView>))]
[JsonSerializable(typeof(CalendarView))]
[JsonSerializable(typeof(CalendarDayView))]
[JsonSerializable(typeof(AddCalendarDayRequest))]
[JsonSerializable(typeof(CalendarRefreshResult))]
[JsonSerializable(typeof(CorporateActionPage))]
[JsonSerializable(typeof(CorporateActionView))]
[JsonSerializable(typeof(AddCorporateActionRequest))]
[JsonSerializable(typeof(UpdateCorporateActionRequest))]
[JsonSerializable(typeof(DetectionResult))]
[JsonSerializable(typeof(ImportCorporateActionsResult))]
[JsonSerializable(typeof(HoldingPage))]
[JsonSerializable(typeof(HoldingsSummary))]
[JsonSerializable(typeof(HoldingGroups))]
[JsonSerializable(typeof(HoldingGroup))]
[JsonSerializable(typeof(HoldingGroupTotal))]
[JsonSerializable(typeof(GroupPart))]
[JsonSerializable(typeof(PricesAsOf))]
[JsonSerializable(typeof(HoldingLots))]
[JsonSerializable(typeof(LotRow))]
[JsonSerializable(typeof(LotDateInput))]
[JsonSerializable(typeof(ImportPreview))]
[JsonSerializable(typeof(SyncInput))]
[JsonSerializable(typeof(SyncRunView))]
[JsonSerializable(typeof(SyncStatusView))]
[JsonSerializable(typeof(PortfolioHistory))]
[JsonSerializable(typeof(BenchmarkInput))]
[JsonSerializable(typeof(BenchmarkView))]
[JsonSerializable(typeof(PortfolioTrackResult))]
[JsonSerializable(typeof(ActionList))]
[JsonSerializable(typeof(ActionItem))]
[JsonSerializable(typeof(ActionNumbers))]
[JsonSerializable(typeof(LongTermLot))]
[JsonSerializable(typeof(ActionCounts))]
[JsonSerializable(typeof(ActionUnavailable))]
[JsonSerializable(typeof(ActionDecisionView))]
[JsonSerializable(typeof(ActionThresholdsView))]
[JsonSerializable(typeof(ActionThresholdsInput))]
[JsonSerializable(typeof(PriceAlertInput))]
[JsonSerializable(typeof(PriceAlertView))]
[JsonSerializable(typeof(PriceAlertPage))]
[JsonSerializable(typeof(PortfolioNotices))]
[JsonSerializable(typeof(ServiceList))]
[JsonSerializable(typeof(ServiceView))]
[JsonSerializable(typeof(ServiceRunView))]
[JsonSerializable(typeof(ServiceRunPage))]
[JsonSerializable(typeof(ServicesTimeline))]
[JsonSerializable(typeof(ServiceNotices))]
[JsonSerializable(typeof(ServiceScheduleInput))]
[JsonSerializable(typeof(SchedulePreview))]
[JsonSerializable(typeof(ProblemIssue[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
