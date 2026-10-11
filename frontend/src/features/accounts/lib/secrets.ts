import type { SecretStatus, UpdateSecretsInput } from '@/shared/api';
import { dhanApp } from './dhanApp';
import { formatDate } from '@/shared/lib/format';
import type { FieldErrors } from './fieldErrors';
import { todayIso } from './portfolioForm';

/** Short names for status lines; `dhanApp` carries the long labels ("API key (app_id)"). */
const secretNames: Record<string, string> = { apiKey: 'API key', apiSecret: 'API secret' };
export const secretName = (name: string): string => secretNames[name] ?? name;

/** "API key: set · updated 11 Oct 2026" or "API key: not set". Never anything about the value. */
export function secretStatusText(secret: SecretStatus): string {
  if (!secret.set) {
    return `${secretName(secret.name)}: not set`;
  }
  return secret.updatedAt
    ? `${secretName(secret.name)}: set · updated ${formatDate(secret.updatedAt)}`
    : `${secretName(secret.name)}: set`;
}

const dayMs = 86_400_000;
const dayNumber = (iso: string): number => Date.parse(`${iso}T00:00:00Z`) / dayMs;

export type KeyExpiry =
  { state: 'ok' | 'soon'; expiresOn: string; daysLeft: number } | { state: 'expired'; expiresOn: string };

/** `expiresOn` is the first day the key no longer works; "soon" = within the warning window. */
export function keyExpiry(
  expiresOn: string,
  warningDays: number = dhanApp.apiKeyExpiryWarningDays,
  today: string = todayIso(),
): KeyExpiry {
  const daysLeft = Math.round(dayNumber(expiresOn) - dayNumber(today));
  if (daysLeft <= 0) {
    return { state: 'expired', expiresOn };
  }
  return { state: daysLeft <= warningDays ? 'soon' : 'ok', expiresOn, daysLeft };
}

/** Write-only secret fields as typed (never pre-filled) plus the Dhan "generated on" date (yyyy-MM-dd). */
export interface SecretsFormState {
  apiKey: string;
  apiSecret: string;
  apiKeyIssuedOn: string;
}

export const emptySecrets = (): SecretsFormState => ({ apiKey: '', apiSecret: '', apiKeyIssuedOn: todayIso() });

export function validateIssuedOn(value: string, today: string = todayIso()): string | undefined {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && value <= today
    ? undefined
    : 'Pick the day the key was generated (up to today).';
}

/** Creating an account: both secrets are required; keys are as in the create request (`secrets.apiKey`, …). */
export function validateNewSecrets(state: SecretsFormState): FieldErrors {
  const errors: Record<string, string> = {};
  for (const name of ['apiKey', 'apiSecret'] as const) {
    if (state[name].trim() === '') {
      errors[`secrets.${name}`] = `Enter the ${secretName(name)}.`;
    }
  }
  const issued = validateIssuedOn(state.apiKeyIssuedOn);
  if (issued) {
    errors.apiKeyIssuedOn = issued;
  }
  return errors;
}

/** The replace dialog: blank keeps a secret; `issuedOnTouched` = the owner changed the date by hand. */
export interface ReplaceSecretsDraft extends SecretsFormState {
  issuedOnTouched: boolean;
}

/**
 * The "generated on" date the dialog shows and sends: what the owner picked; otherwise today for a new key (the
 * server's default too); otherwise the stored date.
 */
export function effectiveIssuedOn(draft: ReplaceSecretsDraft, current: string): string {
  if (draft.issuedOnTouched) {
    return draft.apiKeyIssuedOn;
  }
  return draft.apiKey.trim() ? todayIso() : current;
}

export function toUpdateSecretsInput(draft: ReplaceSecretsDraft, current: string): UpdateSecretsInput {
  const apiKey = draft.apiKey.trim() || null;
  const sendDate = apiKey !== null || draft.issuedOnTouched;
  return {
    apiKey,
    apiSecret: draft.apiSecret.trim() || null,
    apiKeyIssuedOn: sendDate ? effectiveIssuedOn(draft, current) : null,
  };
}

export function validateReplaceSecrets(draft: ReplaceSecretsDraft, current: string): FieldErrors {
  const input = toUpdateSecretsInput(draft, current);
  if (input.apiKeyIssuedOn) {
    const issued = validateIssuedOn(input.apiKeyIssuedOn);
    if (issued) {
      return { apiKeyIssuedOn: issued };
    }
  }
  const unchanged = !input.apiKey && !input.apiSecret && (input.apiKeyIssuedOn ?? current) === current;
  return unchanged ? { apiKey: 'Enter a new API key, a new API secret, or both.' } : {};
}
