import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { createCapabilitiesFake } from './capabilitiesFake';
import { buildAutonomy, buildEntry, buildHypothesis, buildLesson, mountRecords } from './learningFixtures';
import { problemResponse, server } from './server';

export interface OwnerMovesFake {
  /** Every body sent to a write route, with the route's name. */
  writes: { route: string; id: string | null; body: Record<string, unknown> }[];
  /** Answers the next write with this problem (then clears itself unless `sticky`). */
  refuse: { status: number; code: string; title: string; sticky?: boolean } | null;
  autonomy: Schemas['AutonomySettingsView'];
  entry: Schemas['ChangelogEntryView'];
  lesson: Schemas['LessonView'];
  hypothesis: Schemas['HypothesisView'];
}

/** The reads of the forms, the five writes and the autonomy PUT as a stateful stand-in; each capability is on unless named in `off`. */
export function mountOwnerMoves(
  over: Partial<OwnerMovesFake> & { off?: ('learning_moves_ui' | 'autonomy_form')[] } = {},
) {
  const { off = [], ...rest } = over;
  const caps = createCapabilitiesFake();
  caps.state.items = caps.state.items.map((i) =>
    i.name === 'learning_moves_ui' || i.name === 'autonomy_form' ? { ...i, enabled: !off.includes(i.name) } : i,
  );
  const state: OwnerMovesFake = {
    writes: [],
    refuse: null,
    autonomy: buildAutonomy(),
    entry: buildEntry({ id: 5 }),
    lesson: buildLesson({ id: 1 }),
    hypothesis: buildHypothesis({ id: 3 }),
    ...rest,
  };
  mountRecords({ lessons: [state.lesson], hypotheses: [state.hypothesis] });
  const answer = async (route: string, id: string | null, request: Request, success: object) => {
    const body = (await request.json()) as Record<string, unknown>;
    state.writes.push({ route, id, body });
    if (state.refuse) {
      const { status, code, title, sticky } = state.refuse;
      if (!sticky) state.refuse = null;
      return problemResponse({ title, code, status }, status);
    }
    if (body.password === 'wrong') {
      return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
    }
    return HttpResponse.json(success);
  };
  server.use(
    ...caps.handlers,
    http.get('/api/changelog/:id', () => HttpResponse.json(state.entry)),
    http.get('/api/autonomy/settings', () => HttpResponse.json(state.autonomy)),
    http.post('/api/lessons/:id/:verb', ({ request, params }) =>
      answer(`lesson.${String(params.verb)}`, String(params.id), request, {
        id: Number(params.id),
        state: params.verb === 'link' ? 'linked' : params.verb === 'close' ? 'closed' : 'dismissed',
        text: `Lesson ${String(params.verb)} done. Nothing was changed.`,
      }),
    ),
    http.post('/api/hypotheses/:id/move', async ({ request, params }) => {
      const clone = request.clone();
      const to = ((await clone.json()) as { to: string }).to;
      return answer('hypothesis.move', String(params.id), request, {
        id: Number(params.id),
        state: to,
        text: `Moved to ${to}.`,
      });
    }),
    http.post('/api/changelog/:id/revert', ({ request, params }) =>
      answer('changelog.revert', String(params.id), request, {
        id: 9,
        subject: state.entry.subject,
        from: state.entry.toState,
        to: state.entry.fromState,
      }),
    ),
    http.put('/api/autonomy/settings', ({ request }) =>
      answer('autonomy.put', null, request, { ...state.autonomy, id: (state.autonomy.id ?? 0) + 1 }),
    ),
  );
  return state;
}
