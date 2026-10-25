import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { buildBasket } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import {
  allFigures,
  buildEquity,
  buildInsights,
  buildPivot,
  nonValue,
  rankedWhatWorks,
  value,
} from '@/test/reportFixtures';
import { renderReports, reportServer } from '@/test/reportScreen';
import { server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const markup = '<img src=x onerror=alert(1)>';
const figure = /[+-]\d+\.\d\d R/;

const states = [
  ['insufficient', 'Insufficient data'],
  ['unavailable', 'Not available'],
  ['not_evaluable', 'Not evaluable'],
  ['empty', 'No records yet'],
] as const;

describe('every state through the heat grid and the ranked table', () => {
  it('draws each of the six states of a pivot cell with its word and no figure for a non-value', async () => {
    reportServer();
    const cells = [
      ['v', 'value', value(0.3)],
      ['p', 'provisional', { state: 'provisional', number: 0.2, reason: 'A run is provisional.' }],
      ['i', 'insufficient', nonValue('insufficient')],
      ['u', 'unavailable', nonValue('unavailable', 'No exit recorded.')],
      ['n', 'not_evaluable', nonValue('not_evaluable', 'Needs a model.')],
      ['e', 'empty', nonValue('empty', 'No trades.')],
    ] as const;
    server.use(
      http.get('/api/reports/pivot', () =>
        HttpResponse.json({
          ...buildPivot(),
          rows: ['orb'],
          columns: cells.map(([id]) => id),
          cells: cells.map(([id, , metric]) => ({ row: 'orb', column: id, n: 40, value: metric })),
        }),
      ),
    );
    renderReports();
    const grid = await screen.findByRole('grid', { name: 'Expectancy by strategy and timeframe' });
    const named = (word: RegExp) => within(grid).getByRole('button', { name: word });
    expect(named(/orb, v: \+0\.30 R/)).toHaveTextContent('+0.30 R');
    expect(named(/orb, p: provisional/)).toHaveTextContent('Provisional');
    for (const [column, word] of [
      ['i', /insufficient data/],
      ['u', /not available/],
      ['n', /not evaluable/],
      ['e', /no records yet/],
    ] as const) {
      const cell = named(new RegExp(`orb, ${column}: ${word.source}`));
      expect(cell).not.toHaveTextContent(figure);
      expect(cell.querySelector('[data-state="value"]')).toBeNull();
    }
  });

  it.each(states)('prints a %s ranked figure as its word, never a digit', async (state, word) => {
    const ranked = rankedWhatWorks();
    reportServer({
      whatWorks: {
        ...ranked,
        ranked: ranked.ranked.map((cell) => ({
          ...cell,
          group: allFigures(nonValue(state, 'Reason text'), {
            key: cell.group.key,
            n: cell.group.n,
            parts: cell.group.parts,
          }),
        })),
      },
    });
    renderReports('?view=what-works');
    const table = await screen.findByRole('table', { name: 'Ranked cells' });
    await waitFor(() => {
      expect(within(table).getAllByText(word).length).toBeGreaterThanOrEqual(9);
    });
    expect(table).not.toHaveTextContent(figure);
  });
});

describe('equity of a group that is not a value', () => {
  it('prints the state and neither a chart nor a summary figure', async () => {
    reportServer();
    const equity = buildEquity();
    server.use(
      http.get('/api/reports/equity', () =>
        HttpResponse.json({
          ...equity,
          series: equity.series.map((s) => ({ ...s, totalR: nonValue('unavailable', 'Too few exit dates.') })),
        }),
      ),
    );
    renderReports();
    const panel = await screen.findByRole('region', { name: 'Equity and drawdown' });
    expect(await within(panel).findByText('Not available')).toBeInTheDocument();
    expect(panel).not.toHaveTextContent('exit dates, ');
    expect(panel).not.toHaveTextContent(figure);
  });
});

describe('free text from the server is text', () => {
  it('in the not-ranked reason and the ranked headline of the full answer', async () => {
    const ranked = rankedWhatWorks();
    reportServer({
      whatWorks: {
        ...ranked,
        headline: markup,
        notRanked: ranked.notRanked.map((cell) => ({ ...cell, reason: `Why ${markup}` })),
      },
    });
    const { container } = renderReports('?view=what-works');
    expect((await screen.findAllByText(`Why ${markup}`)).length).toBe(4);
    expect(screen.getByText(markup)).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
  });

  it('in the insight sentences and the basket names', async () => {
    reportServer();
    const insights = buildInsights();
    server.use(
      http.get('/api/reports/insights', () =>
        HttpResponse.json({
          ...insights,
          insights: insights.insights.map((i) => ({ ...i, text: `${i.text} ${markup}` })),
        }),
      ),
      http.get('/api/baskets', () =>
        HttpResponse.json([buildBasket({ id: '00000000-0000-4000-8000-000000000401', name: markup })]),
      ),
    );
    const { container } = renderReports();
    const list = await screen.findByRole('list', { name: 'Insights' });
    expect(within(list).getAllByText(new RegExp('onerror=alert\\(1\\)')).length).toBe(8);
    const basket = await screen.findByRole('region', { name: 'Expectancy by basket' });
    expect(await within(basket).findByText(markup)).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
  });
});

describe('the update announcement', () => {
  it('is announced again for every change', async () => {
    reportServer();
    renderReports();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: '1M' }));
    const first = await screen.findByText('Report updated');
    await user.click(screen.getByRole('button', { name: '3M' }));
    await waitFor(() => {
      expect(screen.getByText('Report updated')).not.toBe(first);
    });
  });
});
