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
export type HoldingLots = Schemas['HoldingLots'];
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
export type DhanPhase = Schemas['DhanPhase'];
export type AggregatePhase = Schemas['AggregatePhase'];
export type TimeframeStat = Schemas['TimeframeStat'];
/** The latest bars of an instrument and timeframe, newest first. */
export type InstrumentBars = Schemas['InstrumentBars'];
export type BarRow = Schemas['BarRow'];
export type StartDataJobInput = Schemas['StartDataJobRequest'];
