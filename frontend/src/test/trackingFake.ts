import { http, HttpResponse } from 'msw';
import type { InstrumentSummary, ResolvedInstrument, ResolveResult } from '@/shared/api';
import {
  buildFacets,
  buildInstrumentDetail,
  sampleInstruments,
  searchInstruments,
  trackedInfo,
  untrackedInfo,
} from './fixtures';
import { problemResponse } from './server';

/**
 * A small stateful stand-in for the instruments API with tracking: the list, the counts, the detail, track / untrack
 * and the look-up of pasted symbols work over one in-memory list, so a test sees what the owner would see after each
 * action. `calls` records the request bodies. Use it with `server.use(...fake.handlers)`.
 */
export function createTrackingFake(initial: readonly InstrumentSummary[] = sampleInstruments) {
  let items: InstrumentSummary[] = [...initial];
  const calls = {
    track: [] as string[][],
    untrack: [] as string[][],
    resolve: [] as string[][],
  };
  const trackedCount = () => items.filter((i) => i.tracked.active).length;
  const update = (ids: readonly string[], change: (item: InstrumentSummary) => InstrumentSummary) => {
    items = items.map((item) => (ids.includes(item.id) ? change(item) : item));
  };

  const matchOf = (term: string): InstrumentSummary[] => {
    const wanted = term.trim().toLowerCase();
    return items.filter(
      (i) =>
        i.symbol.toLowerCase() === wanted ||
        i.symbol.toLowerCase() === `nse:${wanted}` ||
        (i.isin?.toLowerCase() ?? null) === wanted,
    );
  };

  const resolve = (symbols: readonly string[]): ResolveResult => {
    const matched: ResolvedInstrument[] = [];
    const unmatched: ResolveResult['unmatched'] = [];
    const seen = new Set<string>();
    let duplicates = 0;
    for (const term of symbols.map((s) => s.trim()).filter((s) => s !== '')) {
      const found = matchOf(term);
      const hit = found[0];
      if (found.length === 0 || hit === undefined) {
        if (!unmatched.some((u) => u.term === term)) unmatched.push({ term, reason: 'not_found' });
      } else if (seen.has(hit.id)) {
        duplicates += 1;
      } else {
        seen.add(hit.id);
        matched.push({
          term,
          id: hit.id,
          symbol: hit.symbol,
          name: hit.name,
          type: hit.type,
          isin: hit.isin,
          retired: hit.retired,
          tracked: hit.tracked.active,
        });
      }
    }
    return { matched, unmatched, entries: symbols.length, duplicates };
  };

  const handlers = [
    http.get('/api/instruments', ({ request }) =>
      HttpResponse.json(searchInstruments(items, new URL(request.url).searchParams)),
    ),
    http.get('/api/instruments/facets', () => HttpResponse.json(buildFacets(items))),
    http.get('/api/instruments/:id', ({ params }) => {
      const found = items.find((i) => i.id === params.id);
      return found
        ? HttpResponse.json(
            buildInstrumentDetail({ ...found, retiredAt: found.retired ? '2026-10-01T03:15:00Z' : null }),
          )
        : problemResponse({ title: 'Instrument not found.', code: 'instruments.not_found' }, 404);
    }),
    http.post('/api/instruments/tracked', async ({ request }) => {
      const { instrumentIds } = (await request.json()) as { instrumentIds: string[] };
      calls.track.push(instrumentIds);
      const wanted = items.filter((i) => instrumentIds.includes(i.id));
      const retired = wanted.filter((i) => i.retired && !i.tracked.active);
      if (retired.length > 0) {
        return problemResponse(
          {
            title: 'Retired instruments cannot be tracked.',
            code: 'instruments.retired',
            errors: {
              instrumentIds: [
                `Not in Dhan's list any more, so it cannot be tracked: ${retired.map((i) => i.symbol).join(', ')}.`,
              ],
            },
          },
          400,
        );
      }
      const already = wanted.filter((i) => i.tracked.active);
      const again = wanted.filter((i) => !i.tracked.active && i.tracked.deactivatedAt !== null);
      update(instrumentIds, (i) =>
        i.tracked.active ? i : { ...i, tracked: trackedInfo({ dataFrom: i.tracked.dataFrom }) },
      );
      return HttpResponse.json({
        requested: wanted.length,
        added: wanted.length - already.length - again.length,
        reactivated: again.length,
        alreadyTracked: already.length,
        totalTracked: trackedCount(),
      });
    }),
    http.delete('/api/instruments/tracked', async ({ request }) => {
      const { instrumentIds } = (await request.json()) as { instrumentIds: string[] };
      calls.untrack.push(instrumentIds);
      const wanted = items.filter((i) => instrumentIds.includes(i.id));
      const tracked = wanted.filter((i) => i.tracked.active);
      update(instrumentIds, (i) =>
        i.tracked.active
          ? { ...i, tracked: { ...untrackedInfo, deactivatedAt: '2026-10-12T05:00:00Z', since: i.tracked.since } }
          : i,
      );
      return HttpResponse.json({
        requested: wanted.length,
        untracked: tracked.length,
        notTracked: wanted.length - tracked.length,
        totalTracked: trackedCount(),
      });
    }),
    http.post('/api/instruments/tracked/resolve', async ({ request }) => {
      const { symbols } = (await request.json()) as { symbols: string[] };
      calls.resolve.push(symbols);
      return HttpResponse.json(resolve(symbols));
    }),
  ];

  /** Tracks instruments the way a basket does when it adds them; returns how many were not tracked before. */
  const trackQuietly = (ids: readonly string[]): number => {
    const fresh = items.filter((i) => ids.includes(i.id) && !i.tracked.active).length;
    update(ids, (i) => (i.tracked.active ? i : { ...i, tracked: trackedInfo({ dataFrom: i.tracked.dataFrom }) }));
    return fresh;
  };

  return { handlers, calls, items: () => items, trackQuietly };
}

export type TrackingFake = ReturnType<typeof createTrackingFake>;
