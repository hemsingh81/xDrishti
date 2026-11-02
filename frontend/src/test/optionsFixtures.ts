import type { Schemas } from '@/shared/api';

/** A recorder switch view that is OFF and never changed (the option chain recorder; the backfill adds its progress). */
export const buildOptionRecorderView = (over: Partial<Schemas['OptionChainRecorderView']> = {}) => ({
  enabled: false,
  switchId: null,
  state: null,
  lastSeenAt: null,
  notRunningBecause: ['Your switch is OFF.'],
  notice: 'Off until the deployment flag, your switch (with your password) and the service are all on.',
  history: [],
  ...over,
});

export const buildBackfillView = (
  over: Partial<Schemas['OptionBackfillView']> = {},
): Schemas['OptionBackfillView'] => ({
  ...buildOptionRecorderView(),
  probeAccepted: null,
  progress: null,
  ...over,
});

export const buildCoverageView = (
  over: Partial<Schemas['OptionCoverageView']> = {},
): Schemas['OptionCoverageView'] => ({
  underlying: 'NSE:NIFTY',
  sessions: [],
  usableChainSessions: 0,
  truncated: false,
  ...over,
});
