import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import type { Basket } from '@/shared/api';
import { versionQuery, versionsQuery } from '../api/systemQueries';

export interface Score {
  rank: number;
  score: number;
}

/**
 * The newest version of a derived basket and its scores (rank, score) by instrument: the "why" of the members grid and
 * the as-of line. Reads nothing for any other basket (the list carries no versions).
 */
export function useLatestVersion(basket: Basket) {
  const derived = basket.kind === 'derived';
  const versions = useQuery({ ...versionsQuery(basket.id), enabled: derived });
  const latest = versions.data?.items[0] ?? null;
  const detail = useQuery({ ...versionQuery(basket.id, latest?.versionId ?? ''), enabled: derived && latest !== null });
  const scores = useMemo(
    () =>
      new Map<string, Score>(
        (detail.data?.scores ?? []).map((s) => [s.instrumentId, { rank: s.rank, score: s.score }]),
      ),
    [detail.data],
  );
  const definition = versions.data?.definition ?? null;
  return { derived, versions, latest, detail, scores, definition };
}
