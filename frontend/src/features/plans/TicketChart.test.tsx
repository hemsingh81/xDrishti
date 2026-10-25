import { readFileSync } from 'node:fs';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { beforeCutoff, buildChart } from '@/test/planFixtures';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { TicketChartPanel } from './components/TicketChartPanel';
import { chartLines } from './lib/chartLines';

pinClock(beforeCutoff);

const withLevels = (over: Partial<ReturnType<typeof buildChart>['levels']> = {}) => ({
  entryRef: 100,
  stop: 95,
  stopAnchor: 'level',
  target: 110,
  rewardRisk: 2,
  session: '2026-10-23',
  basis: "indicative from the close of the session; not tomorrow's price",
  source: 'ticket',
  ...over,
});

let asked = 0;
function serve(chart: ReturnType<typeof buildChart>) {
  asked = 0;
  server.use(
    http.get('/api/plans/7/items/11/chart', () => {
      asked += 1;
      return HttpResponse.json(chart);
    }),
  );
  renderWithProviders(<TicketChartPanel planId={7} itemId={11} symbol="AAA" />);
}
const expand = () => userEvent.click(screen.getByRole('button', { name: 'Chart of AAA' }));

describe('TicketChart', () => {
  it('is collapsed and asks for nothing until expanded', async () => {
    serve(buildChart());

    expect(screen.getByRole('button', { name: 'Chart of AAA' })).toHaveAttribute('aria-expanded', 'false');
    expect(asked).toBe(0);
    await expand();
    expect(await screen.findByRole('img', { name: /AAA daily bars up to 2026-10-23/ })).toBeInTheDocument();
    expect(asked).toBe(1);
  });

  it('draws one labelled line per number, anchored by the stop kind, with the basis caption', async () => {
    serve(buildChart({ levels: withLevels() }));
    await expand();

    const lines = await screen.findByRole('list', { name: 'Price lines' });
    expect(lines).toHaveTextContent('Entry reference, close of 2026-10-23: 100.00');
    expect(lines).toHaveTextContent('Stop level: 95.00');
    expect(lines).toHaveTextContent('Target: 110.00');
    expect(
      screen.getByText(/Indicative from the close of the session; not tomorrow's price\. No order is placed\./),
    ).toBeInTheDocument();
  });

  it('labels a stop from the close as indicative and draws no line for a null target', () => {
    const lines = chartLines(withLevels({ stopAnchor: 'close_of_d', target: null, rewardRisk: null }));

    expect(lines.map((l) => l.label)).toEqual([
      'Entry reference, close of 2026-10-23',
      'Stop, indicative from the close',
    ]);
    expect(lines.every((l) => l.value > 0)).toBe(true);
  });

  it('prints the rule texts when the ticket has no numbers', async () => {
    serve(buildChart());
    await expand();

    expect(
      await screen.findByText(
        /Levels are rules, not numbers, on this ticket\. Trigger: Break of 100\.0 · Stop: Stop 95\.0/,
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Price lines' })).not.toBeInTheDocument();
  });

  it('says so for an ambiguous or unknown symbol and draws nothing', async () => {
    serve(buildChart({ resolution: 'ambiguous', bars: [], barCount: 0 }));
    await expand();

    expect(await screen.findByText(/more than one active instrument/)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('prints the count of a short history and the last ten bars as a table', async () => {
    serve(buildChart({ levels: withLevels() }));
    await expand();
    await userEvent.click(await screen.findByRole('button', { name: 'Show as table' }));

    expect(screen.getByText(/12 daily bars up to .*fewer than 60 sessions/)).toBeInTheDocument();
    const rows = screen.getByRole('table', { name: 'Last bars of AAA' }).querySelectorAll('tbody tr');
    expect(rows).toHaveLength(10);
  });
});

describe('The plan screen sources', () => {
  it('make no write call from the new files', () => {
    for (const file of [
      'TicketChart',
      'TicketChartPanel',
      'PlanRunBanner',
      'PlanBanner',
      'PlanFlags',
      'TicketChecklist',
    ]) {
      const source = readFileSync(`src/features/plans/components/${file}.tsx`, 'utf8');
      expect(source).not.toMatch(/api\.(POST|PUT|PATCH|DELETE)|fetch\(|useMutation/);
    }
    const queries = readFileSync('src/features/plans/api/planQueries.ts', 'utf8');
    expect(queries).toContain("api.GET('/api/plan-runs'");
    expect(queries).toContain("api.GET('/api/plans/{id}/items/{itemId}/chart'");
  });
});
