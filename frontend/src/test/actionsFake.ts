import { http, HttpResponse } from 'msw';
import type {
  ActionsImportResult,
  CorporateAction,
  CorporateActionInput,
  CorporateActionUpdate,
  DataCoverageRow,
  InstrumentBars,
} from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { actionId, buildCorporateAction, searchActions } from './actionFixtures';
import { problemResponse } from './server';

export interface ActionsFakeOptions {
  actions?: readonly CorporateAction[];
  /** What "Detect now" finds: stored as suggestions unless the instrument and ex-date have an action already. */
  found?: readonly CorporateAction[];
  /**
   * The day the loaded CSV files of an instrument reach, by instrument id (yyyy-MM-dd; null: none loaded). Instruments
   * not named here reach the last bar of their coverage row when the row has only CSV bars.
   */
  csvThrough?: Readonly<Record<string, string | null>>;
}

const decidedAt = '2026-10-12T16:40:00+05:30';
const round4 = (value: number) => Math.round(value * 10_000) / 10_000;
const isDay = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value);
const whole = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 100_000;
const queryFlag = (request: Request) => new URL(request.url).searchParams.get('acknowledgeCsvAdjusted') === 'true';
const factorOf = (kind: string, n: number, d: number) => (kind === 'split' ? n / d : d / (n + d));

interface Fields {
  exDate: unknown;
  kind: unknown;
  numerator: unknown;
  denominator: unknown;
  note: unknown;
}

/** The API's checks of an action's fields, by field (`data.invalid`). */
function check(fields: Fields): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const range = 'Enter a whole number from 1 to 100000.';
  if (!isDay(fields.exDate)) errors.exDate = ['Enter the ex-date as yyyy-MM-dd.'];
  if (fields.kind !== 'split' && fields.kind !== 'bonus') errors.kind = ['Choose split or bonus.'];
  if (!whole(fields.numerator)) errors.numerator = [range];
  if (!whole(fields.denominator)) errors.denominator = [range];
  if (fields.kind === 'split' && whole(fields.numerator) && fields.numerator === fields.denominator) {
    errors.denominator = ['A 1:1 split changes nothing, so it is refused.'];
  }
  if (typeof fields.note === 'string' && fields.note.length > 200) {
    errors.note = ['The note can have at most 200 characters.'];
  }
  return errors;
}

/**
 * A stateful stand-in for the corporate actions API (list with status filter, `pending` and each row's
 * `csvAdjustedThrough`, add, change, ignore, detect, CSV import with preview) and for the adjusted read of the bars.
 * It keeps the API's guards: an ex-date on or before the day an instrument's CSV files reach is a 409
 * `data.csv_already_adjusted` unless `acknowledgeCsvAdjusted` is in the body (add, change) or the query (also import);
 * an applied action of the other kind on the same day is a 409 `data.corporate_action_conflicts_same_day`.
 * `calls` records what was asked; `failures.list` makes the list fail.
 */
export function createActionsFake(options: ActionsFakeOptions, coverage: () => readonly DataCoverageRow[]) {
  let items: CorporateAction[] = [...(options.actions ?? [])];
  let next = 50;
  const failures = { list: false };
  const calls = {
    list: [] as string[],
    add: [] as CorporateActionInput[],
    update: [] as { id: string; body: CorporateActionUpdate }[],
    ignore: [] as string[],
    detect: 0,
    imports: [] as { preview: boolean; text: string; contentType: string | null; acknowledge: boolean }[],
    /** Requests the CSV guard refused (409 `data.csv_already_adjusted`). */
    guarded: [] as { symbol: string; exDate: string }[],
  };
  const rowOf = (instrumentId: string) => coverage().find((r) => r.id === instrumentId);
  /** What the API calls `csvAdjustedThrough`: where the instrument's loaded CSV files end (India date). */
  const throughOf = (instrumentId: string): string | null => {
    const given = options.csvThrough?.[instrumentId];
    if (given !== undefined) return given;
    const row = rowOf(instrumentId);
    return row?.source === 'csv' && row.lastBar ? istDayIso(Date.parse(row.lastBar)) : null;
  };
  const view = (action: CorporateAction): CorporateAction => ({
    ...action,
    csvAdjustedThrough: throughOf(action.instrumentId),
  });
  /** The 409 the API answers for an ex-date inside adjusted CSV history, unless the owner agreed (body or query). */
  const guard = (instrumentId: string, exDate: string, acknowledged: boolean, symbol: string) => {
    const through = throughOf(instrumentId);
    if (acknowledged || through === null || exDate > through) return null;
    calls.guarded.push({ symbol, exDate });
    return problemResponse(
      {
        title: `The loaded CSV files are already adjusted for corporate actions up to ${through}. Applying an action with an ex-date on or before it would adjust those prices twice. Confirm that you want this, or leave it out.`,
        code: 'data.csv_already_adjusted',
        adjustsCsvTwice: 1,
        instruments: 1,
      },
      409,
    );
  };
  const sameDay = () =>
    problemResponse(
      {
        title:
          'The instrument already has an applied split or bonus on that ex-date. Ignore it first to apply another kind on the same day.',
        code: 'data.corporate_action_conflicts_same_day',
      },
      409,
    );
  /** An applied action of the other kind on the same instrument and ex-date (the new one would be applied too). */
  const clashes = (a: Pick<CorporateAction, 'instrumentId' | 'exDate' | 'kind'>, ignoreId?: string) =>
    items.some(
      (x) =>
        x.id !== ignoreId &&
        x.status === 'applied' &&
        x.instrumentId === a.instrumentId &&
        x.exDate === a.exDate &&
        x.kind !== a.kind,
    );
  const invalid = (errors: Record<string, string[]>) =>
    problemResponse({ title: 'The corporate action is not valid.', code: 'data.invalid', errors }, 400);
  const missing = () =>
    problemResponse({ title: 'That action does not exist.', code: 'data.corporate_action_not_found' }, 404);
  const exists = () =>
    problemResponse({ title: 'The action exists already.', code: 'data.corporate_action_exists' }, 409);
  const twin = (a: Pick<CorporateAction, 'instrumentId' | 'exDate' | 'kind'>, ignoreId?: string) =>
    items.some(
      (x) => x.id !== ignoreId && x.instrumentId === a.instrumentId && x.exDate === a.exDate && x.kind === a.kind,
    );

  const replace = (action: CorporateAction) => {
    items = items.map((x) => (x.id === action.id ? action : x));
    return action;
  };

  /** The symbol of an imported line: with or without the exchange prefix. */
  const resolve = (symbol: string) => coverage().find((r) => r.symbol === symbol || r.symbol === `NSE:${symbol}`);

  /** The result of an import, or null when the guard refuses it (lines inside CSV history, no consent). */
  function importText(text: string, preview: boolean, acknowledged: boolean): ActionsImportResult | null {
    const lines = text.split(/\r?\n/).filter((line) => line.trim() !== '');
    const body = lines[0]?.toLowerCase().startsWith('symbol') ? lines.slice(1) : lines;
    const offset = body.length === lines.length ? 1 : 2;
    const unknown = new Set<string>();
    const errors: { line: number; message: string }[] = [];
    const accepted: CorporateAction[] = [];
    const warnings: { line: number; symbol: string; exDate: string; csvAdjustedThrough: string; message: string }[] =
      [];
    let adjustsTwice = 0;
    let duplicates = 0;
    body.forEach((line, index) => {
      const [symbol = '', exDate = '', kind = '', n = '', d = '', note = ''] = line.split(',').map((x) => x.trim());
      const row = resolve(symbol);
      const numerator = Number(n);
      const denominator = Number(d);
      const fail = (message: string) => errors.push({ line: index + offset, message });
      if (!isDay(exDate)) return fail('The ex-date must be yyyy-MM-dd.');
      if (kind !== 'split' && kind !== 'bonus') return fail('The kind must be split or bonus.');
      if (!whole(numerator) || !whole(denominator)) return fail('The ratio must be whole numbers.');
      if (kind === 'split' && numerator === denominator) return fail('A 1:1 split changes nothing.');
      if (!row) {
        unknown.add(symbol);
        return;
      }
      const action = buildCorporateAction({
        id: actionId(next + accepted.length),
        instrumentId: row.id,
        symbol: row.symbol,
        name: row.name,
        exDate,
        kind,
        numerator,
        denominator,
        status: 'applied',
        source: 'csv',
        note: note || null,
        decidedAt,
        decidedBy: 'owner@example.com',
      });
      if (
        twin(action) ||
        accepted.some((x) => x.instrumentId === action.instrumentId && x.exDate === exDate && x.kind === kind)
      ) {
        duplicates += 1;
        return;
      }
      const pair = [...accepted, ...items].some(
        (x) =>
          x.instrumentId === action.instrumentId && x.exDate === exDate && x.kind !== kind && x.status === 'applied',
      );
      if (pair) {
        return fail(
          'kind: The instrument has a split or bonus of the other kind on this ex-date (applied, or on an earlier line). Ignore it first.',
        );
      }
      const through = throughOf(action.instrumentId);
      if (through !== null && exDate <= through) {
        adjustsTwice += 1;
        if (warnings.length < 50) {
          warnings.push({
            line: index + offset,
            symbol: action.symbol,
            exDate,
            csvAdjustedThrough: through,
            message: `The loaded CSV files of ${action.symbol} are adjusted through ${through}.`,
          });
        }
      }
      accepted.push(action);
    });
    if (!preview && adjustsTwice > 0 && !acknowledged) {
      calls.guarded.push(...warnings.map((w) => ({ symbol: w.symbol, exDate: w.exDate })));
      return null;
    }
    if (!preview) {
      items = [...items, ...accepted];
      next += accepted.length;
    }
    return {
      preview,
      lines: body.length,
      importable: preview ? accepted.length : 0,
      imported: preview ? 0 : accepted.length,
      duplicates,
      invalid: errors.length,
      unknownSymbols: [...unknown],
      errors,
      adjustsCsvTwice: adjustsTwice,
      warnings,
    };
  }

  const handlers = [
    http.get('/api/data/corporate-actions', ({ request }) => {
      const url = new URL(request.url);
      calls.list.push(url.search);
      if (failures.list)
        return problemResponse({ title: 'The data service is not available.', code: 'data.down' }, 500);
      return HttpResponse.json(searchActions(items.map(view), url.searchParams));
    }),
    http.post('/api/data/corporate-actions', async ({ request }) => {
      const body = (await request.json()) as CorporateActionInput;
      calls.add.push(body);
      const acknowledged = body.acknowledgeCsvAdjusted === true || queryFlag(request);
      const errors = check(body);
      if (!body.instrumentId) errors.instrumentId = ['Choose an instrument.'];
      if (Object.keys(errors).length > 0) return invalid(errors);
      const row = rowOf(body.instrumentId ?? '');
      if (!row) {
        return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
      }
      const action = buildCorporateAction({
        id: actionId(next++),
        instrumentId: row.id,
        symbol: row.symbol,
        name: row.name,
        exDate: body.exDate ?? '',
        kind: body.kind ?? 'split',
        numerator: body.numerator ?? 1,
        denominator: body.denominator ?? 1,
        status: 'applied',
        source: 'manual',
        note: body.note,
        decidedAt,
        decidedBy: 'owner@example.com',
      });
      if (twin(action)) return exists();
      if (clashes(action)) return sameDay();
      const refused = guard(row.id, action.exDate, acknowledged, row.symbol);
      if (refused) return refused;
      items = [...items, action];
      return HttpResponse.json(view(action), { status: 201 });
    }),
    http.post('/api/data/corporate-actions/detect', () => {
      calls.detect += 1;
      const fresh = (options.found ?? []).filter((f) => !twin(f));
      items = [...items, ...fresh.map((f) => ({ ...f, status: 'suggested' }))];
      return HttpResponse.json({
        instruments: coverage().filter((r) => r.bars > 0).length,
        candidates: (options.found ?? []).length,
        suggested: fresh.length,
        pending: items.filter((x) => x.status === 'suggested').length,
      });
    }),
    http.post('/api/data/corporate-actions/import', async ({ request }) => {
      const preview = new URL(request.url).searchParams.get('preview') === 'true';
      const acknowledged = queryFlag(request);
      const text = await request.text();
      calls.imports.push({
        preview,
        text,
        contentType: request.headers.get('content-type'),
        acknowledge: acknowledged,
      });
      if (text.trim() === '') return invalid({ text: ['The text is empty.'] });
      const result = importText(text, preview, acknowledged);
      if (result) return HttpResponse.json(result);
      return problemResponse(
        {
          title:
            'The loaded CSV files are already adjusted for corporate actions. Applying these actions would adjust those prices twice. Confirm that you want this, or leave it out.',
          code: 'data.csv_already_adjusted',
          adjustsCsvTwice: 1,
          instruments: 1,
        },
        409,
      );
    }),
    http.put('/api/data/corporate-actions/:id', async ({ params, request }) => {
      const id = String(params.id);
      const body = (await request.json()) as CorporateActionUpdate;
      calls.update.push({ id, body });
      const acknowledged = body.acknowledgeCsvAdjusted === true || queryFlag(request);
      const current = items.find((x) => x.id === id);
      if (!current) return missing();
      const merged = {
        exDate: body.exDate ?? current.exDate,
        kind: body.kind ?? current.kind,
        numerator: body.numerator ?? current.numerator,
        denominator: body.denominator ?? current.denominator,
        note: body.note ?? current.note,
      };
      const errors = check(merged);
      if (Object.keys(errors).length > 0) return invalid(errors);
      const changed = {
        ...current,
        ...merged,
        ratio: `${String(merged.numerator)}:${String(merged.denominator)}`,
        factor: factorOf(merged.kind, merged.numerator, merged.denominator),
        note: merged.note === '' ? null : merged.note,
      };
      if (twin(changed, id)) return exists();
      if (clashes(changed, id)) return sameDay();
      // Only a change that makes the action count (it was not applied, or its date, kind or ratio change) is guarded.
      const counts =
        current.status !== 'applied' ||
        changed.exDate !== current.exDate ||
        changed.kind !== current.kind ||
        changed.numerator !== current.numerator ||
        changed.denominator !== current.denominator;
      const refused = counts ? guard(current.instrumentId, changed.exDate, acknowledged, current.symbol) : null;
      if (refused) return refused;
      return HttpResponse.json(
        view(replace({ ...changed, status: 'applied', decidedAt, decidedBy: 'owner@example.com' })),
      );
    }),
    http.post('/api/data/corporate-actions/:id/ignore', ({ params }) => {
      const id = String(params.id);
      calls.ignore.push(id);
      const current = items.find((x) => x.id === id);
      if (!current) return missing();
      return HttpResponse.json(
        view(replace({ ...current, status: 'ignored', decidedAt, decidedBy: 'owner@example.com' })),
      );
    }),
  ];

  /** The bars with prices × the factor of the applied actions after each bar's India date, volume ÷ it (adjusted=true). */
  function adjust(bars: InstrumentBars): InstrumentBars {
    const applied = items
      .filter((x) => x.status === 'applied' && x.instrumentId === bars.instrumentId)
      .sort((a, b) => a.exDate.localeCompare(b.exDate));
    const factorAt = (day: string) => applied.filter((x) => x.exDate > day).reduce((f, x) => f * x.factor, 1);
    const oldest = bars.items.reduce<string | null>((min, bar) => {
      const day = istDayIso(Date.parse(bar.ts));
      return min === null || day < min ? day : min;
    }, null);
    const affecting = applied.filter((x) => oldest !== null && x.exDate > oldest);
    return {
      ...bars,
      items: bars.items.map((bar) => {
        const f = factorAt(istDayIso(Date.parse(bar.ts)));
        return f === 1
          ? bar
          : {
              ...bar,
              open: round4(bar.open * f),
              high: round4(bar.high * f),
              low: round4(bar.low * f),
              close: round4(bar.close * f),
              volume: Math.round(bar.volume / f),
            };
      }),
      adjustment: {
        applied: affecting.length,
        factor: oldest === null ? 1 : factorAt(oldest),
        actions: affecting.map((x) => ({ exDate: x.exDate, kind: x.kind, ratio: x.ratio, factor: x.factor })),
      },
    };
  }

  return {
    handlers,
    calls,
    failures,
    adjust,
    /** Actions appear on the server by themselves (a run's detection suggested them). */
    seed: (...added: CorporateAction[]) => {
      items = [...items, ...added];
    },
    /** The actions as the next read would answer them. */
    get items() {
      return [...items];
    },
    /** Suggestions waiting. */
    get pending() {
      return items.filter((x) => x.status === 'suggested').length;
    },
  };
}

export type ActionsFake = ReturnType<typeof createActionsFake>;
