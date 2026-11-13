import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse } from './server';

type Influence = Schemas['MlInfluenceView'];
type Term = Schemas['MlTermView'];
type Settings = Schemas['MlSettingsView'];
type Proposal = Schemas['RollbackProposalView'];
type Proposals = Schemas['RollbackProposalsView'];

const unavailableReason = { code: 'no_source', text: 'no source for the hold-out score yet' };

/** Today's answer: every evidence term is unavailable, the switch is OFF, the model is not the champion. */
export const buildInfluence = (over: Partial<Influence> = {}): Influence => ({
  modelId: 2,
  role: 'challenger',
  mayInfluence: false,
  terms: [
    { name: 'HoldOutBeatsNoMl', state: 'unavailable', reasons: [unavailableReason] },
    {
      name: 'ForwardShadowOk',
      state: 'unavailable',
      reasons: [{ code: 'no_forward', text: 'no forward shadow candidates are stored' }],
    },
    {
      name: 'NoCalibrationAlarm',
      state: 'unavailable',
      reasons: [{ code: 'no_detector', text: 'the calibration detector has no scored outcomes' }],
    },
    { name: 'SwitchOn', state: 'fails', reasons: [{ code: 'switch_off', text: 'ml.influence.enabled is OFF' }] },
  ],
  reasons: [
    unavailableReason,
    { code: 'no_forward', text: 'no forward shadow candidates are stored' },
    { code: 'no_detector', text: 'the calibration detector has no scored outcomes' },
    { code: 'switch_off', text: 'ml.influence.enabled is OFF' },
    { code: 'not_champion', text: 'the model is not the champion' },
  ],
  holdOut: {
    state: 'kept',
    family: 'v3',
    blockStart: null,
    rows: null,
    probabilityBeatsNoMl: null,
    policyEffectR: null,
  },
  switchOn: false,
  settingHash: 'a'.repeat(64),
  settings: 'hold-out probability at least 0.95; 30 candidates over 4 weeks',
  notes: ['The hold-out, forward and calibration figures have no source yet.'],
  ...over,
});

/** A case where every term holds and the model is the champion. */
export const buildHoldingInfluence = (): Influence =>
  buildInfluence({
    role: 'champion',
    mayInfluence: true,
    terms: ['HoldOutBeatsNoMl', 'ForwardShadowOk', 'NoCalibrationAlarm', 'SwitchOn'].map((name): Term => ({
      name,
      state: 'holds',
      reasons: [],
    })),
    reasons: [],
    holdOut: {
      state: 'used',
      family: 'v3',
      blockStart: '2026-07-01',
      rows: 240,
      probabilityBeatsNoMl: 0.9712,
      policyEffectR: 0.0045,
      gainLow: 0.001,
      gainHigh: 0.03,
      baselineName: 'cell_rate',
      mode: 'auto',
    },
    switchOn: true,
    notes: [],
  });

export const buildSettings = (over: Partial<Settings> = {}): Settings => ({
  influenceEnabled: false,
  influenceSettingId: null,
  autoPromote: false,
  autoPromoteSettingId: null,
  autoRollback: false,
  autoRollbackSettingId: null,
  autoHoldOutScore: true,
  autoHoldOutScoreSettingId: 4,
  ...over,
});

export const buildRollback = (over: Partial<Proposal> = {}): Proposal => ({
  id: 7,
  modelId: 2,
  trigger: 'calibration',
  value: 0.12,
  limit: 0.1,
  text: 'Calibration gap 0.12 against the limit 0.10 on three checks.',
  cells: [],
  previousChampionId: 1,
  previousQualifies: false,
  settingsHash: 'b'.repeat(64),
  proposedOn: '2026-10-24',
  proposedAt: '2026-10-24T10:30:00Z',
  state: 'proposed',
  actor: null,
  decidedAt: null,
  autoSettingId: null,
  ...over,
});

export interface MlFake {
  influence?: Influence;
  proposals?: Proposal[];
  settings?: Settings;
  /** Answers the next write with this problem, then clears itself. */
  refuse?: { status: number; code: string; title: string };
}

export interface MlState {
  proposals: Proposal[];
  settings: Settings;
  refuse: MlFake['refuse'] | null;
  /** Every body sent to `PUT /api/learning/ml-settings`. */
  switchBodies: Record<string, unknown>[];
  /** Every (model id, password) sent to the hold-out score route. */
  scoreBodies: { id: number; password: string | null }[];
  /** Every proposal id sent to the apply route. */
  applied: number[];
  /** The model ids the influence read was asked for. */
  influenceIds: string[];
}

/** The influence read, the rollback read and the two owner-act writes as a stateful stand-in for the server. */
export function mlHandlers(fake: MlFake = {}) {
  const state: MlState = {
    proposals: fake.proposals ?? [],
    settings: fake.settings ?? buildSettings(),
    refuse: fake.refuse ?? null,
    switchBodies: [],
    scoreBodies: [],
    applied: [],
    influenceIds: [],
  };
  const refused = () => {
    if (!state.refuse) return null;
    const { status, code, title } = state.refuse;
    state.refuse = null;
    return problemResponse({ title, code, status }, status);
  };
  const handlers = [
    http.get('/api/learning/models/:id/influence', ({ params }) => {
      state.influenceIds.push(String(params.id));
      return HttpResponse.json(fake.influence ?? buildInfluence({ modelId: Number(params.id) }));
    }),
    http.get('/api/learning/rollback/proposals', () =>
      HttpResponse.json({
        items: state.proposals,
        settings: state.settings,
        statements: ['A rollback never re-promotes the previous champion.'],
      } satisfies Proposals),
    ),
    http.post('/api/learning/rollback/:id/apply', ({ params }) => {
      const refusal = refused();
      if (refusal) return refusal;
      const id = Number(params.id);
      const found = state.proposals.find((p) => p.id === id);
      if (!found) return problemResponse({ title: 'No such proposal.', code: 'learning.rollback_not_found' }, 404);
      if (found.state === 'dismissed') {
        return problemResponse({ title: 'The proposal is dismissed.', code: 'learning.rollback_closed' }, 409);
      }
      const already = found.state === 'applied';
      state.applied.push(id);
      state.proposals = state.proposals.map((p) =>
        p.id === id && !already ? { ...p, state: 'applied', actor: 'owner:me', decidedAt: '2026-10-25T04:30:00Z' } : p,
      );
      return HttpResponse.json({
        proposalId: id,
        modelId: found.modelId,
        trigger: found.trigger,
        state: 'applied',
        alreadyApplied: already,
        demoted: !already,
        previousChampionId: found.previousChampionId,
        previousQualifies: found.previousQualifies,
        actor: 'owner:me',
      });
    }),
    http.post('/api/learning/models/:id/holdout-score', async ({ params, request }) => {
      const body = (await request.json()) as { password?: string | null };
      state.scoreBodies.push({ id: Number(params.id), password: body.password ?? null });
      const refusal = refused();
      if (refusal) return refusal;
      if (typeof body.password !== 'string') {
        return problemResponse({ title: 'Password required.', code: 'auth.step_up_required' }, 403);
      }
      if (body.password === 'wrong') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials' }, 401);
      }
      return HttpResponse.json({
        modelId: Number(params.id),
        family: 'v3',
        blockStart: '2026-10-31',
        mode: 'owner',
        actor: 'owner:me',
        rows: 140,
        days: 30,
        dropped: 2,
        logLossModel: 0.652,
        logLossBaseline: 0.671,
        baselineName: 'cell_rate',
        probabilityBeatsNoMl: 0.97,
        gainLow: 0.001,
        gainHigh: 0.03,
        policyEffectR: 0.04,
        settingHash: 'a'.repeat(64),
        inputHash: 'b'.repeat(64),
        scoredAt: '2026-11-13T10:00:00Z',
      } satisfies Schemas['MlHoldOutScoreView']);
    }),
    http.put('/api/learning/ml-settings', async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      state.switchBodies.push(body);
      const refusal = refused();
      if (refusal) return refusal;
      const turnsOn =
        body.influenceEnabled === true || body.autoPromote === true || typeof body.autoHoldOutScore === 'boolean';
      if (turnsOn && typeof body.password !== 'string') {
        return problemResponse({ title: 'Password required.', code: 'auth.step_up_required' }, 403);
      }
      if (turnsOn && body.password === 'wrong') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials' }, 401);
      }
      const next = { ...state.settings };
      const changed: string[] = [];
      if (typeof body.influenceEnabled === 'boolean') {
        next.influenceEnabled = body.influenceEnabled;
        changed.push('ml.influence.enabled');
      }
      if (typeof body.autoPromote === 'boolean') {
        next.autoPromote = body.autoPromote;
        changed.push('ml.auto_promote');
      }
      if (typeof body.autoRollback === 'boolean') {
        next.autoRollback = body.autoRollback;
        changed.push('ml.auto_rollback');
      }
      if (typeof body.autoHoldOutScore === 'boolean') {
        next.autoHoldOutScore = body.autoHoldOutScore;
        changed.push('ml.holdout.auto_score');
      }
      state.settings = next;
      return HttpResponse.json({
        switches: {
          influence: { id: 1, on: next.influenceEnabled },
          autoPromote: { id: 2, on: next.autoPromote },
          autoRollback: { id: 3, on: next.autoRollback },
          autoHoldOutScore: { id: 4, on: next.autoHoldOutScore },
        },
        changed,
      });
    }),
  ];
  return { handlers, state };
}
