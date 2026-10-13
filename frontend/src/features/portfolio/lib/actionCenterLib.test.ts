import {
  buildActionCounts,
  buildActionItem,
  buildActionList,
  buildAlertPage,
  buildPriceAlert,
  buildTriggeredAlert,
  countsOf,
  sampleActionItems,
} from '@/test/actionCenterFixtures';
import { holdingIds } from '@/test/fixtures';
import { withoutActiveAlert, withoutActiveAlertPages } from './alertCache';
import { applyDecision, addDays } from './actionDecision';
import { coverageNotes, decidedText } from './actionText';
import { figuresOf } from './actionFigures';
import { groupBySeverity, holdingIdOf, needDecisionText, rowActionsOf, severityOf, subjectOf } from './actions';
import { conditionText, parseLevel, presetLevel, toAlertInput, validateAlert, type AlertForm } from './alerts';

const today = '2026-10-13';

describe('applyDecision', () => {
  const list = buildActionList(sampleActionItems());
  const tcs = sampleActionItems()[0]?.id ?? '';

  it('moves an open item to snoozed with the day it comes back, and moves the counts with it', () => {
    const next = applyDecision(list, 'open', tcs, 'snooze', today);

    expect(next.items.map((item) => item.id)).not.toContain(tcs);
    expect(next.counts).toMatchObject({ open: 6, snoozed: 1, done: 0 });
    expect(next.counts.byRule.below_cost).toBe(0);
    expect(next.counts.bySeverity.high).toBe(0);
    expect(next.counts.bySeverity.medium).toBe(3);
  });

  it('keeps the item, changed, in the list of its new state', () => {
    const snoozed = applyDecision(list, 'snoozed', tcs, 'snooze', today);

    expect(snoozed.items.find((item) => item.id === tcs)).toMatchObject({
      state: 'snoozed',
      snoozedUntil: '2026-10-20',
    });
    const all = applyDecision(list, 'all', tcs, 'done', today);
    expect(all.items.find((item) => item.id === tcs)).toMatchObject({ state: 'done', snoozedUntil: null });
  });

  it('counts a reopened item among the open ones again, by rule and severity', () => {
    const snoozed = buildActionList([buildActionItem({ state: 'snoozed', snoozedUntil: '2026-10-20' })]);

    const next = applyDecision(snoozed, 'snoozed', tcs, 'reopen', today);

    expect(next.items).toEqual([]);
    expect(next.counts).toMatchObject({ open: 1, snoozed: 0 });
    expect(next.counts.byRule.below_cost).toBe(1);
    expect(next.counts.bySeverity.high).toBe(1);
  });

  it('changes nothing for an item the list does not hold or that is already in that state', () => {
    expect(applyDecision(list, 'open', 'below_cost:nobody', 'done', today)).toBe(list);
    expect(applyDecision(list, 'open', tcs, 'reopen', today)).toBe(list);
  });

  it('never counts below zero', () => {
    const odd = { ...list, counts: buildActionCounts({ open: 0, byRule: {}, bySeverity: {} }) };
    const next = applyDecision(odd, 'open', tcs, 'done', today);
    expect(next.counts.open).toBe(0);
    expect(next.counts.byRule.below_cost).toBe(0);
  });
});

describe('addDays', () => {
  it.each([
    ['2026-10-13', 7, '2026-10-20'],
    ['2026-10-28', 7, '2026-11-04'],
    ['2026-12-28', 7, '2027-01-04'],
    ['2028-02-25', 7, '2028-03-03'],
  ])('%s + %i days = %s', (day, days, expected) => {
    expect(addDays(day, days)).toBe(expected);
  });
});

describe('Action center helpers', () => {
  it('groups by severity, most urgent first, and keeps the API order inside a group', () => {
    const groups = groupBySeverity(sampleActionItems());

    expect(groups.map((g) => [g.severity, g.items.map((i) => i.rule)])).toEqual([
      ['high', ['below_cost']],
      ['medium', ['lagging_nifty', 'overweight', 'sector_concentration']],
      ['info', ['large_gain', 'long_term_soon', 'not_in_basket']],
    ]);
    expect(groupBySeverity([])).toEqual([]);
  });

  it('treats a severity it does not know as the least urgent', () => {
    expect(severityOf({ severity: 'urgent' })).toBe('info');
  });

  it.each([
    [0, 'Nothing needs a decision'],
    [1, '1 needs a decision'],
    [5, '5 need a decision'],
  ])('says "%i" as "%s"', (open, text) => {
    expect(needDecisionText(open)).toBe(text);
  });

  it('names what an item is about', () => {
    expect(subjectOf(buildActionItem())).toBe('TCS');
    const concentration = sampleActionItems().find((i) => i.rule === 'sector_concentration') ?? buildActionItem();
    expect(subjectOf(concentration)).toBe('Test Dhan Main Energy');
    expect(holdingIdOf(concentration)).toBe(holdingIds.reliance);
  });

  it('offers the buttons the API suggests, minus those that need an instrument the item lacks', () => {
    expect(rowActionsOf(buildActionItem())).toEqual(['open', 'set_alert', 'add_to_basket', 'snooze', 'done']);
    expect(rowActionsOf(buildActionItem({ instrumentId: null }))).toEqual(['open', 'snooze', 'done']);
    expect(rowActionsOf(buildActionItem({ suggestions: ['snooze'] }))).toEqual(['snooze']);
    expect(rowActionsOf(buildActionItem({ state: 'snoozed' }))).toEqual(['open', 'reopen']);
    expect(rowActionsOf(buildActionItem({ state: 'done', suggestions: [] }))).toEqual(['reopen']);
  });

  it('says which rules and holdings the figures did not cover', () => {
    expect(coverageNotes(countsOf([]))).toEqual([]);
    expect(coverageNotes(buildActionCounts({ unavailable: { lagging: 1 }, noPrice: 1 }))).toEqual([
      '1 rule not evaluated: Lagging Nifty needs Nifty 50 history (1 holding affected).',
      '1 holding without a price is not checked by the price rules.',
    ]);
  });

  it('words the toast after a decision', () => {
    const item = buildActionItem();
    expect(decidedText('snooze', item, '2026-10-20')).toBe('Snoozed TCS until 20 Oct 2026.');
    expect(decidedText('snooze', item, null)).toBe('Snoozed TCS for 7 days.');
    expect(decidedText('reopen', item, null)).toBe('Reopened TCS.');
  });
});

describe('figuresOf', () => {
  const find = (rule: string) => sampleActionItems().find((item) => item.rule === rule) ?? buildActionItem();

  it('gives the figures behind each rule, with signs and units', () => {
    expect(figuresOf(find('below_cost'))).toEqual([
      { label: 'Profit / loss', value: '−₹9,360 (−23.40%)' },
      { label: 'Invested', value: '₹40,000' },
      { label: 'Value', value: '₹30,640' },
    ]);
    expect(figuresOf(find('overweight'))).toEqual([
      { label: 'Weight', value: '42.1%' },
      { label: 'Limit', value: '22.5%' },
      { label: 'Positions in the account', value: '4' },
      { label: 'Value', value: '₹1,16,100' },
    ]);
    expect(figuresOf(find('lagging_nifty'))).toEqual([
      { label: 'XIRR', value: '+4.10%' },
      { label: 'Nifty 50 XIRR', value: '+16.80%' },
      { label: 'Gap', value: '−12.7 pp' },
      { label: 'Held', value: '412 days' },
    ]);
  });

  it('leaves out the numbers the API did not send, and knows no figures for an unknown rule', () => {
    expect(figuresOf(buildActionItem({ numbers: { pnl: -100 } }))).toEqual([
      { label: 'Profit / loss', value: '−₹100' },
    ]);
    expect(figuresOf(buildActionItem({ rule: 'future_rule' }))).toEqual([]);
  });

  it('words each lot of the long-term rule, today and tomorrow included', () => {
    const lot = { acquiredOn: '2025-10-13', qty: 1, longTermOn: '2026-10-13', daysLeft: 0, gainPct: 5 };
    const [today0, tomorrow] = figuresOf(
      buildActionItem({
        rule: 'long_term_soon',
        numbers: { lots: [lot, { ...lot, daysLeft: 1, longTermOn: '2026-10-14', gainPct: null }] },
      }),
    );
    expect(today0?.value).toBe('12 months on 13 Oct 2026 (today), +5.00%');
    expect(tomorrow?.value).toBe('12 months on 14 Oct 2026 (in 1 day), free shares (no cost)');
  });
});

describe('price alert helpers', () => {
  const form = (overrides: Partial<AlertForm> = {}): AlertForm => ({
    instrumentId: 'i',
    direction: 'at_or_below',
    level: '1200',
    note: '',
    ...overrides,
  });

  it.each([
    ['1200', 1200],
    ['1,200.50', 1200.5],
    ['₹ 99', 99],
    ['0.0001', 0.0001],
    ['', null],
    ['abc', null],
    ['-5', null],
    ['1e3', null],
    ['12.', null],
  ])('reads "%s" as %s', (text, value) => {
    expect(parseLevel(text)).toBe(value);
  });

  it('computes the quick levels from the last price to the paisa, down for "at or below", up for "at or above"', () => {
    expect(presetLevel('at_or_below', 1000, 5)).toBe(950);
    expect(presetLevel('at_or_below', 2902.5, 5)).toBe(2757.38);
    expect(presetLevel('at_or_above', 2902.5, 20)).toBe(3483);
    expect(presetLevel('at_or_above', 333.33, 10)).toBe(366.66);
  });

  it('validates like the API, field by field', () => {
    expect(validateAlert(form())).toEqual({});
    expect(validateAlert(form({ instrumentId: '' }))).toEqual({ instrumentId: 'Choose a share or ETF.' });
    expect(validateAlert(form({ level: '' })).level).toBe('Enter the price to be alerted at.');
    expect(validateAlert(form({ level: '0' })).level).toBe('Enter a price above 0, e.g. 1200 or 1200.50.');
    expect(validateAlert(form({ level: '10000001' })).level).toBe('The price can be at most ₹1,00,00,000.');
    expect(validateAlert(form({ level: '10000000' }))).toEqual({});
    expect(validateAlert(form({ level: '1.12345' })).level).toBe('Use at most 4 decimals.');
    expect(validateAlert(form({ level: '1.1234' }))).toEqual({});
    expect(validateAlert(form({ note: 'x'.repeat(200) }))).toEqual({});
    expect(validateAlert(form({ note: 'x'.repeat(201) })).note).toBe('At most 200 characters.');
  });

  it('builds the request: a trimmed note or none', () => {
    expect(toAlertInput(form({ level: '1,200.5', note: ' hi ' }))).toEqual({
      instrumentId: 'i',
      direction: 'at_or_below',
      level: 1200.5,
      note: 'hi',
    });
    expect(toAlertInput(form({ note: '   ' })).note).toBeNull();
  });

  it('says the condition in words', () => {
    expect(conditionText(buildPriceAlert())).toBe('at or below ₹2,500.00');
    expect(conditionText(buildTriggeredAlert())).toBe('at or above ₹3,900.00');
  });
});

describe('cancelling an alert in the cached pages', () => {
  const page = buildAlertPage([buildPriceAlert(), buildPriceAlert({ id: 'other' })]);

  it('takes it out of the Active list and counts one fewer active', () => {
    const next = withoutActiveAlert(page, buildPriceAlert().id, 'active');

    expect(next.items.map((alert) => alert.id)).toEqual(['other']);
    expect(next).toMatchObject({ total: 1, activeCount: 1 });
  });

  it('counts a page that does not list it (a count read), and leaves other lists’ rows alone', () => {
    expect(withoutActiveAlert(buildAlertPage([], { total: 3, activeCount: 3 }), 'x', 'active')).toMatchObject({
      total: 2,
      activeCount: 2,
    });
    const triggered = buildAlertPage([buildTriggeredAlert()], { activeCount: 2 });
    expect(withoutActiveAlert(triggered, 'x', 'triggered')).toMatchObject({ total: 1, activeCount: 1 });
    expect(withoutActiveAlert(triggered, 'x', 'triggered').items).toHaveLength(1);
  });

  it('never counts below zero, and works over every loaded page', () => {
    const empty = buildAlertPage([], { total: 0, activeCount: 0 });
    expect(withoutActiveAlert(empty, 'x', 'active')).toMatchObject({ total: 0, activeCount: 0 });
    const data = withoutActiveAlertPages({ pages: [page, page], pageParams: [1, 2] }, buildPriceAlert().id, 'active');
    expect(data.pages.map((p) => p.items.length)).toEqual([1, 1]);
  });
});
