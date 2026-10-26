import { act, screen, waitFor } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { beforeAll, describe, expect, it } from 'vitest';
import { countingUser } from '@/test/interactions';
import { pinClock } from '@/test/pinClock';
import { buildWhatWorks, rankedWhatWorks } from '@/test/reportFixtures';
import { scrollToEnd } from '@/test/intersection';
import { renderReportsFromStart, reportServer } from '@/test/reportScreen';

pinClock('2026-10-25T10:00:00Z');

// P8-17 criteria 1 and 2: the exit question is answered within 4 interactions, and the requests made are the ones the manifest names.
const MAX_INTERACTIONS = 4;
// Spec P8-17 criterion 2: at most 12 requests. The built page (P8-08) made 21 from the cold start to the picked basket: ten panels, then the
// same ten with the basket filter. P8-17f mounts the panels below the first view only when they come near the viewport (the walk does not
// scroll), so the path makes the first-view requests twice and nothing else. The test ratchets that measured count exactly.
const MAX_REQUESTS = 12;
const MEASURED_REQUESTS = 11;
const withinRequests = (count: number) => count <= MAX_REQUESTS;
const basketId = '00000000-0000-4000-8000-000000000401';

interface Manifest {
  version: number;
  answerPath: { operation: string; query?: Record<string, string> }[];
  landing: { operation: string }[];
}
interface Contract {
  paths: Record<string, Record<string, { operationId?: string } | undefined>>;
}

const manifest = JSON.parse(readFileSync('../backend/contracts/what-works-path.json', 'utf8')) as Manifest;
const contract = JSON.parse(readFileSync('../backend/contracts/XDrishti.Api.json', 'utf8')) as Contract;

/** Method and path of an operation, found in the OpenAPI contract. */
function route(operation: string): { method: string; path: string } {
  for (const [path, item] of Object.entries(contract.paths)) {
    for (const [method, op] of Object.entries(item)) {
      if (op?.operationId === operation) return { method: method.toUpperCase(), path };
    }
  }
  throw new Error(`operation ${operation} is not in the contract`);
}

const withinBudget = (count: number) => count <= MAX_INTERACTIONS;

/** The cold start of the owner: click Reports, open Baskets, pick one, close the menu (the multi-select menu hides the page until Escape). */
async function walk(extra = 0) {
  const { user, count } = countingUser();
  renderReportsFromStart();
  await act(() => user.click(screen.getByRole('link', { name: 'Reports' })));
  await act(async () => {
    await user.click(await screen.findByRole('combobox', { name: 'Baskets' }));
  });
  await act(() => user.click(screen.getByRole('option', { name: 'MyChoice' })));
  await act(() => user.keyboard('{Escape}'));
  for (let i = 0; i < extra; i += 1) await act(() => user.tab());
  return count();
}

describe('the What works path (P8-17)', () => {
  // The page is a lazy chunk; loading it inside the nested act of the walk would keep React from resolving the Suspense.
  beforeAll(async () => {
    await import('../components/ReportsPage');
  });

  it('names the operations in the contract and the answer path is runs then what-works with the basket', () => {
    expect(manifest.version).toBe(1);
    expect(manifest.answerPath.map((a) => route(a.operation))).toEqual([
      { method: 'GET', path: '/api/reports/runs' },
      { method: 'GET', path: '/api/reports/what-works' },
    ]);
    for (const landing of manifest.landing) expect(route(landing.operation).method).toBe('GET');
  });

  it('answers with a ranked cell within 4 interactions and only the manifest requests', async () => {
    const requests = reportServer({ whatWorks: rankedWhatWorks() });
    const interactions = await walk();
    await waitFor(() => {
      expect(
        requests.some(
          (u) => u.pathname === route('GetReportWhatWorks').path && u.searchParams.get('basket') === basketId,
        ),
      ).toBe(true);
    });
    expect((await screen.findAllByText(/looks best, with a lower bound of \+0\.05 R/)).length).toBeGreaterThan(0);
    expect(withinBudget(interactions)).toBe(true);
    expect(interactions).toBe(4);
    expect(requests.some((u) => u.pathname === route('ListReportRuns').path)).toBe(true);
    expect(requests.length).toBe(MEASURED_REQUESTS);
    expect(withinRequests(requests.length)).toBe(true);
    const seen = new Map<string, number>();
    for (const u of requests) seen.set(u.pathname + u.search, (seen.get(u.pathname + u.search) ?? 0) + 1);
    for (const n of seen.values()) expect(n).toBeLessThanOrEqual(2);
  });

  it('answers "nothing has enough evidence yet" within the same count', async () => {
    reportServer({ whatWorks: buildWhatWorks() });
    const interactions = await walk();
    expect((await screen.findAllByText(/Nothing has enough evidence yet/)).length).toBeGreaterThan(0);
    expect(withinBudget(interactions)).toBe(true);
  });

  it('keeps the panels below the first view out of the path, and draws them with the basket once they are near', async () => {
    const requests = reportServer({ whatWorks: rankedWhatWorks() });
    await walk();
    await screen.findByRole('region', { name: 'Equity and drawdown' });
    expect(screen.queryByRole('region', { name: 'Monthly total R' })).not.toBeInTheDocument();
    scrollToEnd();
    expect(await screen.findByRole('region', { name: 'Monthly total R' })).toBeInTheDocument();
    await waitFor(() => {
      expect(
        requests.some((u) => u.pathname === '/api/reports/groups' && u.searchParams.get('basket') === basketId),
      ).toBe(true);
    });
    expect(
      requests.filter((u) => u.searchParams.get('lens') === 'month' && !u.searchParams.has('basket')),
    ).toHaveLength(0);
  });

  it('counts a thirteenth request and the budget fails it (positive control)', () => {
    expect(withinRequests(12)).toBe(true);
    expect(withinRequests(13)).toBe(false);
  });

  it('counts a fifth interaction and the budget fails it (positive control)', async () => {
    reportServer({ whatWorks: buildWhatWorks() });
    const interactions = await walk(1);
    expect(interactions).toBe(5);
    expect(withinBudget(interactions)).toBe(false);
  });
});
