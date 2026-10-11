import type { AccountSummary, CreateAccountInput } from '@/shared/api';
import { accountColors } from '@/theme';
import { validateClientId, validateIdentity, type IdentityFormState } from './connectionForm';
import { dhanApp } from './dhanApp';
import type { FieldErrors } from './fieldErrors';
import {
  portfolioFormFrom,
  toPortfolioInput,
  validateLiveFeedBudget,
  validatePortfolio,
  type PortfolioFormState,
} from './portfolioForm';
import type { RoleDraft } from './roleChanges';
import { emptySecrets, validateNewSecrets, type SecretsFormState } from './secrets';
import { toTradingInput, tradingFormFrom, validateTrading, type TradingFormState } from './tradingForm';

export const wizardSteps = ['Dhan app', 'Identity', 'Roles', 'Role settings', 'Review'] as const;
export const lastStep = wizardSteps.length - 1;

/** What the Dhan app needs: the client id and the write-only API key and secret. */
export interface BrokerAppDraft extends SecretsFormState {
  clientId: string;
}

export interface WizardState {
  /** Client id, API key and secret. Lives only in this state, never in a cache. */
  app: BrokerAppDraft;
  identity: IdentityFormState;
  /** The owner typed a name: stop suggesting one. */
  nameEdited: boolean;
  roles: RoleDraft;
  trading: TradingFormState;
  portfolio: PortfolioFormState;
  liveFeedBudget: string;
}

export const emptyBrokerApp = (): BrokerAppDraft => ({ clientId: '', ...emptySecrets() });

export function initialWizardState(existing: readonly AccountSummary[]): WizardState {
  return {
    app: emptyBrokerApp(),
    identity: {
      name: '',
      color: accountColors[existing.length % accountColors.length] ?? accountColors[0],
      notes: '',
    },
    nameEdited: false,
    // Like the prototype: Portfolio is on; the first Data account becomes primary, later ones standby.
    roles: {
      data: false,
      dataKind: existing.some((a) => a.roles.data.primary) ? 'standby' : 'primary',
      trading: false,
      portfolio: true,
    },
    trading: tradingFormFrom(),
    portfolio: portfolioFormFrom(),
    liveFeedBudget: '200',
  };
}

/** "Dhan · Main" when no account has that name yet, else "Dhan · 2", "Dhan · 3", … */
export function suggestName(existing: readonly Pick<AccountSummary, 'name'>[]): string {
  const taken = new Set(existing.map((a) => a.name.trim().toLowerCase()));
  const candidates = [`${dhanApp.name} · Main`, ...Array.from({ length: 98 }, (_, i) => `${dhanApp.name} · ${i + 2}`)];
  return candidates.find((name) => !taken.has(name.toLowerCase())) ?? '';
}

/** The client-side checks of one step (the server repeats them all). */
export function validateStep(step: number, state: WizardState): FieldErrors {
  const errors: Record<string, string> = {};
  if (step === 0) {
    const clientId = validateClientId(state.app.clientId, true);
    if (clientId) {
      errors.clientId = clientId;
    }
    Object.assign(errors, validateNewSecrets(state.app));
  }
  if (step === 1) {
    Object.assign(errors, validateIdentity(state.identity));
  }
  if (step === 2 && !state.roles.data && !state.roles.trading && !state.roles.portfolio) {
    errors.roles = 'Choose at least one role.';
  }
  if (step === 3) {
    if (state.roles.trading) {
      for (const [field, message] of Object.entries(validateTrading(state.trading))) {
        errors[`trading.${field}`] = message;
      }
    }
    if (state.roles.portfolio) {
      for (const [field, message] of Object.entries(validatePortfolio(state.portfolio))) {
        errors[`portfolio.${field}`] = message;
      }
    }
    const budget = state.roles.data ? validateLiveFeedBudget(state.liveFeedBudget) : undefined;
    if (budget) {
      errors['data.liveFeedBudget'] = budget;
    }
  }
  return errors;
}

/** Which wizard step shows the field the API complained about (paths as in the create request). */
export function stepOfField(field: string): number {
  if (field === 'clientId' || field === 'apiKeyIssuedOn' || field === 'secrets' || field.startsWith('secrets.')) {
    return 0;
  }
  if (field === 'roles' || field.startsWith('roles.')) {
    return 2;
  }
  if (/^(trading|portfolio|data)(\.|$)/.test(field)) {
    return 3;
  }
  return 1;
}

export function toCreateInput(state: WizardState): CreateAccountInput {
  const { identity, roles, app } = state;
  return {
    name: identity.name.trim(),
    clientId: app.clientId.trim(),
    secrets: { apiKey: app.apiKey.trim(), apiSecret: app.apiSecret.trim() },
    apiKeyIssuedOn: app.apiKeyIssuedOn,
    color: identity.color,
    notes: identity.notes.trim() || null,
    roles: {
      data: roles.data ? { enabled: true, kind: roles.dataKind } : null,
      trading: roles.trading ? { enabled: true } : null,
      portfolio: roles.portfolio ? { enabled: true } : null,
    },
    trading: roles.trading ? toTradingInput(state.trading) : null,
    portfolio: roles.portfolio ? toPortfolioInput(state.portfolio) : null,
    data: roles.data ? { liveFeedBudget: Number(state.liveFeedBudget) } : null,
  };
}

/** First four and last two characters, like the API shows them. */
export const maskClientId = (id: string): string => (id.length > 6 ? `${id.slice(0, 4)}••${id.slice(-2)}` : '••••••');
