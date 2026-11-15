import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { buildBasket } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { buildSimDay, buildSimDays, SIM, simDaysServer } from '@/test/simDaysFixtures';
import { simDaysInterval, simDaysPollMs } from './api/simDaysApi';
import { SimDaysPage } from './components/days/SimDaysPage';

pinClock('2026-11-15T04:00:00Z');

const show = (path = `/simulation/days?sim=${SIM}`) =>
  renderWithProviders(<SimDaysPage />, { path: '/simulation/days' }).router.navigate(path);

const clock = () => screen.getByRole('region', { name: 'Simulation clock' });
const findClock = () => screen.findByRole('region', { name: 'Simulation clock' });

describe('SimDaysPage', () => {
  it('validates the start form and shows the API refusal inline', async () => {
    localStorage.clear();
    server.use(
      http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
      http.post('/api/sim/days', () =>
        problemResponse({ code: 'sim.busy', detail: 'A simulation is already active.' }, 409),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<SimDaysPage />);
    await user.click(screen.getByRole('button', { name: 'Start simulation' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Choose the start date.');
    await user.type(screen.getByLabelText(/^Start\s*\*?$/), '2026-02-02');
    expect(screen.getByRole('alert')).toHaveTextContent('Choose at least one stock or basket.');
    expect(screen.getByLabelText('Capital (₹)')).toHaveValue('1000000');
    expect(screen.getByRole('radio', { name: /Learning/ })).toBeChecked();
    await user.click(screen.getByRole('combobox', { name: 'Baskets' }));
    await user.click(await screen.findByRole('option', { name: 'MyChoice' }));
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Start simulation' }));
    expect(await screen.findByText('A simulation is already active.')).toBeInTheDocument();
  });

  it('opens the new simulation after a start', async () => {
    localStorage.clear();
    let body: Schemas['SimDaysStartBody'] | undefined;
    server.use(
      http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
      http.post('/api/sim/days', async ({ request }) => {
        body = (await request.json()) as Schemas['SimDaysStartBody'];
        return HttpResponse.json({ id: SIM }, { status: 201 });
      }),
      ...simDaysServer(buildSimDays({ phase: 'idle', settledEquity: null })),
    );
    const user = userEvent.setup();
    renderWithProviders(<SimDaysPage />);
    await user.type(screen.getByLabelText(/^Start\s*\*?$/), '2026-02-02');
    await user.click(screen.getByRole('combobox', { name: 'Baskets' }));
    await user.click(await screen.findByRole('option', { name: 'MyChoice' }));
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Start simulation' }));
    expect(await screen.findByRole('heading', { name: /Simulation 2 Feb 2026/ })).toBeInTheDocument();
    expect(body).toMatchObject({ start: '2026-02-02', end: null, capital: 1_000_000, mode: 'learning' });
    expect(body?.scope?.basketIds).toEqual([buildBasket().id]);
  });

  it('plays the settled day from 09:15 and never shows what comes after the clock minute', async () => {
    server.use(...simDaysServer());
    const user = userEvent.setup();
    await show();
    expect(await within(await findClock()).findByText('09:15')).toBeInTheDocument();
    const positions = await screen.findByRole('table', { name: 'Open positions' });
    expect(within(positions).getByText('INFY BUY')).toBeInTheDocument();
    expect(within(positions).queryByText('RELIANCE BUY')).not.toBeInTheDocument();
    expect(screen.getByText('Events so far (0)')).toBeInTheDocument();
    expect(screen.getByText('WIPRO: risk_cap — max open positions')).toBeInTheDocument();

    await user.click(within(clock()).getByRole('button', { name: 'Max' }));
    expect(within(clock()).getByRole('button', { name: 'Max' })).toHaveAttribute('aria-pressed', 'true');
    await within(clock()).findByText('09:17');
    await user.click(within(clock()).getByRole('button', { name: 'Pause' }));
    const at = within(clock()).getByRole('time').textContent;
    // the board shows exactly what is at or before the clock minute
    const minuteNow = Number(at.slice(3));
    const table = screen.getByRole('table', { name: 'Open positions' });
    if (minuteNow < 18) expect(within(table).getByText('RELIANCE BUY')).toBeInTheDocument();
    else expect(within(table).queryByText('RELIANCE BUY')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: `Live board at ${at}` })).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 200));
    expect(within(clock()).getByText(at)).toBeInTheDocument();
    await user.click(within(clock()).getByRole('button', { name: 'Resume' }));
    await within(clock()).findByText('09:20');
  });

  it('jumps to the end of the day on Instant: the board equity is the closing equity', async () => {
    server.use(...simDaysServer());
    const user = userEvent.setup();
    await show();
    await within(await findClock()).findByText('09:15');
    await user.click(screen.getByRole('button', { name: 'Instant' }));
    expect(await within(clock()).findByText('15:29')).toBeInTheDocument();
    expect(within(clock()).getByText(/end of day/)).toBeInTheDocument();
    expect(await screen.findByText(/Realised equity ₹10,00,040/)).toBeInTheDocument();
    expect(screen.getByText('Events so far (3)')).toBeInTheDocument();
  });

  it('runs a day: settle, then the clock plays it from 09:15', async () => {
    let settled = false;
    server.use(
      http.get('/api/sim/days/:id', () => HttpResponse.json(buildSimDays({ phase: settled ? 'settled' : 'idle' }))),
      http.post('/api/sim/days/:id/settle', () => {
        settled = true;
        return HttpResponse.json(buildSimDays({ phase: 'settled' }), { status: 202 });
      }),
      ...simDaysServer(),
    );
    const user = userEvent.setup();
    await show();
    expect(await screen.findByRole('button', { name: 'Advance' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Run day' }));
    expect(await within(await findClock()).findByText('09:15')).toBeInTheDocument();
    expect(within(clock()).getByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Advance' })).toBeEnabled();
  });

  it('shows why auto-run waits, and pauses and resumes it', async () => {
    let auto = true;
    const asked: boolean[] = [];
    server.use(
      http.get('/api/sim/days/:id', () =>
        HttpResponse.json(buildSimDays({ phase: 'idle', auto, waiting: auto ? 'market_hours' : null })),
      ),
      ...simDaysServer(buildSimDays({ phase: 'idle' }), null),
      http.post('/api/sim/days/:id/auto', async ({ request }) => {
        auto = ((await request.json()) as { on: boolean }).on;
        asked.push(auto);
        return HttpResponse.json(buildSimDays({ phase: 'idle', auto, waiting: auto ? 'market_hours' : null }));
      }),
    );
    const user = userEvent.setup();
    await show();
    expect(await screen.findByText('waiting: market hours')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Run day' })).toBeDisabled();
    await user.click(screen.getByRole('switch', { name: 'Auto' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Run day' })).toBeEnabled();
    });
    expect(screen.queryByText('waiting: market hours')).not.toBeInTheDocument();
    await user.click(screen.getByRole('switch', { name: 'Auto' }));
    expect(await screen.findByText('waiting: market hours')).toBeInTheDocument();
    expect(asked).toEqual([false, true]);
  });

  it('continues a complete run, and shows a refusal in the API words', async () => {
    let calls = 0;
    server.use(
      ...simDaysServer(buildSimDays({ phase: 'complete', daysDone: 5, cursor: '2026-02-06' }), null),
      http.post('/api/sim/days/:id/continue', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ code: 'sim.nothing_new', detail: 'No newer complete session is stored.' }, 409)
          : HttpResponse.json(buildSimDays({ phase: 'idle', cursor: '2026-02-09', daysDone: 5, daysTotal: 6 }));
      }),
    );
    const user = userEvent.setup();
    await show();
    expect(await screen.findByText(/The simulation is complete/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Run day' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('No newer complete session is stored.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Run day' })).toBeEnabled();
    });
    expect(screen.getByText('5 of 6')).toBeInTheDocument();
  });

  it('lists the stored days and opens one in the drawer', async () => {
    const stored = buildSimDay({ stored: true });
    server.use(...simDaysServer(buildSimDays({ phase: 'idle', cursor: '2026-02-03', daysDone: 1 }), stored));
    const user = userEvent.setup();
    await show();
    const table = await screen.findByRole('table', { name: 'Stored days' });
    expect(within(table).getByText('₹10,00,040')).toBeInTheDocument();
    await user.click(within(table).getByRole('button', { name: '2 Feb 2026' }));
    expect(await screen.findByRole('heading', { name: 'Day 2 Feb 2026' })).toBeInTheDocument();
    expect(screen.getByText(/RELIANCE BUY 10 at/)).toBeInTheDocument();
  });

  it('shows the hold-out note and the cost label', async () => {
    server.use(
      ...simDaysServer(buildSimDays({ phase: 'idle', holdOutNote: 'This run reads inside hold-out block 1.' }), null),
    );
    await show();
    expect(await screen.findByRole('note', { name: 'Hold-out' })).toHaveTextContent('inside hold-out block 1');
    expect(screen.getByRole('note', { name: 'Hindsight labels' })).toHaveTextContent(/gross/);
  });

  it('polls only while the runner moves', () => {
    expect(simDaysInterval(buildSimDays({ phase: 'settling' }))).toBe(simDaysPollMs);
    expect(simDaysInterval(buildSimDays({ phase: 'idle', auto: true }))).toBe(simDaysPollMs);
    expect(simDaysInterval(buildSimDays({ phase: 'idle' }))).toBe(false);
    expect(simDaysInterval(buildSimDays({ phase: 'complete', auto: true }))).toBe(false);
  });
});
