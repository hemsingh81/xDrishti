import { buildCorporateAction } from '@/test/actionFixtures';
import {
  emptyActionForm,
  formFactor,
  formOfAction,
  toActionInput,
  toActionUpdate,
  validateActionForm,
} from './actionForm';
import { actionName, adjustedText, adjustsCsvTwice, factorOf, factorPreview, factorText } from './actions';

describe('factorOf', () => {
  it('turns a split n:d into n/d: old shares to new shares', () => {
    expect(factorOf('split', 1, 5)).toBeCloseTo(0.2);
    expect(factorOf('split', 2, 1)).toBe(2);
    expect(factorOf('split', 2, 3)).toBeCloseTo(2 / 3);
  });

  it('turns a bonus a:b into b/(a+b): a bonus shares for b held', () => {
    expect(factorOf('bonus', 1, 1)).toBe(0.5);
    expect(factorOf('bonus', 1, 2)).toBeCloseTo(2 / 3);
    expect(factorOf('bonus', 3, 1)).toBe(0.25);
  });

  it('gives nothing for numbers that are not whole and positive, or a kind it does not know', () => {
    expect(factorOf('split', 0, 5)).toBeNull();
    expect(factorOf('split', 1.5, 5)).toBeNull();
    expect(factorOf('split', Number.NaN, 5)).toBeNull();
    expect(factorOf('rights', 1, 5)).toBeNull();
  });
});

describe('how a factor is said', () => {
  it('writes the factor with up to four decimals', () => {
    expect(factorText(0.2)).toBe('×0.2');
    expect(factorText(0.5)).toBe('×0.5');
    expect(factorText(2 / 3)).toBe('×0.6667');
    expect(factorText(0.125)).toBe('×0.125');
  });

  it('previews the prices before the ex-date', () => {
    expect(factorPreview('2026-09-01', 0.2)).toBe('Prices before 1 Sept 2026 × 0.2');
  });

  it('names the adjustment of a read', () => {
    expect(adjustedText(0.2, 1)).toBe('Adjusted by ×0.2 for 1 action');
    expect(adjustedText(0.1, 2)).toBe('Adjusted by ×0.1 for 2 actions');
  });

  it('names an action for buttons and toasts', () => {
    expect(actionName(buildCorporateAction())).toBe('NSE:INFY split 1:5 on 1 Sept 2026');
  });
});

describe('the day the CSV files reach', () => {
  it('flags an ex-date on or before it, and not a later one or none', () => {
    expect(adjustsCsvTwice('2026-04-30', '2026-04-30')).toBe(true);
    expect(adjustsCsvTwice('2026-01-15', '2026-04-30')).toBe(true);
    expect(adjustsCsvTwice('2026-05-01', '2026-04-30')).toBe(false);
    expect(adjustsCsvTwice('', '2026-04-30')).toBe(false);
  });
});

describe('the action form', () => {
  const valid = { ...emptyActionForm, instrumentId: 'x', exDate: '2026-09-01', numerator: '1', denominator: '5' };

  it('accepts a complete form', () => {
    expect(validateActionForm(valid, { needsInstrument: true })).toEqual({});
  });

  it('asks for the instrument only when adding', () => {
    const form = { ...valid, instrumentId: '' };

    expect(validateActionForm(form, { needsInstrument: true })).toEqual({ instrumentId: 'Choose an instrument.' });
    expect(validateActionForm(form, { needsInstrument: false })).toEqual({});
  });

  it('checks the date, the whole numbers (1 to 100000), the 1:1 split and the note', () => {
    const errors = validateActionForm(
      { ...valid, exDate: '', numerator: '0', denominator: '100001', note: 'x'.repeat(201) },
      { needsInstrument: true },
    );

    expect(Object.keys(errors).sort()).toEqual(['denominator', 'exDate', 'note', 'numerator']);
    expect(validateActionForm({ ...valid, numerator: '3', denominator: '3' }, { needsInstrument: true })).toEqual({
      denominator: 'A 1:1 split changes nothing. Enter a different ratio.',
    });
    expect(
      validateActionForm({ ...valid, kind: 'bonus', numerator: '1', denominator: '1' }, { needsInstrument: true }),
    ).toEqual({});
  });

  it('gives the factor of the numbers typed, none for a 1:1 split or text', () => {
    expect(formFactor(valid)).toBeCloseTo(0.2);
    expect(formFactor({ ...valid, kind: 'bonus', numerator: '1', denominator: '1' })).toBe(0.5);
    expect(formFactor({ ...valid, numerator: '2', denominator: '2' })).toBeNull();
    expect(formFactor({ ...valid, numerator: 'a' })).toBeNull();
    expect(formFactor({ ...valid, numerator: '1.5' })).toBeNull();
  });

  it('builds the request to add, with the note trimmed or left out', () => {
    expect(toActionInput({ ...valid, note: '  Split  ' })).toEqual({
      instrumentId: 'x',
      exDate: '2026-09-01',
      kind: 'split',
      numerator: 1,
      denominator: 5,
      note: 'Split',
    });
    expect(toActionInput(valid).note).toBeNull();
  });

  it('starts an edit from the action and sends every field, an empty note too (it clears the note)', () => {
    const form = formOfAction(buildCorporateAction({ note: null }));

    expect(form).toMatchObject({ exDate: '2026-09-01', kind: 'split', numerator: '1', denominator: '5', note: '' });
    expect(toActionUpdate(form)).toEqual({
      exDate: '2026-09-01',
      kind: 'split',
      numerator: 1,
      denominator: 5,
      note: '',
    });
  });
});
