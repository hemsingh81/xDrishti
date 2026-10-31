import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildBlotterRow, buildExecStatus, buildHalt, buildPosition } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { StopChip } from './components/StopChip';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const status = (over = {}) => {
  server.use(
    http.get('/api/execution/status', () =>
      HttpResponse.json(
        buildExecStatus({
          anyAutomated: true,
          service: { state: 'ready', lastBeatAt: '2026-10-30T03:59:50Z' },
          ...over,
        }),
      ),
    ),
  );
};

describe('Stop chip', () => {
  beforeEach(() => {
    server.use(
      http.get('/api/execution/blotter', () =>
        HttpResponse.json([buildBlotterRow({ state: 'open', stateGroup: 'working' })]),
      ),
      http.get('/api/execution/positions', () =>
        HttpResponse.json([buildPosition(), buildPosition({ symbol: 'INFY' })]),
      ),
    );
  });

  it('is not there when nothing is automated and no halt is active', async () => {
    renderWithProviders(<StopChip />);
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.queryByRole('button', { name: 'Stop trading' })).not.toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('opens a dialog with the numbers, three outcome-labelled actions, and the focus on Cancel', async () => {
    status();
    renderWithProviders(<StopChip />);
    await userEvent.click(await screen.findByRole('button', { name: 'Stop trading' }));
    const dialog = await screen.findByRole('dialog');
    expect(
      await within(dialog).findByText(/Working orders: 1\. Open positions: 2\. Stops stay at the broker/),
    ).toBeInTheDocument();
    for (const name of [
      'Stop new orders',
      'Stop new orders and cancel pending ones',
      'Stop new orders, cancel pending ones and exit all positions with marketable limit orders',
    ]) {
      expect(within(dialog).getByRole('button', { name })).toBeInTheDocument();
    }
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    });
  });

  it('sends the entries stop on one click and says requested, not stopped, until the halt row appears', async () => {
    status();
    let halts: unknown[] = [];
    let body: unknown;
    server.use(
      http.post('/api/execution/halts', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ commandId: 'c1' });
      }),
      http.get('/api/execution/halts', () => HttpResponse.json(halts)),
    );
    renderWithProviders(<StopChip />);
    await userEvent.click(await screen.findByRole('button', { name: 'Stop trading' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Stop new orders' }));
    expect(await screen.findByText('Stop requested')).toBeInTheDocument();
    expect(body).toMatchObject({ scope: 'system', level: 'entries' });
    expect(screen.queryByText(/Trading stopped/)).not.toBeInTheDocument();

    halts = [buildHalt()];
    expect(await screen.findByText(/Trading stopped \(halt 17, entries/, {}, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.queryByText('Stop requested')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Release' })).toBeInTheDocument();
  });

  it('says xd-exec is not running when the service is down and only the request exists', async () => {
    status({ service: { state: 'down', lastBeatAt: null } });
    server.use(
      http.post('/api/execution/halts', () => HttpResponse.json({ commandId: 'c1' })),
      http.get('/api/execution/halts', () => HttpResponse.json([])),
    );
    renderWithProviders(<StopChip />);
    await userEvent.click(await screen.findByRole('button', { name: 'Stop trading' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Stop new orders and cancel pending ones' }));
    expect(await screen.findByText('Stop requested, xd-exec is not running')).toBeInTheDocument();
  });

  it('exit all lists the positions, warns, and needs a second click that runs the step-up', async () => {
    status();
    const calls: number[] = [];
    server.use(
      http.post('/api/execution/halts', () => {
        calls.push(1);
        return calls.length === 1
          ? HttpResponse.json({ title: 'Step-up', code: 'auth.step_up_required' }, { status: 403 })
          : HttpResponse.json({ commandId: 'c2' });
      }),
      http.post('/api/auth/step-up', () => HttpResponse.json({ validUntil: '2026-10-30T04:10:00Z' })),
      http.get('/api/execution/halts', () => HttpResponse.json([])),
    );
    renderWithProviders(<StopChip />);
    await userEvent.click(await screen.findByRole('button', { name: 'Stop trading' }));
    await userEvent.click(
      await screen.findByRole('button', { name: /exit all positions with marketable limit orders/ }),
    );
    expect(calls).toHaveLength(0);
    const list = await screen.findByRole('list', { name: 'Positions to exit' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText(/never sends market orders, so a fill is\s+not guaranteed/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Exit all positions' }));
    const pw = await screen.findByLabelText(/Password/);
    await userEvent.type(pw, 'secret');
    await userEvent.keyboard('{Enter}');
    await waitFor(() => {
      expect(calls).toHaveLength(2);
    });
    expect(await screen.findByText(/Stop requested/)).toBeInTheDocument();
  });

  it('shows the chip as stopped with the halt details when a halt is already active, and release shows the refusal verbatim', async () => {
    status({ anyAutomated: false, killSwitchActive: true });
    let sent: unknown;
    server.use(
      http.get('/api/execution/halts', () => HttpResponse.json([buildHalt({ level: 'cancel' })])),
      http.post('/api/execution/halts/17/release', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(
          { title: 'Refused', detail: 'The broker book is not clean: 1 open order.' },
          { status: 409 },
        );
      }),
    );
    renderWithProviders(<StopChip />);
    expect(await screen.findByText(/Trading stopped \(halt 17, cancel/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Release' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'checked');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Release the halt' }));
    expect(await within(dialog).findByText('The broker book is not clean: 1 open order.')).toBeInTheDocument();
    expect(sent).toEqual({ reason: 'checked', password: 'pw' });
  });

  it('does not say stopped while the halt rows cannot be read', async () => {
    status({ anyAutomated: false, killSwitchActive: true });
    server.use(http.get('/api/execution/halts', () => HttpResponse.json({ title: 'down' }, { status: 500 })));
    renderWithProviders(<StopChip />);
    expect(await screen.findByText('A halt is reported, reading it')).toBeInTheDocument();
    expect(screen.queryByText(/Trading stopped/)).not.toBeInTheDocument();
  });
});
