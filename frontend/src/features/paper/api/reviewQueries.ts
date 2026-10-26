import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type PaperReview = Schemas['PaperReviewView'];
export type ReviewSection = Schemas['ReviewSection'];
export type Discipline = Schemas['DisciplineView'];
export type DisciplineLine = Schemas['DisciplineLineView'];
export type ExecutionRow = Schemas['ExecutionRowView'];

export const reviewKeys = {
  list: ['paper', 'reviews'] as const,
  now: ['paper', 'review', 'now'] as const,
  stored: (week: string) => ['paper', 'reviews', week] as const,
  discipline: ['paper', 'discipline'] as const,
};

// Every query here is a GET: the Review tab is read only (a review is stored by the paper-review service or `xd paper review --store`).
export const paperReviewListQuery = queryOptions({
  queryKey: reviewKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/reviews', { signal })),
});

/** The current week built now from stored records; stores nothing. */
export const paperReviewNowQuery = queryOptions({
  queryKey: reviewKeys.now,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/review', { signal })),
});

/** A stored review, exactly as it was built. */
export const paperReviewStoredQuery = (week: string) =>
  queryOptions({
    queryKey: reviewKeys.stored(week),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/paper/reviews/{isoWeek}', { params: { path: { isoWeek: week } }, signal })),
  });

/** The discipline report of the whole epoch up to the last closed session. */
export const paperDisciplineQuery = queryOptions({
  queryKey: reviewKeys.discipline,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/discipline', { signal })),
});
