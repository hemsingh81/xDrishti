import type { ManualTradeInput } from '@/shared/api';
import { isRealDay, todayIso } from '@/shared/lib/dates';

/** The earliest trade date the API accepts. */
export const minTradeDate = '1990-01-02';

export type TradeSide = 'buy' | 'sell';

/** The Add trade form as typed: numbers stay text until they are checked. */
export interface TradeForm {
  /** The holding picked in the account's list; '' while none is. */
  holdingId: string;
  /** A security that is not in the list, typed in (instead of the holding). */
  typed: boolean;
  symbol: string;
  isin: string;
  side: TradeSide | '';
  tradeDate: string;
  tradeTime: string;
  quantity: string;
  price: string;
  charges: string;
}

export const emptyTradeForm: TradeForm = {
  holdingId: '',
  typed: false,
  symbol: '',
  isin: '',
  side: '',
  tradeDate: '',
  tradeTime: '',
  quantity: '',
  price: '',
  charges: '',
};

export type TradeField = 'security' | 'side' | 'tradeDate' | 'tradeTime' | 'quantity' | 'price' | 'charges';
export type TradeFormErrors = Partial<Record<TradeField, string>>;

/** The order the fields are on the form: the first one with a problem gets the keyboard. */
export const tradeFieldOrder: readonly TradeField[] = [
  'security',
  'side',
  'tradeDate',
  'tradeTime',
  'quantity',
  'price',
  'charges',
];

/** A plain decimal number: optional minus, up to 12 digits, up to 6 decimals (no exponent, hex or Infinity). */
const decimal = /^-?\d{1,12}(\.\d{1,6})?$/;

/** A number as typed ("1,250.50"); NaN when it is not a plain decimal. */
export function parseAmount(text: string): number {
  const cleaned = text.replaceAll(',', '').trim();
  return decimal.test(cleaned) ? Number(cleaned) : Number.NaN;
}

/** What is wrong with the form, field by field (the API checks again and has the last word). `holding` is given when the form is for one holding. */
export function validateTrade(form: TradeForm, holdingGiven: boolean, today: string = todayIso()): TradeFormErrors {
  const errors: TradeFormErrors = {};
  if (!holdingGiven) {
    if (form.typed && form.symbol.trim() === '' && form.isin.trim() === '') {
      errors.security = 'Enter the symbol or the ISIN of an NSE share or ETF.';
    } else if (!form.typed && form.holdingId === '') {
      errors.security = 'Choose the security.';
    }
  }
  if (form.side === '') errors.side = 'Choose buy or sell.';
  if (form.tradeDate === '') errors.tradeDate = 'Enter the trade date.';
  else if (!isRealDay(form.tradeDate)) errors.tradeDate = 'Enter a valid date.';
  else if (form.tradeDate < minTradeDate) errors.tradeDate = 'The date cannot be before 2 Jan 1990.';
  else if (form.tradeDate > today) errors.tradeDate = 'The date cannot be in the future.';
  const quantity = parseAmount(form.quantity);
  if (Number.isNaN(quantity)) {
    errors.quantity =
      form.quantity.trim() === '' ? 'Enter the quantity.' : 'Enter the quantity as a plain number, for example 10.';
  } else if (!Number.isInteger(quantity)) errors.quantity = 'The quantity must be a whole number of shares.';
  else if (quantity <= 0) errors.quantity = 'The quantity must be above 0.';
  const price = parseAmount(form.price);
  if (Number.isNaN(price)) {
    errors.price =
      form.price.trim() === '' ? 'Enter the price.' : 'Enter the price as a plain number, for example 1250.50.';
  } else if (price <= 0) errors.price = 'The price must be above 0.';
  if (form.charges.trim() !== '') {
    const charges = parseAmount(form.charges);
    if (Number.isNaN(charges)) errors.charges = 'Enter the charges as a number, or leave them empty.';
    else if (charges < 0) errors.charges = 'The charges cannot be negative.';
  }
  return errors;
}

/** The request for a valid form: the holding, or the account with the typed symbol and ISIN. */
export function toManualInput(form: TradeForm, accountId: string, holdingId: string | null): ManualTradeInput {
  const holding = holdingId ?? (form.typed ? '' : form.holdingId);
  const byHolding = holding !== '';
  return {
    accountId,
    holdingId: byHolding ? holding : null,
    symbol: byHolding || form.symbol.trim() === '' ? null : form.symbol.trim(),
    isin: byHolding || form.isin.trim() === '' ? null : form.isin.trim(),
    side: form.side === '' ? null : form.side,
    tradeDate: form.tradeDate,
    // The input gives HH:mm; the API reads a time of day with seconds.
    tradeTime: form.tradeTime === '' ? null : form.tradeTime.length === 5 ? `${form.tradeTime}:00` : form.tradeTime,
    quantity: parseAmount(form.quantity),
    price: parseAmount(form.price),
    charges: form.charges.trim() === '' ? null : parseAmount(form.charges),
  };
}

const fieldOf: Record<string, TradeField> = {
  holdingId: 'security',
  symbol: 'security',
  isin: 'security',
  accountId: 'security',
  side: 'side',
  tradeDate: 'tradeDate',
  tradeTime: 'tradeTime',
  quantity: 'quantity',
  price: 'price',
  charges: 'charges',
};

/** The API's messages (keyed by its field names) placed at the form's fields; what has no field is returned apart. */
export function placeServerErrors(messages: Readonly<Record<string, string>>): {
  errors: TradeFormErrors;
  unplaced: string[];
} {
  const errors: TradeFormErrors = {};
  const unplaced: string[] = [];
  for (const [name, message] of Object.entries(messages)) {
    const key = `${name.charAt(0).toLowerCase()}${name.slice(1)}`;
    const field = Object.hasOwn(fieldOf, key) ? fieldOf[key] : undefined;
    if (!field) unplaced.push(message);
    else errors[field] = errors[field] ? `${errors[field]} ${message}` : message;
  }
  return { errors, unplaced };
}
