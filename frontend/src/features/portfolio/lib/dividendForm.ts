import type { DividendInput } from '@/shared/api';
import { isRealDay, todayIso } from '@/shared/lib/dates';
import { minTradeDate, parseAmount } from './tradeForm';

/** The Add dividend form as typed: numbers stay text until they are checked. */
export interface DividendForm {
  /** The holding the dividend is for; '' = none (an unallocated dividend of the account). */
  holdingId: string;
  payDate: string;
  amount: string;
  perShare: string;
  note: string;
}

export const emptyDividendForm: DividendForm = { holdingId: '', payDate: '', amount: '', perShare: '', note: '' };

export type DividendField = 'account' | 'payDate' | 'amount' | 'perShare' | 'note';
export type DividendFormErrors = Partial<Record<DividendField, string>>;

/** The order of the fields on the form: the first one with a problem gets the keyboard. */
export const dividendFieldOrder: readonly DividendField[] = ['account', 'payDate', 'amount', 'perShare', 'note'];

export const maxNoteLength = 200;

/** Rupees and paise: a plain number with at most two decimals. */
const paise = /^\d{1,12}(\.\d{1,2})?$/;

/** What is wrong with the form, field by field (the API checks again and has the last word). */
export function validateDividend(
  form: DividendForm,
  accountId: string,
  today: string = todayIso(),
): DividendFormErrors {
  const errors: DividendFormErrors = {};
  if (accountId === '') errors.account = 'Choose the account.';
  if (form.payDate === '') errors.payDate = 'Enter the day the money was credited.';
  else if (!isRealDay(form.payDate)) errors.payDate = 'Enter a valid date.';
  else if (form.payDate < minTradeDate) errors.payDate = 'The date cannot be before 2 Jan 1990.';
  else if (form.payDate > today) errors.payDate = 'The date cannot be in the future.';
  const amount = parseAmount(form.amount);
  if (Number.isNaN(amount)) {
    errors.amount =
      form.amount.trim() === ''
        ? 'Enter the amount you received.'
        : 'Enter the amount as a plain number, for example 1250.50.';
  } else if (amount <= 0) errors.amount = 'The amount must be above 0.';
  else if (!paise.test(form.amount.replaceAll(',', '').trim()))
    errors.amount = 'Enter rupees and paise: at most two decimals.';
  if (form.perShare.trim() !== '') {
    const perShare = parseAmount(form.perShare);
    if (Number.isNaN(perShare)) errors.perShare = 'Enter the amount per share as a number, or leave it empty.';
    else if (perShare <= 0) errors.perShare = 'The amount per share must be above 0.';
  }
  if (form.note.length > maxNoteLength) errors.note = `The note can have at most ${String(maxNoteLength)} characters.`;
  return errors;
}

/** The request for a valid form; `confirm` repeats it after the owner said the second payment is genuine. */
export function toDividendInput(form: DividendForm, accountId: string, confirm: boolean): DividendInput {
  const holding = form.holdingId !== '';
  return {
    accountId: holding ? null : accountId,
    holdingId: holding ? form.holdingId : null,
    payDate: form.payDate,
    amount: parseAmount(form.amount),
    perShare: form.perShare.trim() === '' ? null : parseAmount(form.perShare),
    note: form.note.trim() === '' ? null : form.note.trim(),
    confirm: confirm ? true : null,
  };
}

const fieldOf: Record<string, DividendField> = {
  accountId: 'account',
  holdingId: 'account',
  payDate: 'payDate',
  amount: 'amount',
  perShare: 'perShare',
  note: 'note',
};

/** The API's messages (keyed by its field names) placed at the form's fields; what has no field is returned apart. */
export function placeDividendErrors(messages: Readonly<Record<string, string>>): {
  errors: DividendFormErrors;
  unplaced: string[];
} {
  const errors: DividendFormErrors = {};
  const unplaced: string[] = [];
  for (const [name, message] of Object.entries(messages)) {
    const key = `${name.charAt(0).toLowerCase()}${name.slice(1)}`;
    const field = Object.hasOwn(fieldOf, key) ? fieldOf[key] : undefined;
    if (!field) unplaced.push(message);
    else errors[field] = errors[field] ? `${errors[field]} ${message}` : message;
  }
  return { errors, unplaced };
}
