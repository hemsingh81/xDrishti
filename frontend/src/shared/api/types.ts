import type { components } from './schema';

/** Friendly aliases for API schemas. Import from here, not from the generated file. */
export type Schemas = components['schemas'];
export type SystemStatus = Schemas['SystemStatusResponse'];
export type ServiceStatus = Schemas['ServiceStatus'];
export type Problem = Schemas['ProblemDetails'];
/** A problem that may carry per-field messages (`errors`), e.g. `accounts.invalid`. */
export type ValidationProblem = Schemas['HttpValidationProblemDetails'];
export type SessionInfo = Schemas['SessionInfo'];
export type UserInfo = Schemas['UserInfo'];
export type StepUpResult = Schemas['StepUpResult'];

export type AccountSummary = Schemas['AccountSummary'];
export type AccountDetail = Schemas['AccountDetail'];
export type AccountEvent = Schemas['AccountEventView'];
export type AccountOverrides = Schemas['AccountOverrides'];
export type CreateAccountInput = Schemas['CreateAccountInput'];
export type UpdateSecretsInput = Schemas['UpdateSecretsInput'];
export type SecretStatus = Schemas['SecretStatusView'];
export type ConnectionInput = Schemas['ConnectionInput'];
/** The account's link to its broker (status, expiry, holder) — never the token itself. */
export type BrokerConnection = Schemas['ConnectionView'];
export type ConnectStart = Schemas['ConnectStartView'];
export type RolesInput = Schemas['RolesInput'];
export type TradingInput = Schemas['TradingInput'];
export type PortfolioInput = Schemas['PortfolioInput'];
export type TradingSettings = Schemas['TradingSettingsView'];
export type RiskProfile = Schemas['RiskProfileView'];
export type RiskProfileInput = Schemas['RiskProfileInput'];
export type PortfolioBucket = Schemas['BucketView'];

export type InstrumentSummary = Schemas['InstrumentSummary'];
export type InstrumentDetail = Schemas['InstrumentDetail'];
export type InstrumentPage = Schemas['InstrumentPage'];
export type InstrumentFacets = Schemas['InstrumentFacets'];
export type FacetCount = Schemas['FacetCount'];
export type TrackedInfo = Schemas['TrackedInfo'];
export type TrackResult = Schemas['TrackResult'];
export type UntrackResult = Schemas['UntrackResult'];
export type ResolveResult = Schemas['ResolveResult'];
export type ResolvedInstrument = Schemas['ResolvedInstrument'];
export type UnmatchedEntry = Schemas['UnmatchedEntry'];
export type BrokerId = Schemas['BrokerIdView'];
/** One run of the master-list refresh (manual, daily or forced). */
export type RefreshRun = Schemas['RefreshRunView'];
export type RefreshStatus = Schemas['RefreshStatusView'];
export type AutoRefresh = Schemas['AutoRefreshView'];

/** Profit/loss figures of a holding, a lot or a group: invested, value, P&L (₹ and %), XIRR and its state, realised P&L. */
export type Returns = Schemas['ReturnsView'];
export type HoldingRow = Schemas['HoldingRow'];
export type HoldingPage = Schemas['HoldingPage'];
export type HoldingsSummary = Schemas['HoldingsSummary'];
/** The holdings of a scope grouped by account, bucket, sector or basket, with the scope total (P2-08, ADR 0021). */
export type HoldingGroups = Schemas['HoldingGroups'];
export type HoldingGroup = Schemas['HoldingGroup'];
export type HoldingGroupTotal = Schemas['HoldingGroupTotal'];
/** One part of a group's value in the second dimension (the sector of an account, the account of a sector). */
export type GroupPart = Schemas['GroupPart'];
export type HoldingLots = Schemas['HoldingLots'];
/** The value history of a scope with its benchmark and coverage (P2-07, ADR 0020). */
export type PortfolioHistory = Schemas['PortfolioHistory'];
export type HistoryPoint = Schemas['HistoryPoint'];
export type HistoryBenchmark = Schemas['HistoryBenchmark'];
export type HistoryCoverage = Schemas['HistoryCoverage'];
/** The largest fall of a unit-value series: `pct` is a positive number (a fall of pct %), with the peak and low dates. */
export type Drawdown = Schemas['Drawdown'];
/** The benchmark index in force. */
export type PortfolioBenchmark = Schemas['BenchmarkView'];
export type PortfolioTrackResult = Schemas['PortfolioTrackResult'];
export type LotRow = Schemas['LotRow'];
/** One run of the holdings sync (manual, automatic or the import). */
export type SyncRun = Schemas['SyncRunView'];
/** The auto refresh of an account and its latest run. */
export type SyncStatus = Schemas['SyncStatusView'];
export type ImportPreview = Schemas['ImportPreview'];
export type ImportPreviewRow = Schemas['PreviewRow'];
/** The answer of saving the portfolio settings: the account and what the first import did (null when none was asked for). */
export type PortfolioSaved = Schemas['PortfolioSaved'];
export type FirstImportView = Schemas['FirstImportView'];
export type FirstImportInput = Schemas['FirstImportInput'];
export type ImportRowInput = Schemas['ImportRowInput'];

/** A manual basket: name, colour, purpose, order and how many instruments it holds now. */
export type Basket = Schemas['BasketView'];
export type BasketInput = Schemas['BasketInput'];
/** The members of a basket now (or as of a date) with their instrument summaries. */
export type BasketMembers = Schemas['BasketMembers'];
/** One stay of an instrument in a basket (added date, removed date, note). */
export type BasketMember = Schemas['BasketMemberView'];
export type BasketHistory = Schemas['BasketHistory'];
export type BasketHistoryEntry = Schemas['BasketHistoryEntry'];
export type AddMembersResult = Schemas['AddMembersResult'];
export type RemoveMembersResult = Schemas['RemoveMembersResult'];
export type TransferInput = Schemas['TransferInput'];
export type TransferResult = Schemas['TransferResult'];
/** A basket that holds an instrument now. */
export type InstrumentBasket = Schemas['InstrumentBasketView'];

/** Data & import: what is stored, the pipeline (CSV → Dhan → aggregates) and the job in progress. */
export type DataOverview = Schemas['DataOverview'];
export type CsvOverview = Schemas['CsvOverview'];
export type DhanOverview = Schemas['DhanOverview'];
export type AggregatesOverview = Schemas['AggregatesOverview'];
/** The stored 1-minute bars: instruments, first and last bar (the last tick), how far behind. */
export type BarsOverview = Schemas['BarsOverview'];
export type DataCoveragePage = Schemas['DataInstrumentPage'];
export type DataCoverageRow = Schemas['DataInstrumentRow'];
/** One run of the pipeline with a block per phase. */
export type DataJob = Schemas['DataJobView'];
export type CsvPhase = Schemas['CsvPhase'];
/** The CSV folder's files (P1-07a): one page, a file's row with state and mapping, the counts by state. */
export type CsvFilePage = Schemas['CsvFilePage'];
export type CsvFileRow = Schemas['CsvFileRow'];
export type CsvFileCounts = Schemas['CsvFileCounts'];
export type CsvInstrumentRef = Schemas['CsvInstrumentRef'];
/** The dry run over some files: a verdict per file, the first rows and what loading would do to the stored bars. */
export type CsvPreviewResult = Schemas['CsvPreviewResult'];
export type CsvFilePreview = Schemas['CsvFilePreview'];
export type CsvPreviewBar = Schemas['CsvPreviewBar'];
export type CsvPreviewComparison = Schemas['CsvPreviewComparison'];
export type DhanPhase = Schemas['DhanPhase'];
export type AggregatePhase = Schemas['AggregatePhase'];
export type TimeframeStat = Schemas['TimeframeStat'];
/** The latest bars of an instrument and timeframe, newest first. */
export type InstrumentBars = Schemas['InstrumentBars'];
export type BarRow = Schemas['BarRow'];
export type StartDataJobInput = Schemas['StartDataJobRequest'];
/** What an adjusted bars read applied (null for raw prices): the number of actions, the cumulative factor and the actions. */
export type BarsAdjustment = Schemas['AdjustmentView'];
export type BarsAdjustmentAction = Schemas['AdjustmentActionView'];

/** Corporate actions (P1-10a): a split or bonus issue, status suggested · applied · ignored, source manual · detected · csv. */
export type CorporateAction = Schemas['CorporateActionView'];
export type CorporateActionPage = Schemas['CorporateActionPage'];
export type CorporateActionInput = Schemas['AddCorporateActionRequest'];
/** A partial change: `{}` confirms a suggestion as it is; the result is always applied. */
export type CorporateActionUpdate = Schemas['UpdateCorporateActionRequest'];
/** What a detection run did: instruments looked at, candidates found, new suggestions, suggestions waiting now. */
export type DetectionResult = Schemas['DetectionResult'];
/** The outcome of a CSV import or its preview: counts, unknown symbols and the first line errors. */
export type ActionsImportResult = Schemas['ImportCorporateActionsResult'];
export type ImportLineError = Schemas['ImportLineError'];
/** An importable line whose ex-date is on or before the date its instrument's loaded CSV files reach. */
export type ImportLineWarning = Schemas['ImportLineWarning'];

/** The trading calendar: confirmed holidays and special sessions, proposals waiting for the owner, the next holiday. */
export type CalendarView = Schemas['CalendarView'];
/** A date of the calendar: kind holiday | special, source manual | observed | nse, status confirmed | proposed. */
export type CalendarDay = Schemas['CalendarDayView'];
export type CalendarDayInput = Schemas['AddCalendarDayRequest'];
/** What a refresh from NSE's holiday list did (never an error: `succeeded` false says why in `message`). */
export type CalendarRefreshResult = Schemas['CalendarRefreshResult'];

/** The scheduled end-of-day run: switched on, time (IST), next run and the latest automatic run. */
export type EodOverview = Schemas['EodOverview'];
/** An automatic run: status running | succeeded | failed | skipped, with the reason of a failure or skip in `message`. */
export type EodRun = Schemas['EodRunView'];
/** Sessions with missing 1-minute bars by state: open, unfillable, filled. */
export type GapsOverview = Schemas['GapsOverview'];
export type GapRow = Schemas['GapRow'];
export type GapPage = Schemas['GapPage'];
/** What the owner should know about the data (a failed scheduled run, open gaps), for the notification bell. */
export type DataNotice = Schemas['DataNotice'];
export type DataNotices = Schemas['DataNotices'];

/** Data quality (P1-14a): the checks over the last sessions of every tracked instrument with bars. */
export type QualityOverview = Schemas['QualityOverview'];
export type QualityCounts = Schemas['QualityCounts'];
/** The fail-closed gate for the nightly plan: blocked or ok, with the reasons in words. */
export type QualityGate = Schemas['QualityGateView'];
/** One run of the checks: trigger manual · daily · after_job, status running · succeeded · failed. */
export type QualityRun = Schemas['QualityRunView'];
/** The quality block of a data job: instruments checked so far and the counts by result. */
export type QualityPhase = Schemas['QualityPhase'];
export type QualityInstrumentPage = Schemas['QualityInstrumentPage'];
export type QualityInstrumentRow = Schemas['QualityInstrumentRow'];
export type QualityInstrumentDetail = Schemas['QualityInstrumentDetail'];
export type QualityCheckResults = Schemas['QualityCheckResults'];
export type QualityBucketRow = Schemas['QualityBucketRow'];
export type QualityBucketValues = Schemas['QualityBucketValues'];
export type QualityBuckets = Schemas['QualityBuckets'];
export type QualityMissing = Schemas['QualityMissing'];
export type CompletenessCheck = Schemas['CompletenessCheck'];
export type SanityCheck = Schemas['SanityCheck'];
export type SpikeCheck = Schemas['SpikeCheck'];
export type AggregatesCheck = Schemas['AggregatesCheck'];
export type FreshnessCheck = Schemas['FreshnessCheck'];
export type OfficialDailyCheck = Schemas['OfficialDailyCheck'];

/** Services (P1-13): the registered automatic jobs with their schedule, execution settings, parameters, last and next run. */
export type ServiceView = Schemas['ServiceView'];
export type ServiceList = Schemas['ServiceList'];
/** The stored form of a schedule: type daily · weekly · after · window · interval · continuous, times HH:mm India time. */
export type ScheduleSpec = Schemas['ScheduleSpec'];
export type ServiceExecution = Schemas['ExecutionView'];
export type ServiceParameter = Schemas['ParameterView'];
/** One run (or attempt) of a service: trigger scheduled · catch_up · after · manual, status running · succeeded · failed · skipped. */
export type ServiceRun = Schemas['ServiceRunView'];
export type ServiceRunPage = Schemas['ServiceRunPage'];
/** A finding of the schedule checks: severity error (blocks saving) · warning, `field` a dotted path like the request. */
export type ServiceIssue = Schemas['ServiceIssueView'];
export type ServiceScheduleInput = Schemas['ServiceScheduleInput'];
/** The dry run of a save: issues and the next three starts. */
export type SchedulePreview = Schemas['SchedulePreview'];
export type ServicesTimeline = Schemas['ServicesTimeline'];
export type TimelineEntry = Schemas['TimelineEntry'];
/** A failed run to tell the owner of (the bell). */
export type ServiceNotice = Schemas['ServiceNotice'];
export type ServiceNotices = Schemas['ServiceNotices'];

/** The Portfolio Action center (P2-09, ADR 0022): holdings that need a decision, with the figures behind each. */
export type ActionList = Schemas['ActionList'];
export type ActionItem = Schemas['ActionItem'];
export type ActionNumbers = Schemas['ActionNumbers'];
export type ActionCounts = Schemas['ActionCounts'];
/** A profitable open lot that turns 12 months old soon. */
export type LongTermLot = Schemas['LongTermLot'];
export type ActionThresholds = Schemas['ActionThresholds'];
export type ActionThresholdsInput = Schemas['ActionThresholdsInput'];
export type ActionThresholdsView = Schemas['ActionThresholdsView'];
export type ChargeSheetStatusView = Schemas['ChargeSheetStatusView'];
/** One-shot price alerts, checked on every quotes tick. */
export type PriceAlert = Schemas['PriceAlertView'];
export type PriceAlertPage = Schemas['PriceAlertPage'];
export type PriceAlertInput = Schemas['PriceAlertInput'];
/** What the Portfolio module tells the bell: a triggered alert, items waiting for a decision. */
export type PortfolioNotice = Schemas['PortfolioNotice'];
export type PortfolioNotices = Schemas['PortfolioNotices'];

/** Trades (P2-03, ADR 0023): a tradebook CSV or a manual entry, stored as transactions that can rebuild a holding's lots. */
export type TradePreviewReport = Schemas['TradePreviewReport'];
export type TradeFileSummary = Schemas['TradeFileSummary'];
export type TradeBadRow = Schemas['TradeBadRow'];
/** One security of the file: its trades in the file and what rebuilding its holding's lots would do. */
export type TradeSecurity = Schemas['TradeSecurityView'];
/** What rebuilding a holding's lots from its trades does (status ok · unchanged · quantity_mismatch · no_holding · ambiguous). */
export type TradeRebuild = Schemas['TradeRebuildView'];
export type TradeImportResult = Schemas['TradeImportResult'];
/** One imported file; status imported | undone. */
export type TradeBatch = Schemas['TradeBatchView'];
export type TradeBatchPage = Schemas['TradeBatchPage'];
/** What undoing an import did, or with a dry run would do (`dryRun`, `applied`, the lots it would replace, the holdings it cannot rebuild). */
export type TradeUndoResult = Schemas['TradeUndoResult'];
export type TradeNotRebuilt = Schemas['TradeNotRebuilt'];
/** A trade (side buy | sell, source csv | manual); a voided trade does not count. */
export type Trade = Schemas['TradeView'];
export type TradePage = Schemas['TradePage'];
export type ManualTradeInput = Schemas['ManualTradeInput'];
export type HoldingRebuildResult = Schemas['HoldingRebuildResult'];

/** Dhan trade history and ledger (P2-13, ADR 0025): a run reads one kind (trades | ledger); the status says how far each is read. */
export type BrokerSyncRun = Schemas['BrokerSyncRunView'];
export type BrokerSyncCounts = Schemas['BrokerSyncCounts'];
/** The answer of "Sync now": the trades run as it begins and, once it has been carried on to it, the ledger run. */
export type BrokerHistoryRuns = Schemas['BrokerHistoryRunView'];
export type BrokerKindStatus = Schemas['BrokerKindStatus'];
export type BrokerDroppedRow = Schemas['BrokerDroppedRow'];
export type BrokerHistoryStatus = Schemas['BrokerHistoryStatusView'];
/** A holding the automatic rebuild left alone, and why (needs_review, quantity_mismatch, ambiguous, no_holding, changed_meanwhile). */
export type BrokerNotRebuilt = Schemas['BrokerNotRebuilt'];
/** A cash movement of the account read from Dhan's ledger; `amount` is signed, `narration` is untrusted free text. */
export type LedgerEntry = Schemas['LedgerEntryView'];
export type LedgerPage = Schemas['LedgerPage'];
export type LedgerClassTotal = Schemas['LedgerClassTotal'];
export type BrokerHistoryInput = Schemas['BrokerHistoryInput'];

/** Dividends and charges in the returns (P2-14, ADR 0026). */
export type IncomeView = Schemas['IncomeView'];
export type DividendView = Schemas['DividendView'];
export type DividendInput = Schemas['DividendInput'];
export type DividendPage = Schemas['DividendPage'];
export type DividendSuggestionPage = Schemas['DividendSuggestionPage'];
export type AcceptSuggestionInput = Schemas['AcceptSuggestionInput'];
export type SuggestionDecision = Schemas['SuggestionDecisionView'];

/** Dividend file import (P2-15, ADR 0028): preview, import with a digest, batches and Undo. */
export type DividendPreviewReport = Schemas['DividendPreviewReport'];
export type DividendPreviewItem = Schemas['DividendPreviewItem'];
export type DividendFileSummary = Schemas['DividendFileSummary'];
export type DividendMatch = Schemas['DividendMatchView'];
export type DividendCandidate = Schemas['DividendCandidate'];
export type DividendRepeat = Schemas['DividendRepeat'];
export type DividendBadRow = Schemas['DividendBadRow'];
export type DividendChoice = Schemas['DividendChoice'];
export type DividendImportResult = Schemas['DividendImportResult'];
export type DividendBatch = Schemas['DividendBatchView'];
export type DividendBatchPage = Schemas['DividendBatchPage'];
export type DividendUndoResult = Schemas['DividendUndoResult'];

/** Dhan report imports (P2-17, ADR 0029): downloaded files recognised by kind, previewed and imported together. */
export type ImportFilePreview = Schemas['ImportFilePreview'];
export type ImportFileResult = Schemas['ImportFileResult'];
export type ImportFileError = Schemas['ImportFileError'];
export type ImportSummary = Schemas['ImportSummary'];
export type ImportWarning = Schemas['ImportWarning'];
export type ImportPreviewResponse = Schemas['ImportPreviewResponse'];
export type ImportRunResponse = Schemas['ImportRunResponse'];
/** What is particular to a global transaction report: names, coverage by Dhan's own trades, the charges check. */
export type BillDetails = Schemas['BillDetails'];
export type BillNameView = Schemas['BillNameView'];
export type BillCandidate = Schemas['BillCandidate'];
export type BillCoverage = Schemas['BillCoverage'];
export type BillDifference = Schemas['BillDifference'];
export type BillCharges = Schemas['BillCharges'];
/** A depository-charges sheet: its lines, counts and the rupees an import would add to the costs. */
export type DpPreview = Schemas['DpPreview'];
export type DpLine = Schemas['DpLineView'];
export type DpCounts = Schemas['DpCounts'];
export type NameLearning = Schemas['NameLearning'];
/** A demat holding statement: a check against the synced holdings and a source of names; it changes no holding. */
export type DematPreview = Schemas['DematPreview'];
export type DematDifference = Schemas['DematDifference'];
export type DematHeld = Schemas['DematHeld'];
export type DematSynced = Schemas['DematSynced'];
export type DematImportResult = Schemas['DematImportResult'];
/** Imports of depository-charges sheets (cost batches) with Undo. */
export type CostBatch = Schemas['CostBatchView'];
export type CostBatchPage = Schemas['CostBatchPage'];
export type CostImportResult = Schemas['CostImportResult'];
export type CostUndoResult = Schemas['CostUndoResult'];
