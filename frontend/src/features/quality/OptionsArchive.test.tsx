import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildCoverageView } from '@/test/optionsFixtures';
import { problemResponse } from '@/test/server';
import { renderQuality } from './testing';

const open = async () => {
  await userEvent.click(await screen.findByRole('button', { name: 'Options archive' }));
  return screen.findByRole('region', { name: 'Options archive' });
};

describe('Data quality: options archive', () => {
  it('reads nothing until opened, then shows checks, usable and truncated without any price', async () => {
    const asked: (string | null)[] = [];
    renderQuality({
      handlers: [
        http.get('/api/options/coverage', ({ request }) => {
          asked.push(new URL(request.url).searchParams.get('underlying'));
          return HttpResponse.json(
            buildCoverageView({
              usableChainSessions: 12,
              truncated: true,
              sessions: [
                {
                  date: '2026-10-30',
                  source: 'chain',
                  fetchKey: null,
                  usable: true,
                  checks: [
                    { code: 'coverage', status: 'pass', value: 0.99, threshold: 0.95, reason: null },
                    { code: 'parity', status: 'unavailable', value: null, threshold: 0.065, reason: 'too_few_points' },
                  ],
                },
              ],
            }),
          );
        }),
      ],
    });
    await screen.findByRole('button', { name: 'Run checks' });
    expect(asked).toEqual([]);

    const card = await open();
    expect(await within(card).findByText('Usable chain sessions: 12')).toBeInTheDocument();
    expect(within(card).getByText('coverage: pass')).toBeInTheDocument();
    expect(within(card).getByText('parity: unavailable (too_few_points)')).toBeInTheDocument();
    expect(within(card).getByText('Usable')).toBeInTheDocument();
    expect(within(card).getByText(/More sessions exist than are listed/)).toBeInTheDocument();
    expect(asked).toEqual(['NIFTY']);
    expect(card).not.toHaveTextContent('0.99');
    expect(card).not.toHaveTextContent('0.95');
  });

  it('switches the underlying and shows a session that is not usable', async () => {
    const asked: (string | null)[] = [];
    renderQuality({
      handlers: [
        http.get('/api/options/coverage', ({ request }) => {
          const u = new URL(request.url).searchParams.get('underlying');
          asked.push(u);
          return HttpResponse.json(
            buildCoverageView({
              underlying: 'NSE:BANKNIFTY',
              sessions:
                u === 'BANKNIFTY'
                  ? [{ date: '2026-10-29', source: 'chain', fetchKey: null, usable: false, checks: [] }]
                  : [],
            }),
          );
        }),
      ],
    });
    const card = await open();
    await within(card).findByText(/Usable chain sessions/);
    await userEvent.click(within(card).getByRole('combobox', { name: 'Underlying' }));
    await userEvent.click(await screen.findByRole('option', { name: 'BANKNIFTY' }));
    expect(await within(card).findByText('Not usable')).toBeInTheDocument();
    expect(asked).toContain('BANKNIFTY');
  });

  it('lists at most 200 sessions and counts the rest', async () => {
    const sessions = Array.from({ length: 205 }, (_, i) => ({
      date: '2026-10-01',
      source: 'chain' as const,
      fetchKey: `k${String(i)}`,
      usable: true,
      checks: [],
    }));
    renderQuality({
      handlers: [http.get('/api/options/coverage', () => HttpResponse.json(buildCoverageView({ sessions })))],
    });
    const card = await open();
    expect(await within(card).findByText('5 more sessions not shown')).toBeInTheDocument();
    expect(within(card).getAllByRole('listitem')).toHaveLength(200);
  });

  it('says an empty archive plainly, not as an error', async () => {
    renderQuality({ handlers: [http.get('/api/options/coverage', () => HttpResponse.json(buildCoverageView()))] });
    const card = await open();
    expect(await within(card).findByText(/Nothing is recorded for NSE:NIFTY/)).toBeInTheDocument();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows an error state when the read fails', async () => {
    renderQuality({
      handlers: [http.get('/api/options/coverage', () => problemResponse({ title: 'Down', code: 'x' }, 500))],
    });
    const card = await open();
    expect(await within(card).findByRole('alert')).toBeInTheDocument();
  });
});
