import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

// CF-11 part 2b: the Readiness screen. Every query here is a read; the one write (the enforce switch) is enforceApi.ts.
export type ScorecardList = Schemas['ScorecardListView'];
export type ScorecardSummary = Schemas['ScorecardSummaryView'];
export type SystemScorecard = Schemas['SystemScorecardView'];
export type Scorecard = Schemas['ScorecardView'];
export type ScorecardRow = Schemas['ScorecardRowView'];
export type SubScore = Schemas['SubScoreView'];
export type ReadinessGate = Schemas['ReadinessGateView'];
export type ScorecardPoint = Schemas['ScorecardPointView'];
export type ReadinessSettings = Schemas['ReadinessSettingsView'];

export const readinessKeys = {
  all: ['readiness'] as const,
  list: ['readiness', 'scorecards'] as const,
  card: (strategy: string, version: number) => ['readiness', 'scorecard', strategy, version] as const,
  settings: ['readiness', 'settings'] as const,
};

/** The system card and one line per strategy at its latest Lab version, weakest first. */
export const scorecardsQuery = queryOptions({
  queryKey: readinessKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/confidence/scorecards', { signal })),
});

/** One strategy version's scorecard. A 404 or 400 is the server's answer, shown as sent: no retry. */
export const scorecardQuery = (strategy: string, version: number) =>
  queryOptions({
    queryKey: readinessKeys.card(strategy, version),
    retry: false,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/confidence/scorecards/{strategyId}/versions/{version}', {
          params: { path: { strategyId: strategy, version } },
          signal,
        }),
      ),
  });

/** The enforce switch (OFF when no row is stored) and the text and hash of the thresholds. */
export const readinessSettingsQuery = queryOptions({
  queryKey: readinessKeys.settings,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/confidence/readiness-settings', { signal })),
});
