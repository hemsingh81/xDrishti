import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildPhaseAccount, buildPhaseCheck, buildSessionLine } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { PhaseCheckPanel } from './components/PhaseCheckPanel';
import { recordWrites } from './testing';

recordWrites();
pinClock('2026-10-30T04:00:00Z');

const serve = (body: ReturnType<typeof buildPhaseCheck>) => {
  server.use(http.get('/api/execution/phase-check', () => HttpResponse.json(body)));
};

describe('PhaseCheckPanel', () => {
  it('reads NOT EVALUABLE on an empty database, with the limits, and is not green', async () => {
    renderWithProviders(<PhaseCheckPanel />);
    const card = await screen.findByRole('region', { name: 'Test Dhan Main phase check' });
    expect(within(card).getByText('NOT EVALUABLE')).toBeInTheDocument();
    expect(within(card).getByText(/0 of 30 counted sessions/)).toBeInTheDocument();
    expect(within(card).getByText('No sessions recorded.')).toBeInTheDocument();
    expect(screen.getByText(/Phase exit:/).parentElement).toHaveTextContent('NOT EVALUABLE');
    expect(screen.getByText('Sends per second are unavailable.')).toBeInTheDocument();
    expect(screen.queryByText('MET')).not.toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('shows MET with the count since the window start and the class words', async () => {
    serve(
      buildPhaseCheck({
        exitVerdict: 'MET',
        exitCriterion: 'MET (1 account)',
        accounts: [
          buildPhaseAccount({
            verdict: 'MET',
            windowStart: '2026-09-01',
            counted: 30,
            streak: 30,
            label: 'MET (30 counted sessions since 2026-09-01)',
            sessions: [buildSessionLine(), buildSessionLine({ date: '2026-10-29', class: 'idle' })],
          }),
        ],
      }),
    );
    renderWithProviders(<PhaseCheckPanel />);
    const card = await screen.findByRole('region', { name: 'Test Dhan Main phase check' });
    expect(within(card).getByText('30 of 30 counted sessions since 2026-09-01')).toBeInTheDocument();
    const table = within(card).getByRole('table', { name: 'Test Dhan Main sessions' });
    expect(within(table).getByText('Counted')).toBeInTheDocument();
    expect(within(table).getByText('Idle')).toBeInTheDocument();
    expect(within(card).getByText('No break in the window.')).toBeInTheDocument();
  });

  it('shows NOT MET with the last break, its kind and date', async () => {
    serve(
      buildPhaseCheck({
        exitVerdict: 'NOT MET',
        exitCriterion: 'NOT MET (a break in the window)',
        accounts: [
          buildPhaseAccount({
            verdict: 'NOT MET',
            windowStart: '2026-10-01',
            counted: 5,
            broken: 1,
            lastBreak: '2026-10-20: position_mismatch',
            sessions: [
              buildSessionLine({
                date: '2026-10-20',
                class: 'broken',
                breaks: 1,
                breakKinds: ['position_mismatch'],
                reasons: ['recon_break'],
              }),
            ],
          }),
        ],
      }),
    );
    renderWithProviders(<PhaseCheckPanel />);
    const card = await screen.findByRole('region', { name: 'Test Dhan Main phase check' });
    expect(within(card).getByText('NOT MET')).toBeInTheDocument();
    expect(within(card).getByText('Last break: 2026-10-20: position_mismatch')).toBeInTheDocument();
    expect(within(card).getByText('Broken')).toBeInTheDocument();
    expect(within(card).getByText('1 (position_mismatch)')).toBeInTheDocument();
  });

  it('shows the problem and a retry when the read fails', async () => {
    server.use(
      http.get('/api/execution/phase-check', () =>
        HttpResponse.json({ title: 'Phase check unavailable', status: 500 }, { status: 500 }),
      ),
    );
    renderWithProviders(<PhaseCheckPanel />);
    expect(await screen.findByRole('alert')).toHaveTextContent(/unavailable/i);
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
