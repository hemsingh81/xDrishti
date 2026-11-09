import type { components } from '@/shared/api/schema';

type Essential = components['schemas']['EssentialSettingView'];
type Learned = components['schemas']['LearnedChangeView'];
type LearnedView = components['schemas']['LearnedChangesView'];

export const buildEssential = (over: Partial<Essential> & Pick<Essential, 'key' | 'label'>): Essential => ({
  value: null,
  default: null,
  isDefault: true,
  changedBy: null,
  changedAt: null,
  canReset: false,
  resetNote: null,
  ...over,
});

/** The five Essential rows as the API answers them with nothing changed. */
export const buildEssentials = (): { items: Essential[] } => ({
  items: [
    buildEssential({ key: 'auto.enabled', label: 'Auto', default: 'false' }),
    buildEssential({ key: 'suggest.scope', label: 'What to suggest', default: 'eligible' }),
    buildEssential({
      key: 'risk.profile',
      label: 'Risk profile',
      resetNote: 'Risk profile has no single default; pick a profile.',
    }),
    buildEssential({ key: 'chargesheet.gate', label: 'Charge sheet', value: 'false', default: 'false' }),
    buildEssential({ key: 'notices.failure', label: 'Failure notices', value: 'true', default: 'true' }),
  ],
});

export const buildLearnedChange = (over: Partial<Learned> = {}): Learned => ({
  id: 1,
  key: 'Confidence:Ladder:MinProbability',
  label: 'Confidence bar',
  oldValue: '0.55',
  newValue: '0.6',
  direction: 'tighten',
  source: 'learning',
  state: 'proposed',
  decidedBy: null,
  decidedAt: null,
  undoneBy: null,
  undoneAt: null,
  evidenceRef: 'ev-1',
  snapshotHash: 'abc123',
  undoOf: null,
  createdAt: '2026-11-08T05:00:00Z',
  approveNeedsPassword: false,
  canUndo: false,
  undoNeedsPassword: false,
  ...over,
});

export const buildLearned = (items: Learned[] = []): LearnedView => ({
  autoOn: false,
  weeklyCap: 3,
  autoAppliedThisWeek: 0,
  takesEffect: 'at the next weekly run',
  allowlist: [{ key: 'Confidence:Ladder:MinProbability', label: 'Confidence bar', safer: 'higher', range: null }],
  items,
});
