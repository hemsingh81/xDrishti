import type { AccountDetail, ConnectionInput } from '@/shared/api';
import type { FieldErrors } from './fieldErrors';

/** Name, colour and notes: the account's identity in this app (wizard step 3 and the Connection tab). */
export interface IdentityFormState {
  name: string;
  color: string;
  notes: string;
}

export interface ConnectionFormState extends IdentityFormState {
  /** Blank = keep the current client id (it is never shown in full, so it cannot be pre-filled). */
  clientId: string;
}

export const clientIdPattern = /^[A-Za-z0-9_-]{4,32}$/;
export const colorPattern = /^#[0-9a-fA-F]{6}$/;

export function connectionFormFrom(account: AccountDetail): ConnectionFormState {
  return { name: account.name, clientId: '', color: account.color, notes: account.notes ?? '' };
}

export function toConnectionInput(state: ConnectionFormState): ConnectionInput {
  return {
    name: state.name.trim(),
    clientId: state.clientId.trim() || null,
    color: state.color,
    notes: state.notes.trim() || null,
  };
}

export function validateIdentity(state: IdentityFormState): FieldErrors {
  const errors: Record<string, string> = {};
  const name = state.name.trim();
  if (name.length === 0 || name.length > 80) {
    errors.name = 'Required, at most 80 characters.';
  }
  if (!colorPattern.test(state.color)) {
    errors.color = 'Use a colour like #1A2B3C.';
  }
  if (state.notes.trim().length > 500) {
    errors.notes = 'At most 500 characters.';
  }
  return errors;
}

/** Client id: blank is fine unless `required` (creating); otherwise 4–32 safe characters. */
export function validateClientId(value: string, required: boolean): string | undefined {
  const clientId = value.trim();
  if (clientId ? clientIdPattern.test(clientId) : !required) {
    return undefined;
  }
  return required ? "Required: 4 to 32 letters, digits, '_' or '-'." : "Use 4 to 32 letters, digits, '_' or '-'.";
}
