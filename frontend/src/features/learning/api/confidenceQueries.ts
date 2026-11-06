import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

// CF-12 12c-2: the Decay and Loop tabs. All three are reads; deciding a proposal is not part of this feature.
export type DecayReport = Schemas['DecayReportView'];
export type DecaySeries = Schemas['DecaySeriesView'];
export type DecayWindow = Schemas['DecayWindowView'];
export type DecayQuarter = Schemas['DecayQuarterView'];
export type DecayKill = Schemas['DecayKillView'];
export type LoopReport = Schemas['LoopReportView'];
export type LoopArm = Schemas['LoopArmView'];
export type KillProposals = Schemas['KillProposalsView'];
export type KillProposal = Schemas['KillProposalView'];
export type KillSettings = Schemas['KillSettingsView'];

/** The most proposals one request returns here (the server accepts 1 to 200). */
export const proposalLimit = 50;

export const confidenceKeys = {
  decay: (strategy: string) => ['learning', 'decay', strategy] as const,
  loop: ['learning', 'loop'] as const,
  killProposals: (strategy: string) => ['learning', 'kill-proposals', strategy] as const,
};

/** The decay monitor of one strategy. A 404 means the strategy has no series: an answer, so there is no retry. */
export const decayQuery = (strategy: string) =>
  queryOptions({
    queryKey: confidenceKeys.decay(strategy),
    retry: false,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/confidence/decay', { params: { query: { strategy } }, signal })),
  });

/** Is the learning loop helping? Today most sections answer `unavailable` with a reason. */
export const loopQuery = queryOptions({
  queryKey: confidenceKeys.loop,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/confidence/loop', { signal })),
});

/** The kill proposals of one strategy, newest first, with the automatic setting beside them. */
export const killProposalsQuery = (strategy: string) =>
  queryOptions({
    queryKey: confidenceKeys.killProposals(strategy),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/confidence/kill-proposals', {
          params: { query: { strategy, limit: proposalLimit } },
          signal,
        }),
      ),
  });
