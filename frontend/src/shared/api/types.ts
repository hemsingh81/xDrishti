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
export type BrokerId = Schemas['BrokerIdView'];
/** One run of the master-list refresh (manual, daily or forced). */
export type RefreshRun = Schemas['RefreshRunView'];
export type RefreshStatus = Schemas['RefreshStatusView'];
export type AutoRefresh = Schemas['AutoRefreshView'];
