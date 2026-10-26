import { http, HttpResponse } from 'msw';
import type { components } from '@/shared/api/schema';
import { problemResponse } from './server';

type Item = components['schemas']['CapabilityView'];
type History = components['schemas']['CapabilityHistoryView'];

const names: [string, string][] = [
  ['experiments_ui', 'Experiments from the screen'],
  ['learning_moves_ui', 'Learning moves from the screen'],
  ['autonomy_form', 'Autonomy form'],
  ['exports', 'Exports'],
  ['pwa', 'Install as an app'],
];

export interface CapabilitiesFakeState {
  items: Item[];
  history: History[];
  /** Answers every PUT with this problem. */
  refuse: { status: number; code: string; title: string } | null;
  bodies: Record<string, unknown>[];
}

/** A stateful stand-in for the capability routes: all off by default; a wrong password is "wrong". */
export function createCapabilitiesFake(initial: Partial<CapabilitiesFakeState> = {}) {
  const state: CapabilitiesFakeState = {
    items: names.map(([name, label]) => ({
      name,
      label,
      meaning: `Allows ${label.toLowerCase()} (password asked again for every act).`,
      enabled: false,
      changedAt: null,
      reason: null,
    })),
    history: [],
    refuse: null,
    bodies: [],
    ...initial,
  };
  const view = () => ({ items: state.items, history: state.history });
  const handlers = [
    http.get('/api/capabilities', () => HttpResponse.json(view())),
    http.put('/api/capabilities/:name', async ({ request, params }) => {
      const body = (await request.json()) as { enabled: boolean; reason: string; password?: string };
      state.bodies.push(body);
      if (state.refuse) {
        return problemResponse(
          { title: state.refuse.title, code: state.refuse.code, status: state.refuse.status },
          state.refuse.status,
        );
      }
      if (body.enabled && body.password === 'wrong') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
      }
      const at = '2026-10-26T06:00:00Z';
      state.items = state.items.map((item) =>
        item.name === params.name ? { ...item, enabled: body.enabled, changedAt: at, reason: body.reason } : item,
      );
      state.history = [
        {
          id: state.history.length + 1,
          name: String(params.name),
          enabled: body.enabled,
          actor: 'owner:example@invalid.test',
          reason: body.reason,
          at,
        },
        ...state.history,
      ];
      return HttpResponse.json({ changed: true, capabilities: view() });
    }),
  ];
  return { state, handlers };
}
