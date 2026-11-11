import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { BasketProfilePage } from '@/features/basket-profile';
import type { Schemas } from '@/shared/api';
import { basketIds } from '@/test/fixtures';
import { buildProfile, buildRichProfile, type Profile } from '@/test/profileFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { buildGroup, buildGroups, nonValue, value } from '@/test/reportFixtures';
import { problemResponse, server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const id = basketIds.myChoice;

function show(profile: Profile, search = '') {
  server.use(
    http.get('/api/reports/baskets/:id/profile', () => HttpResponse.json(profile)),
    http.get('/api/reports/groups', ({ request }) => {
      const lens = new URL(request.url).searchParams.get('lens');
      return lens === 'regime'
        ? problemResponse(
            { title: 'Regime is unavailable: no market context is stored.', code: 'reports.dimension_unavailable' },
            422,
          )
        : HttpResponse.json(buildGroups('basket', [buildGroup({ key: `basket=${id}`, n: 120 })]));
    }),
  );
  return renderRoutes([{ path: '/baskets/:basketId/profile', element: <BasketProfilePage /> }], {
    initialPath: `/baskets/${id}/profile${search}`,
  });
}

const header = async () => within(await screen.findByRole('banner', { name: 'Basket profile header' }));

type Metric = Schemas['MetricView'];

/** Every figure of the view set to one state: the screen must print a word for each and no digit of a number. */
function allAs(node: unknown, state: Metric['state']): unknown {
  if (Array.isArray(node)) return node.map((n) => allAs(n, state));
  if (node && typeof node === 'object') {
    const o = node as Record<string, unknown>;
    if (typeof o.state === 'string' && 'number' in o && 'reason' in o) {
      return {
        state,
        number: null,
        reason: state === 'insufficient' ? 'n = 3, at least 5 needed' : 'Not available here',
      };
    }
    return Object.fromEntries(Object.entries(o).map(([k, v]) => [k, allAs(v, state)]));
  }
  return node;
}

describe('the header', () => {
  it('prints the single snapshot basis line in the header, not in a tooltip', async () => {
    show(buildProfile());

    const h = await header();

    expect(h.getByTestId('membership-basis')).toHaveTextContent(
      /Members as of 25 Sept? 2026, not as of each trading day; survivorship applies \(ISS-22\)\./,
    );
  });

  it('says dated with the first date when the membership has a history', async () => {
    show(buildProfile({ membershipBasis: 'dated', membershipFrom: '2026-06-01' }));

    const h = await header();

    expect(h.getByTestId('membership-basis')).toHaveTextContent('Membership is dated from 1 Jun 2026');
    expect(h.getByTestId('membership-basis')).not.toHaveTextContent('survivorship applies');
  });

  it('counts the instruments and names the ones left out with their reason on request', async () => {
    const user = userEvent.setup();
    show(buildRichProfile());

    const h = await header();
    expect(h.getByText('7 of 8 instruments counted')).toBeInTheDocument();
    await user.click(h.getByRole('button', { name: 'Show the ones left out' }));

    expect(
      within(h.getByRole('list', { name: 'Instruments left out' })).getByText('ZZZ: starts late'),
    ).toBeInTheDocument();
  });

  it('gives the window, its source and the index in words and 44 px touch targets', async () => {
    show(buildProfile());

    const h = await header();

    expect(
      h.getByText(
        /Window 14 Sept? 2026 to 25 Sept? 2026 \(the dates you chose\)\. Index: NIFTY \(the benchmark you track\)\./,
      ),
    ).toBeInTheDocument();
    expect(h.getByRole('link', { name: 'Compare baskets' })).toHaveStyle({ minHeight: '44px' });
  });

  it('shows the basket name as text, never as markup', async () => {
    show(buildProfile({ basket: { id, name: '<img src=x alt="boom">', kind: 'manual', paramsHash: null } }));

    expect(await screen.findByRole('heading', { level: 1, name: '<img src=x alt="boom">' })).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'boom' })).not.toBeInTheDocument();
  });
});

describe('the states of the screen', () => {
  it('shows loading while the profile is read', async () => {
    server.use(http.get('/api/reports/baskets/:id/profile', async () => new Promise(() => undefined)));
    renderRoutes([{ path: '/baskets/:basketId/profile', element: <BasketProfilePage /> }], {
      initialPath: `/baskets/${id}/profile`,
    });

    expect(await screen.findByRole('status')).toBeInTheDocument();
  });

  it('shows the error and retries', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      http.get('/api/reports/baskets/:id/profile', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'The profile failed.', code: 'reports.invalid' }, 422)
          : HttpResponse.json(buildProfile());
      }),
    );
    renderRoutes([{ path: '/baskets/:basketId/profile', element: <BasketProfilePage /> }], {
      initialPath: `/baskets/${id}/profile`,
    });

    expect(await screen.findByText('The profile failed.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'MyChoice' })).toBeInTheDocument();
  });

  it('says there is nothing to profile for a basket with no members', async () => {
    show(buildProfile({ members: { total: 0, counted: 0, excluded: [] } }));

    expect(await screen.findByText(/no members in the window/)).toBeInTheDocument();
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
  });

  it('keeps the tab in the address and opens it from there', async () => {
    const user = userEvent.setup();
    const view = show(buildRichProfile(), '?tab=inside');

    expect(await screen.findByRole('heading', { name: 'Inside the basket' })).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Performance and risk' }));

    await waitFor(() => {
      expect(view.router.state.location.search).toBe('');
    });
    expect(await screen.findByRole('heading', { name: 'Risk' })).toBeInTheDocument();
  });
});

describe('every figure keeps to its state', () => {
  it.each(['insufficient', 'unavailable', 'not_evaluable', 'empty'] as const)(
    'prints a %s figure as words and shows no digit of a number on any tab',
    async (state) => {
      const user = userEvent.setup();
      show(allAs(buildRichProfile(), state) as Profile);
      await header();

      const seen: string[] = [];
      for (const tab of ['Performance and risk', 'Inside the basket', 'Regimes and capacity']) {
        await user.click(screen.getByRole('tab', { name: tab }));
        await screen.findByRole('tabpanel');
        for (const el of document.querySelectorAll('[data-state]')) {
          const kind = el.getAttribute('data-state');
          if (kind === state) seen.push(el.textContent.replace(/n [\d,]+ of at least [\d,]+/, ''));
        }
      }

      expect(seen.length).toBeGreaterThan(10);
      for (const text of seen) expect(text).not.toMatch(/\d/);
    },
  );

  it('prints the n and the minimum of an insufficient breadth and draws no line for it', async () => {
    const user = userEvent.setup();
    show(buildProfile());
    await header();

    await user.click(screen.getByRole('tab', { name: 'Inside the basket' }));
    const table = await screen.findByRole('table', { name: 'Breadth and dispersion' });

    expect(within(table).getAllByText('n 3 of at least 5').length).toBeGreaterThan(0);
    expect(screen.queryByLabelText('Share of members above their 20-day average')).not.toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Correlation matrix' })).not.toBeInTheDocument();
  });

  it('says the index has no bars and where it can be tracked, with no chart line for it', async () => {
    const noIndex = nonValue(
      'unavailable',
      'NIFTY has no daily bars; the owner can track it in Portfolio (this read tracks nothing)',
    );
    show(
      buildProfile({
        index: { id: null, symbol: null, isDefault: true },
        series: {
          ...buildProfile().series,
          index: buildProfile().series.index.map(() => null),
          relativeStrength: buildProfile().series.index.map(() => null),
        },
        indexDifference: { basket: value(4), index: noIndex, difference: noIndex },
      }),
    );

    expect(await screen.findAllByText(/track it in Portfolio/)).not.toHaveLength(0);
    expect(screen.queryByLabelText('Basket minus index, in index points')).not.toBeInTheDocument();
  });

  it('keeps the difference to the index out of the view of any trading result and never calls it alpha', async () => {
    const user = userEvent.setup();
    show(buildRichProfile());
    await header();

    const overview = document.body.textContent;
    expect(overview).toContain('Difference to the index (passive)');
    expect(overview).not.toMatch(/expectancy|win rate|profit factor/i);
    expect(screen.queryAllByText(/\balpha\b|excess|outperform/i)).toEqual([]);
    expect(overview).toContain('Intercept vs index');

    await user.click(screen.getByRole('tab', { name: 'Strategies' }));
    await screen.findByRole('table', { name: 'Strategy results for this basket' });

    expect(document.body.textContent).not.toContain('Difference to the index (passive)');
  });
});

describe('the panels', () => {
  it('draws the correlation grid with a keyboard path and states a pair without enough sessions', async () => {
    const user = userEvent.setup();
    show(buildRichProfile(), '?tab=inside');
    const grid = await screen.findByRole('grid', { name: 'Correlation matrix' });
    const cells = within(grid).getAllByRole('button');

    cells[0]?.focus();
    await user.keyboard('{ArrowRight}');

    expect(cells[1]).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(await screen.findByRole('status')).toHaveTextContent(/AAA and BBB: correlation/);
    await user.click(within(grid).getByRole('button', { name: /AAA with DDD/ }));
    expect(screen.getByRole('status')).toHaveTextContent('AAA and DDD: not enough common sessions.');
  });

  it('says it shows the 40 most liquid when the basket is larger than the matrix', async () => {
    const rich = buildRichProfile();
    show(
      buildRichProfile({
        correlation: {
          ...rich.correlation,
          matrix: { symbols: ['AAA', 'BBB', 'CCC', 'DDD'], total: 120, rho: rich.correlation.matrix?.rho ?? [] },
        },
      }),
      '?tab=inside',
    );

    expect(
      await screen.findByText(/Too many members for a matrix: showing the 4 most liquid of 120/),
    ).toBeInTheDocument();
  });

  it('lists the members and draws only the rows in view above 100 of them', async () => {
    const rows = Array.from({ length: 150 }, (_, i) => ({
      symbol: `S${String(i).padStart(3, '0')}`,
      returnPct: 1,
      share: 0.01,
    }));
    const rich = buildRichProfile();
    show(buildRichProfile({ contribution: { ...rich.contribution, rows } }), '?tab=inside');

    const table = await screen.findByRole('table', { name: 'Members' });

    const drawn = within(table).getAllByRole('row').length;
    expect(drawn).toBeGreaterThan(10);
    expect(drawn).toBeLessThan(60);
    expect(within(table).getByText('S000')).toBeInTheDocument();
    expect(within(table).queryByText('S149')).not.toBeInTheDocument();
  });

  it('shows a regime with too few days as a state, with the minimum, and a table that scrolls inside its card', async () => {
    show(buildRichProfile(), '?tab=liquidity');

    const table = await screen.findByRole('table', { name: 'Index by market regime' });

    expect(within(table).getAllByText('n 9 of at least 30').length).toBeGreaterThan(0);
    expect(screen.getByRole('region', { name: 'Index by market regime, scrolls sideways' })).toHaveAttribute(
      'tabindex',
      '0',
    );
  });

  it('gives the reason when no regime is recorded', async () => {
    show(buildProfile(), '?tab=liquidity');

    expect(await screen.findByText('no regime code is recorded for the window')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Index by market regime' })).not.toBeInTheDocument();
  });

  it('shows the strategy row, the median split, a link into Reports with the basket and the disabled regime panel with the server reason', async () => {
    const user = userEvent.setup();
    const cell = {
      strategy: 'ORB',
      splitDate: '2026-02-01',
      firstN: 40,
      secondN: 40,
      firstExpectancy: value(0.4),
      secondExpectancy: value(-0.2),
      verdict: 'disagrees',
    };
    show(buildProfile({ holdOut: { status: value(80), trades: 80, minPerHalf: 30, cells: [cell] } }));
    await header();

    await user.click(screen.getByRole('tab', { name: 'Strategies' }));

    expect(await screen.findByRole('table', { name: 'Strategy results for this basket' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open in Reports' })).toHaveAttribute('href', `/reports?basket=${id}`);
    const split = screen.getByRole('table', { name: 'Expectancy in each half' });
    expect(within(split).getByText('The two halves disagree in sign')).toBeInTheDocument();
    expect(await screen.findByText('Regime is unavailable: no market context is stored.')).toBeInTheDocument();
  });

  it('says a half with too few trades is insufficient with its n', async () => {
    const user = userEvent.setup();
    const thin = nonValue('insufficient', 'n = 12, at least 30 needed');
    const cell = {
      strategy: 'ORB',
      splitDate: '2026-02-01',
      firstN: 12,
      secondN: 40,
      firstExpectancy: thin,
      secondExpectancy: value(0.2),
      verdict: 'insufficient',
    };
    show(buildProfile({ holdOut: { status: value(52), trades: 52, minPerHalf: 30, cells: [cell] } }));
    await header();

    await user.click(screen.getByRole('tab', { name: 'Strategies' }));
    const split = await screen.findByRole('table', { name: 'Expectancy in each half' });

    expect(within(split).getByText('n 12 of at least 30')).toBeInTheDocument();
    expect(within(split).getByText('Too few trades in a half')).toBeInTheDocument();
  });
});
