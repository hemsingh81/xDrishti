import type { CorporateAction, CorporateActionInput, CorporateActionUpdate } from '@/shared/api';
import { factorOf, maxActionNote, maxRatioPart, type ActionKind } from './actions';

export interface ActionForm {
  /** Only used when adding; an edit keeps its instrument. */
  instrumentId: string;
  /** yyyy-MM-dd, '' until chosen. */
  exDate: string;
  kind: ActionKind;
  numerator: string;
  denominator: string;
  note: string;
}

export const emptyActionForm: ActionForm = {
  instrumentId: '',
  exDate: '',
  kind: 'split',
  numerator: '',
  denominator: '',
  note: '',
};

/** An edit starts from what the action has now (a detected suggestion keeps the detector's note). */
export const formOfAction = (action: CorporateAction): ActionForm => ({
  instrumentId: action.instrumentId,
  exDate: action.exDate,
  kind: action.kind === 'bonus' ? 'bonus' : 'split',
  numerator: String(action.numerator),
  denominator: String(action.denominator),
  note: action.note ?? '',
});

const wholeNumber = /^\d+$/;
const part = (text: string): number => (wholeNumber.test(text.trim()) ? Number(text.trim()) : Number.NaN);

/** The ratio as numbers; NaN for text that is not a whole number. */
export const ratioOf = (form: Pick<ActionForm, 'numerator' | 'denominator'>) => ({
  numerator: part(form.numerator),
  denominator: part(form.denominator),
});

/** The factor the form's numbers give, or null while they are not usable. */
export function formFactor(form: ActionForm): number | null {
  const { numerator, denominator } = ratioOf(form);
  if (form.kind === 'split' && numerator === denominator) return null;
  return factorOf(form.kind, numerator, denominator);
}

/** Client-side checks (the server checks again); messages by field. */
export function validateActionForm(form: ActionForm, { needsInstrument }: { needsInstrument: boolean }) {
  const errors: Record<string, string> = {};
  if (needsInstrument && form.instrumentId === '') errors.instrumentId = 'Choose an instrument.';
  if (form.exDate === '') errors.exDate = 'Choose the ex-date.';
  const { numerator, denominator } = ratioOf(form);
  const inRange = (value: number) => Number.isInteger(value) && value >= 1 && value <= maxRatioPart;
  const range = `Enter a whole number from 1 to ${String(maxRatioPart)}.`;
  if (!inRange(numerator)) errors.numerator = range;
  if (!inRange(denominator)) errors.denominator = range;
  if (form.kind === 'split' && inRange(numerator) && numerator === denominator) {
    errors.denominator = 'A 1:1 split changes nothing. Enter a different ratio.';
  }
  if (form.note.length > maxActionNote) errors.note = `The note can have at most ${String(maxActionNote)} characters.`;
  return errors;
}

export function toActionInput(form: ActionForm): CorporateActionInput {
  const { numerator, denominator } = ratioOf(form);
  const note = form.note.trim();
  return {
    instrumentId: form.instrumentId,
    exDate: form.exDate,
    kind: form.kind,
    numerator,
    denominator,
    note: note === '' ? null : note,
  };
}

/** An edit sends the whole form, the note too (an empty text clears it). */
export function toActionUpdate(form: ActionForm): CorporateActionUpdate {
  const { numerator, denominator } = ratioOf(form);
  return { exDate: form.exDate, kind: form.kind, numerator, denominator, note: form.note.trim() };
}

/** Nothing changed: the body that confirms a suggestion (or brings back an ignored action) as it is. */
export const confirmAsIs: CorporateActionUpdate = {
  exDate: null,
  kind: null,
  numerator: null,
  denominator: null,
  note: null,
};
