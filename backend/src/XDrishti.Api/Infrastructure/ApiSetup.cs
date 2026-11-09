using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using XDrishti.Api.Auth;
using XDrishti.Api.Confidence;
using XDrishti.Api.Endpoints;
using XDrishti.Api.Lab;
using XDrishti.Api.Live;
using XDrishti.Api.Market;
using XDrishti.Api.Platform;
using XDrishti.Api.Portfolio;
using XDrishti.Application;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Application.Live.Push;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Api.Infrastructure;

internal static class ApiSetup
{
    public const string OpenApiRoute = "/api/openapi/{documentName}.json";

    /// <summary>Failed and successful logins together: more than this per minute from one address are refused with 429.</summary>
    public const int LoginAttemptsPerMinute = 10;

    /// <summary>The most owner decisions on a plan (approve, modify, reject, approve-all, mark seen) per minute and client (P7-10): far above a person, far below a script.</summary>
    public const int PlanDecisionsPerMinute = 120;

    /// <summary>Chat questions per minute per session (spec P9-03).</summary>
    public const int AssistantChatsPerMinute = 10;

    /// <summary>Taps and skips of confirm tickets per minute and client (spec P12-08): the routes that check a password only when the ticket asks for it have their own limit; the day unlock, which always checks one, has the sign-in limit.</summary>
    public const int ConfirmActsPerMinute = 60;

    private static readonly string[] TrustedProxyNetworks = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "xDrishti API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            o.AddSchemaTransformer(ProblemDetailsSchema.AddExtensions);
            o.AddDocumentTransformer(HubContractDocument.AddAsync);
            o.AddDocumentTransformer(AttentionCommandsContractDocument.AddAsync);
        });
        services.ConfigureHttpJsonOptions(o =>
        {
            // Numbers are numbers on the wire (no "123" strings): keeps the contract and generated types exact.
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        services.AddAuth();

        // Endpoint modules are registered explicitly — one line per feature module.
        services.AddSingleton<IEndpointModule, SystemEndpoints>();
        services.AddSingleton<IEndpointModule, SettingsEndpoints>(); // the settings catalog (AU-08, ADR 0242)
        services.AddSingleton<Application.Abstractions.SettingsCatalog.ISettingsCatalogSource, Settings.SettingsCatalogSource>();
        services.AddSettingsCatalogHandler(); // registered with its source, in the Api only
        services.AddSingleton<IEndpointModule, AuthEndpoints>();
        services.AddSingleton<IEndpointModule, AccountsEndpoints>();
        services.AddSingleton<IEndpointModule, BrokersEndpoints>();
        services.AddSingleton<IEndpointModule, RiskProfilesEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioBucketsEndpoints>();
        services.AddSingleton<IEndpointModule, InstrumentsEndpoints>();
        services.AddSingleton<IEndpointModule, BasketsEndpoints>();
        services.AddSingleton<IEndpointModule, HoldingsEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioHistoryEndpoints>();
        services.AddSingleton<IEndpointModule, DataEndpoints>();
        services.AddSingleton<IEndpointModule, DataQualityEndpoints>();
        services.AddSingleton<IEndpointModule, DataCsvEndpoints>();
        services.AddSingleton<IEndpointModule, CorporateActionEndpoints>();
        services.AddSingleton<IEndpointModule, ServicesEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioActionsEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioAlertsEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioTradesEndpoints>();
        services.AddSingleton<IEndpointModule, BrokerHistoryEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioDividendsEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioDividendImportEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioImportsEndpoints>();
        services.AddSingleton<IEndpointModule, LiveEndpoints>();
        services.AddSingleton<IEndpointModule, LabEndpoints>();
        services.AddSingleton<IEndpointModule, LabRunsEndpoints>();
        services.AddSingleton<IEndpointModule, LabConfidenceEndpoints>();
        services.AddSingleton<IEndpointModule, LearningOverviewEndpoints>();
        services.AddSingleton<IEndpointModule, LearningEvidenceEndpoints>();
        services.AddSingleton<IEndpointModule, DigestEndpoints>();
        services.AddSingleton<IEndpointModule, JourneyEndpoints>();
        services.AddSingleton<IEndpointModule, CellsEndpoints>();
        services.AddSingleton<IEndpointModule, ModelsEndpoints>();
        services.AddSingleton<IEndpointModule, ExperimentsEndpoints>();
        services.AddSingleton<IEndpointModule, ExperimentJobsEndpoints>(); // the replay job started from the Learning area (P8-18 Part B, ADR 0129): capability off by default, fresh password, never applies
        services.AddSingleton<IEndpointModule, OwnerMovesEndpoints>(); // the lesson, hypothesis and revert moves of the owner-acts forms (P8-19, ADR 0130): capability off by default, fresh password, writes one row
        services.AddSingleton<IEndpointModule, ExportsEndpoints>(); // saving a research table as a file (P8-20, ADR 0131): capability off by default, audited before the file is given, nothing kept
        services.AddSingleton<Exports.ExportSlot>();
        services.AddOptions<Exports.ExportOptions>().BindConfiguration("Exports").Validate(Exports.ExportOptions.IsValid, "Exports:MaxCsvRows and Exports:MaxCsvBytes may only lower the caps (1 up to 100000 rows and 26214400 bytes).").ValidateOnStart();
        services.AddScoped<ICommandHandler<Exports.ExportCommand, Exports.ExportFile>, Exports.ExportHandler>();
        services.AddSingleton<IEndpointModule, ChangelogEndpoints>();
        services.AddSingleton<IEndpointModule, AuditEndpoints>();
        services.AddSingleton<IEndpointModule, HypothesesEndpoints>();
        services.AddSingleton<IEndpointModule, LessonsEndpoints>();
        services.AddSingleton<IEndpointModule, PlansEndpoints>();
        services.AddSingleton<IEndpointModule, PaperEndpoints>();
        services.AddSingleton<IEndpointModule, PaperGovernanceEndpoints>();
        services.AddSingleton<IEndpointModule, PaperReviewEndpoints>();
        services.AddSingleton<IEndpointModule, PaperCompareEndpoints>(); // backtest versus paper (P10-04): one GET, computed on read
        services.AddSingleton<IEndpointModule, ConfidenceEndpoints>(); // the decay monitor of a strategy and "is the learning loop helping?" (CF-12 part 12a): read only, nothing is proposed or changed
        services.AddSingleton<IEndpointModule, ReadinessEndpoints>(); // the readiness scorecard per strategy and for the system, and the owner's enforce switch (CF-11 part 1): derived reads, the switch is the owner's act, no order
        services.AddSingleton<IEndpointModule, KillEndpoints>(); // the kill proposals, applying one, the owner's automatic setting and the re-enable of a paused cell (CF-12 part 12b): the owner's acts, no order
        services.AddSingleton<IEndpointModule, MlSafetyEndpoints>(); // the rollback proposals, applying one, the owner's three ML switches and the promotion of a model (CF-10 part 10d-1): the owner's acts, no order
        services.AddSingleton<IEndpointModule, PaperSequentialEndpoints>(); // the sequential failure test of the paper edge series (CF-09): a flag, never a pass, read only
        services.AddSingleton<IEndpointModule, PaperShadowEndpoints>(); // the shadow record summary and the owner's switch (CF-08 part B): fresh password per change, insert-only
        services.AddSingleton<IEndpointModule, PaperPhaseCheckEndpoints>(); // the paper phase check (P10-06): one GET, computed on read
        services.AddSingleton<IEndpointModule, TicketAlertsEndpoints>(); // price-crossing alerts and prompts on the plan screen (P7-20); preferences only, no order
        services.AddSingleton<IEndpointModule, ReportsEndpoints>();
        services.AddSingleton<IEndpointModule, ReportsPhaseCheckEndpoints>(); // the P8 phase check (P8-17): one GET, computed on read
        services.AddSingleton<IEndpointCatalogue, EndpointCatalogue>(); // the route table for the phase check's read-only criterion
        services.AddSingleton<IEndpointModule, AutonomyEndpoints>();
        services.AddSingleton<IEndpointModule, ExecutionConfirmEndpoints>(); // the confirm tickets and day unlocks (P12-08b): the owner's tap writes an outcome row and sends nothing
        services.AddSingleton<IEndpointModule, ExecutionLimitsEndpoints>(); // the per-account limits sheet (P12-09): a raise needs the password and waits a full session; sends nothing
        services.AddSingleton<IEndpointModule, ExecutionSwitchEndpoints>(); // the owner's switch writes and the kill-switch drill (ADR 0174): password every time, auto_live stays OFF; sends nothing
        services.AddSingleton<IEndpointModule, ExecutionEndpoints>(); // the execution service's status (P12-01b): read only, no write route
        services.AddSingleton<IEndpointModule, DepthEndpoints>(); // the owner's depth-recorder switch (P7-16): fresh password per change, insert-only; no depth data is readable
        services.AddSingleton<IEndpointModule, OptionChainEndpoints>(); // the owner's option-chain recorder switch (P14-02): fresh password per change, insert-only; no chain data is readable
        services.AddSingleton<IEndpointModule, OptionBackfillEndpoints>(); // the owner's expired-options backfill switch and probe acceptance (P14-03): fresh password per change, insert-only; no bar is readable
        services.AddSingleton<IEndpointModule, OptionCoverageEndpoints>(); // the stored quality verdicts of the option archive (P14-04): one GET, aggregates only, no price, IV or Greek
        services.AddSingleton<IEndpointModule, OptionReportEndpoints>(); // the read-only P14 reports (P14-16): GETs only, counts, shares, dates and verdict words
        services.AddSingleton<IEndpointModule, FilingEndpoints>(); // the stored filings as metadata and counts, optionally as of an instant (P15-02): GETs only, no file content
        services.AddSingleton<IEndpointModule, ExtensionEvaluationEndpoints>(); // the stored verdicts of protocols F, X, P and L (P15-10): GETs only; a KEEP is a record
        services.AddSingleton<IEndpointModule, EventFilterEndpoints>(); // the owner's event-risk switch (P15-06): password every time, recorded as owner; the auto option is inert until a protocol-F verdict exists; places no order
        services.AddSingleton<IEndpointModule, CalendarEndpoints>(); // the results meetings known at an instant and the instruments without an entry (P15-04): one GET, no file content, no estimate
        services.AddSingleton<IEndpointModule, TickEndpoints>(); // the owner's trade-tick recorder switch (P7-16b): fresh password per change, insert-only; no tick data is readable
        services.AddSingleton<IEndpointModule, LiveTradingEndpoints>(); // the compliance record of the small live phase (P11-05): fresh password, gate Live:Writes:Enabled (OFF), insert-only; no broker call
        services.AddSingleton<IEndpointModule, ChargeSheetEndpoints>(); // the owner's confirmation of the charge sheet (P8-23, ADR 0127)
        services.AddSingleton<SwitchOnLimiter>();
        services.AddSingleton<IEndpointModule, CapabilitiesEndpoints>(); // the owner's capability switches of the owner-act slices (P8-18 Part A, ADR 0128)
        services.AddSingleton<IEndpointModule, AssistantProposalEndpoints>(); // the assistant's proposals: the owner reads them, accepts with a fresh password or rejects (P9-08)
        services.AddSingleton<IEndpointModule, AssistantEvalEndpoints>(); // the eval results, read only: no endpoint runs, grades or accepts an eval (P9-09)
        services.AddSingleton<IEndpointModule, AssistantDraftEndpoints>(); // strategy drafting: the owner types an idea, reads the draft, confirms with a fresh password (P9-07)
        services.AddSingleton<IEndpointModule, AssistantBriefingEndpoints>(); // the stored briefing and weekly review texts, read only (P9-06)
        services.AddSingleton<IEndpointModule, AssistantEndpoints>(); // the local model server's state, read only (P9-01)
        services.AddSingleton<IEndpointModule, PlanReviewEndpoints>(); // the owner's approve / modify / reject / approve-all of a plan (P7-10); session + CSRF, manual approval, no order

        // Instrument master (ADR 0010): the worker that carries out registered refreshes.
        services.AddHostedService<RefreshJobWorker>();

        // Market data (ADR 0016): the worker that carries out registered data jobs (CSV load, Dhan fetch, aggregates).
        services.AddHostedService<DataJobWorker>();

        // The services framework (ADR 0018): the one scheduler of the automatic jobs (end-of-day data run, instrument master refresh); their times are edited on the Services screen.
        services.AddHostedService<ServiceSchedulerHost>();

        // Holdings (ADR 0012): the automatic refresh of the accounts that switched it on.
        services.AddHostedService<PortfolioRefreshService>();

        // Trades and ledger read from Dhan (ADR 0025): the worker that carries out the runs "Sync now" registered; the nightly read is a service of the scheduler above.
        services.AddHostedService<BrokerHistoryWorker>();

        // The rules compiler is warmed up on a thread-pool thread when the host starts, so that the first compile of a person does not pay for the JIT (ISS-12, ADR 0034 amendment 2). Readiness is not delayed.
        services.AddHostedService(sp => new StrategyWarmupService(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<StrategyWarmupService>>(), CompilerWarmup.Run));

        services.AddLabRuns();
        services.AddConfidence();
        services.AddExperimentJobs();
        services.AddLiveHub();
        return services;
    }

    /// <summary>
    /// The <c>Confidence</c> section (CF-01, ADR 0203) as the options of effective n, power and minimum track record; a value out of range stops the host at start. The default mode is ReportOnly: shown, nothing decided.
    /// </summary>
    private static void AddConfidence(this IServiceCollection services)
    {
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ConfidenceSettings>, Settings.ConfidenceRangeValidator>(); // the descriptor bounds, checked at start (AU-08)
        services.AddOptions<ConfidenceSettings>()
            .BindConfiguration(ConfidenceSettings.SectionName)
            .Validate(s => s.ToOptions().Problems().Count == 0, "Confidence: a value is out of range (EffectiveN:MinSessionsForAutocorr 5..250, MinTradesForEstimate 3..50, MinClusters 3..50, Power:Alpha 0.001..0.2, Power 0.5..0.99, SigmaFloorR 0.1..5, TargetEdgeR 0.01..5, MinTrl:Confidence 0.5..0.999, BenchmarkSr -1..1).")
            .Validate(s => s.ToShrinkageOptions().Problems().Count == 0, "Confidence:Shrinkage: a value is out of range or the mode is not offered (Mode Off or ReportOnly, TauSquaredFloor 0.0001..1, MinTradesForSibling 3..30, MinParentTrades 10..200, StrengthMin and StrengthMax 1..1000 with Min not above Max, QuantileLevel 0.01..0.25, SigmaPoolStrength 1..1000).")
            .Validate(s => s.ToDeflationOptions().Problems().Count == 0, "Confidence:Deflation: a value is out of range or the mode is not offered (Mode Off or ReportOnly, MinProbability 0.5..0.999, AssumedTrialSrSd 0.001..1, TBasis NEff or Raw).")
            .Validate(s => s.ToBootstrapOptions().Problems().Count == 0, "Confidence:Bootstrap: a value is out of range (Mode Off or ReportOnly, Draws 100..20000, BlockLength 0 or 2..60).")
            .Validate(s => s.ToPboOptions().Problems().Count == 0, "Confidence:Pbo: a value is out of range (Mode Off, ReportOnly or Gate, Blocks an even number 4..16, MinVariants 6..100, MaxPbo 0.1..0.9, Metric Mean or Sharpe).")
            .Validate(s => s.ToHoldOutOptions().Problems().Count == 0, "Confidence:HoldOut: a value is out of range (MinMonths 1..60, SharePercent 5..50, MinTrades 5..1000, Retention 0.1..1).")
            .Validate(s => s.ToCostStressOptions().Problems().Count == 0, "Confidence:CostStress: a value is out of range (Mode Off or ReportOnly, MidMultiplier above 1 up to 5, HighMultiplier above MidMultiplier up to 5, MidFloorR and HighFloorR -1..1, DelayMinutes 1..5, DelayRetention 0.1..1, TierACutCrore 100..100000, TierBCutCrore at least 20 and below TierACutCrore, CapacityFillShare above 0 up to 0.01, CapacityDailyShare above 0 up to 0.005: the tier and capacity values can only be tightened; TierABps 3..5, TierBBps 8..15, RangeFraction 0.10..0.20 and MinTurnoverCrore 20..50 (judgment calls, ADR 0230: raise only); FillMinuteMaxTrades 0..20000 and FillMinuteTimeoutSeconds 1..600 bound the cost of the fill-minute read).")
            .Validate(s => s.ToNullOptions().Problems().Count == 0, "Confidence:Nulls: a value is out of range (Mode Off or ReportOnly and not Enforce with Mode Off, Draws 200..MaxDraws, MaxDraws 1000..20000, Percentile 90..99, Redraws 0..20, MinPlacebos 19..20).")
            .Validate(s => s.ToPlateauOptions().Problems().Count == 0, "Confidence:Plateau: a value is out of range (Mode Off or ReportOnly and not Enforce with Mode Off, Retention 0.50..0.95, MinPoints 3..5).")
            .Validate(s => !s.PlateauEnforcedWithoutNulls(), "Confidence:Plateau:Enforce needs Confidence:Nulls:Mode ReportOnly (the plateau gate is computed inside the nulls stage; with Nulls:Mode Off nothing is computed to enforce).")
            .Validate(s => s.ToSequentialOptions().Problems().Count == 0, "Confidence:Sequential: a value is out of range (Mu1 above 0 up to 2, Sigma 0.5..5, Alpha above 0 up to 0.2, BetaCaution above 0 up to 0.5 with (1 - BetaCaution) / Alpha above 1, BetaFail above 0 and below BetaCaution, SigmaFromN 5..200).")
            .Validate(s => s.ToCalibrationOptions().Problems().Count == 0, "Confidence:Calibration: a value is out of range (IsotonicMinRows 1..100000).")
            .Validate(s => s.ToLadderOptions().Problems().Count == 0, "Confidence:Ladder: a value is out of range (Mode Off or ReportOnly, Epv 5..50, MaxLogisticFeatures 1..50, MinTreeEvents 10..100000, MinProbability 0.50..0.999, PsiWatch above 0 and below PsiAlarm up to 5, PsiBins 2..100, MinRows 5..100000, MaxFlipShare above 0 and below 0.5).")
            .Validate(s => s.ToPairedBootstrapOptions().Problems().Count == 0, "Confidence:PairedBootstrap: a value is out of range (Mode Off or ReportOnly, Draws 100..20000, Level above 0.50 and below 1, MinDays 2..1000, PromisingFrom 0.50..0.99).")
            .Validate(s => s.ToMlInfluenceOptions().Problems().Count == 0, "Confidence:MlInfluence: a value is out of range or looser than the spec's proposal (HoldOutMinRows 100..100000, HoldOutMinProbability 0.95..0.999, ForwardMinCandidates 30..100000, ForwardMinWeeks 4..520, ForwardMaxEcePp above 0 up to 5; configuration can only tighten them).")
            .Validate(s => s.ToDecayOptions().Problems().Count == 0, "Confidence:Decay: a value is out of range or looser than the spec's proposal (WindowTrades 20..60, Sigmas above 0 up to 1.5, QuarterFloorR -0.05..0; configuration can only tighten them).")
            .Validate(s => s.ToLoopOptions().Problems().Count == 0, "Confidence:Loop: a value is out of range or looser than the spec's proposal (MinDays 20..250 (a trade-off, not a tightening), Draws 2000..20000, Level 0.90..0.99, HelpsAt 0.95..0.999, MinSlopePer50Rows above 0 up to 0.1, TargetGain 0.001..0.1, SlopeLow 0.8 up to but not including 1, SlopeHigh above 1 up to 1.2, SlopeSe 1..2; the reliability window is fixed at 200 and the hurts bar at 0.95; configuration can only tighten the rest).")
            .Validate(s => s.ToKillOptions().Problems().Count == 0, "Confidence:Kill: a value is out of range or looser than the spec's proposal (StressMultiplier 1.5..3, it can only be raised; DrawdownFactor 1..2, it can only be lowered). The trailing 100 trades and the 20 new trades between firings are fixed, and the automatic setting confidence.kill.auto is stored state changed only by the owner, not a configuration value.")
            .Validate(s => s.ToReadinessOptions().Problems().Count == 0, "Confidence:Readiness: a value is out of range or looser than the spec's proposal (MinNEff 300..100000, MinTradesPerCell 30..10000, ExpectancyR 0.15..2, TStatistic 2..10, MinQuarterShare 0.70..1, each of these can only be raised; MaxYearShare 0.20..0.60, MaxInstrumentShare 0.02..0.15, DrawdownBudgetR 1..20, each of these can only be lowered; BuildingFrom 40..99, UsableFrom 70..99.5, ReadyFrom 85..100, each can only be raised and they must increase). The enforce switch confidence.readiness.enforce is stored state changed only by the owner, not a configuration value.")
            .Validate(s => s.MlInfluence.HoldOutMinProbability >= s.Ladder.MinProbability, "Confidence:MlInfluence:HoldOutMinProbability must not be below Confidence:Ladder:MinProbability (the hold-out bar is never lower than the ladder's pass bar).")
            .Validate(s => new Domain.Learning.ValidationOptions(Ladder: s.ToLadderOptions(), Paired: s.ToPairedBootstrapOptions()).Problems().Count == 0, "Confidence:PairedBootstrap:PromisingFrom must not be above Confidence:Ladder:MinProbability (a probability between them reads promising, never pass).")
            .ValidateOnStart();
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToShrinkageOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToDeflationOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToBootstrapOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToPboOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToCostStressOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToNullOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToPlateauOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToSequentialOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToCalibrationOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToLadderOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToPairedBootstrapOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToMlInfluenceOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToDecayOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToKillOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToLoopOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToReadinessOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToRegistrationOptions()));
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ConfidenceSettings>>().Value.ToHoldOutOptions()));
    }

    /// <summary>
    /// Lab runs (P5-02, ADR 0058): the executor and dispatcher of the Application layer, run by a hosted service of this host (the export library that renders a run's record may only be named here), the hub as the progress port, and the settings
    /// of doc 16 <c>strategy_lab</c> from the configuration.
    /// </summary>
    private static void AddLabRuns(this IServiceCollection services)
    {
        services.AddLabRunner();
        services.Replace(ServiceDescriptor.Singleton(sp => LabSettingsBinder.Bind(sp.GetRequiredService<IConfiguration>())));
        services.AddSingleton<IRunRecordWriter, ExportRunRecordWriter>();
        services.Replace(ServiceDescriptor.Singleton<ILabProgress, HubLabProgress>());
        services.AddHostedService<LabRunService>();
    }

    /// <summary>
    /// Replay jobs (P8-18 Part B, ADR 0129): the executor and dispatcher of the Application layer, run by a hosted service of this host, the hub as the progress port, and the <c>Experiments</c> section as settings (a value out of range stops the host at start).
    /// </summary>
    private static void AddExperimentJobs(this IServiceCollection services)
    {
        services.AddOptions<ExperimentJobSettings>().BindConfiguration(ExperimentJobSettings.Section).ValidateDataAnnotations().ValidateOnStart();
        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<ExperimentJobSettings>>().Value));
        services.AddExperimentJobRunner();
        services.Replace(ServiceDescriptor.Singleton<IExperimentJobProgress, HubExperimentProgress>());
        services.AddHostedService<ExperimentJobService>();
    }

    /// <summary>
    /// The SignalR hub <c>/hubs/events</c> (ADR 0024 §7, P2-10a): the push coordinator of the Application layer, the way out to the connections, and the hosted loop that runs the listener and the pushes. The hub is not mapped to the
    /// feed: <c>xd-api</c> never references it and runs the same without it. Messages use the API's source-generated JSON (camelCase, numbers as numbers).
    /// </summary>
    private static void AddLiveHub(this IServiceCollection services)
    {
        services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 64 * 1024; // a Subscribe with 1000 holding ids is about 40 KB
            o.EnableDetailedErrors = false;
        }).AddJsonProtocol(o =>
        {
            o.PayloadSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });
        services.AddSingleton<SignalRLiveClients>();
        services.AddSingleton<ILiveClients>(sp => sp.GetRequiredService<SignalRLiveClients>());
        services.AddLivePush();
        services.AddHostedService<LivePushService>();
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging(o =>
        {
            o.GetLevel = RequestLogLevel.ForPath;
            o.Logger = app.Services.GetRequiredService<Serilog.ILogger>(); // the host's logger (HostDefaults keeps the static one out)
        });

        app.UseMiddleware<HubOriginMiddleware>(); // before the session is looked at: another origin gets its 403 without touching it
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<StepUpMiddleware>();

        // Liveness: process is up. Readiness: dependencies (database) reachable. Public: container probes use them.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        // The contract and the docs UI need a session like everything else (stated explicitly; the fallback policy would also cover them).
        app.MapOpenApi(OpenApiRoute).RequireAuthorization();
        // Local-only docs UI: Scalar's cloud features (AI agent, MCP, telemetry, client button) are disabled.
        app.MapScalarApiReference("/api/docs", o => o
            .WithTitle("xDrishti API")
            .WithOpenApiRoutePattern(OpenApiRoute)
            .DisableAgent()
            .DisableMcp()
            .DisableTelemetry()
            .HideClientButton())
            .RequireAuthorization();

        // The hub: owner session (the fallback policy, stated again by [Authorize] on the hub), WebSockets only.
        app.MapHub<EventsHub>(LiveHubContract.Route, o => o.Transports = HttpTransportType.WebSockets).RequireAuthorization();

        var api = app.MapGroup("/api");
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.Map(api);
        }

        return app;
    }

    private static void AddAuth(this IServiceCollection services)
    {
        services.AddAuthentication(SessionAuthentication.Scheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthentication.Scheme, _ => { });

        // Closed by default: any endpoint without its own policy needs a signed-in session.
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // The proxy (Caddy, container network) is trusted to tell the real client address and scheme.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var network in TrustedProxyNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(SessionAuthentication.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(SessionAuthentication.PlanDecisionRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PlanDecisionsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(ExecutionConfirmEndpoints.ConfirmRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ConfirmActsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // The limiter runs before the session is looked at, so a cookie cannot be trusted as a key: a rotating fake cookie would grow the table and dodge the limit. One owner, one address (the proxy's, with the real one in X-Forwarded-For): the key is the address.
            o.AddPolicy(AssistantEndpoints.ChatRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = AssistantChatsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (context, _) =>
            {
                int? retryAfter = null;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window))
                {
                    retryAfter = (int)Math.Ceiling(window.TotalSeconds);
                    context.HttpContext.Response.Headers.RetryAfter = retryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await ProblemResults.WriteAsync(context.HttpContext, ProblemResults.Create(
                    StatusCodes.Status429TooManyRequests, "Too many attempts. Try again later.", SessionAuthentication.RateLimitedCode, retryAfter));
            };
        });
    }
}
