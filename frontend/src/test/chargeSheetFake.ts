import { http, HttpResponse } from 'msw';
import type { ChargeSheetStatusView } from '@/shared/api';
import { problemResponse } from './server';

type Sheet = NonNullable<ChargeSheetStatusView['sheet']>;

export const sampleSheet: Sheet = {
  id: 'dhan-2026',
  label: 'Dhan intraday and delivery',
  source: 'contract note of 2026-10-01',
  checked: 'my October contract notes',
  schedules: 2,
  firstEffective: '2026-01-01',
  lastEffective: '2026-03-03',
  ratesSha256: 'a'.repeat(64),
};

/** What a screen test asks of the fake: a wrong password, a refusal, or the sheet changing under a confirmation. */
export interface ChargeSheetFakeState {
  capabilityOn: boolean;
  sheet: Sheet | null;
  confirmed: { sheetId: string; ratesSha256: string } | null;
  revoked: boolean;
  history: ChargeSheetStatusView['history'];
  wrongPassword: boolean;
  refuse: { status: number; code: string; title: string } | null;
  bodies: { path: string; body: Record<string, unknown> }[];
}

const meaning =
  "Confirming says that this stored sheet holds the rates of your own broker's charges, checked by you. It places no order and changes no stored run or number.";

/** A stateful stand-in for the four routes of the confirmation; it applies the same rules the API states, not more. */
export function createChargeSheetFake(initial: Partial<ChargeSheetFakeState> = {}) {
  const state: ChargeSheetFakeState = {
    capabilityOn: false,
    sheet: sampleSheet,
    confirmed: null,
    revoked: false,
    history: [],
    wrongPassword: false,
    refuse: null,
    bodies: [],
    ...initial,
  };

  const view = (requested: string | null): ChargeSheetStatusView => {
    const shownSheet =
      requested === null || requested === ''
        ? state.sheet?.id === state.confirmed?.sheetId
          ? state.sheet
          : null
        : state.sheet?.id === requested
          ? state.sheet
          : null;
    const stale =
      state.confirmed !== null && state.sheet !== null && state.sheet.ratesSha256 !== state.confirmed.ratesSha256;
    const word =
      state.confirmed === null ? (state.revoked ? 'revoked' : 'not_confirmed') : stale ? 'stale' : 'confirmed';
    return {
      capabilityOn: state.capabilityOn,
      state: word,
      text: {
        not_confirmed: 'Not confirmed: no sheet is confirmed. Every figure is gross.',
        revoked: 'Revoked: no sheet is confirmed. Every figure is gross.',
        stale: `Stale: the stored sheet no longer has the rates you confirmed. Every figure is gross until you confirm it again.`,
        confirmed: `Confirmed: charge sheet '${state.confirmed?.sheetId ?? ''}'. Net figures may be printed.`,
      }[word],
      inForce: state.confirmed
        ? {
            sheetId: state.confirmed.sheetId,
            ratesSha256: state.confirmed.ratesSha256,
            confirmedOn: '2026-10-26',
            actor: 'owner:example@invalid.test',
            at: '2026-10-26T06:00:00Z',
          }
        : null,
      sheet: shownSheet,
      sheetNote: requested && !shownSheet ? `No stored charge sheet is named '${requested}'.` : null,
      meaning,
      history: state.history,
    };
  };

  const push = (kind: string, body: Record<string, unknown>) => {
    state.history = [
      {
        id: state.history.length + 1,
        kind,
        sheetId:
          kind === 'confirm' || kind === 'revoke' ? (state.confirmed?.sheetId ?? (body.sheetId as string)) : null,
        ratesSha256: null,
        actor: 'owner:example@invalid.test',
        reason: String(body.reason),
        at: '2026-10-26T06:00:00Z',
      },
      ...state.history,
    ];
  };

  const act =
    (path: string, apply: (body: Record<string, unknown>) => boolean) =>
    async ({ request }: { request: Request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      state.bodies.push({ path, body });
      if (state.wrongPassword || body.password === 'wrong') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
      }
      if (state.refuse) {
        return problemResponse(
          { title: state.refuse.title, code: state.refuse.code, status: state.refuse.status },
          state.refuse.status,
        );
      }
      const changed = apply(body);
      return HttpResponse.json({ changed, status: view(null) });
    };

  const handlers = [
    http.get('/api/charge-sheet/confirmation', ({ request }) =>
      HttpResponse.json(view(new URL(request.url).searchParams.get('sheetId'))),
    ),
    http.post(
      '/api/charge-sheet/confirmation',
      act('confirm', (body) => {
        state.confirmed = { sheetId: String(body.sheetId), ratesSha256: String(body.ratesSha256) };
        state.revoked = false;
        push('confirm', body);
        return true;
      }),
    ),
    http.post(
      '/api/charge-sheet/confirmation/revoke',
      act('revoke', (body) => {
        push('revoke', body);
        state.confirmed = null;
        state.revoked = true;
        return true;
      }),
    ),
    http.put(
      '/api/charge-sheet/capability',
      act('capability', (body) => {
        state.capabilityOn = body.on === true;
        push(state.capabilityOn ? 'gate_on' : 'gate_off', body);
        return true;
      }),
    ),
  ];
  return { state, handlers };
}
