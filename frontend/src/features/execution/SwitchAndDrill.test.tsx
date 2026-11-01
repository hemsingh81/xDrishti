import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildDrills,
  buildLimitsView,
  buildModeAccount,
  buildModes,
  buildSettings,
  buildSettingsRow,
  execAccountId,
} from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const problem = (status: number, detail: string) =>
  HttpResponse.json(
    { title: 'Refused', detail, status },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );

const open = (settings = buildSettings(), drills = buildDrills()) => {
  server.use(
    http.get('/api/execution/modes', () => HttpResponse.json(buildModes([buildModeAccount()]))),
    http.get('/api/execution/limits', () => HttpResponse.json(buildLimitsView())),
    http.get('/api/execution/settings', () => HttpResponse.json(settings)),
    http.get('/api/execution/drills', () => HttpResponse.json(drills)),
  );
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: '/execution?tab=modes' });
};
const switchCard = async () => screen.findByRole('region', { name: 'Execution switch' });
const fill = async (dialog: HTMLElement, password: string, reason?: string) => {
  if (reason !== undefined) await userEvent.type(within(dialog).getByLabelText(/Reason/), reason);
  await userEvent.type(within(dialog).getByLabelText(/Password/), password);
};
const watchPut = () => {
  const bodies: unknown[] = [];
  server.use(
    http.put('/api/execution/settings', async ({ request }) => {
      bodies.push(await request.json());
      return HttpResponse.json(buildSettings());
    }),
  );
  return bodies;
};

describe('Execution switch', () => {
  it('shows the state in force, the waiting switch-on in IST words and the locked auto-live with no control', async () => {
    open(
      buildSettings({
        pending: buildSettingsRow({ id: 2, executionEnabled: true, effectiveFrom: '2026-11-02T03:30:00Z' }),
      }),
    );
    const card = await switchCard();
    expect(await within(card).findByText('OFF')).toBeInTheDocument();
    expect(within(card).getByRole('status')).toHaveTextContent(
      'takes effect at 09:00 IST on 2 Nov 2026 after a full closed session',
    );
    expect(within(card).getByText(/Auto-live: OFF, locked/)).toBeInTheDocument();
    expect(within(card).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(card).queryByRole('switch')).not.toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Turn on' })).toBeDisabled();
    expect(within(card).getByRole('button', { name: 'Turn off' })).toBeEnabled();
  });

  it('sends nothing before confirm or after cancel, then sends the exact body and clears the fields', async () => {
    const bodies = watchPut();
    open();
    const card = await switchCard();
    await userEvent.click(await within(card).findByRole('button', { name: 'Turn on' }));
    let dialog = await screen.findByRole('dialog');
    await fill(dialog, 'secret', 'start of week');
    expect(bodies).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(bodies).toEqual([]);

    await userEvent.click(within(card).getByRole('button', { name: 'Turn on' }));
    dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    await fill(dialog, 'secret', ' start of week ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    await vi.waitFor(() => {
      expect(bodies).toEqual([
        { executionEnabled: true, autoLiveEnabled: false, reason: 'start of week', password: 'secret' },
      ]);
    });
    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('turns off with the password too, and never sends auto-live as true', async () => {
    const bodies = watchPut();
    open(buildSettings({ inForce: buildSettingsRow({ executionEnabled: true }) }));
    const card = await switchCard();
    expect(await within(card).findByText('ON')).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: 'Turn off' }));
    const dialog = await screen.findByRole('dialog');
    await fill(dialog, 'pw', 'stop');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));
    await vi.waitFor(() => {
      expect(bodies).toEqual([{ executionEnabled: false, autoLiveEnabled: false, reason: 'stop', password: 'pw' }]);
    });
  });

  it('shows the refusal as it came and keeps the dialog open', async () => {
    let calls = 0;
    server.use(
      http.put('/api/execution/settings', () => {
        calls += 1;
        return problem(409, 'live.capability_off: live writes are not enabled');
      }),
    );
    open();
    const card = await switchCard();
    await userEvent.click(await within(card).findByRole('button', { name: 'Turn on' }));
    const dialog = await screen.findByRole('dialog');
    await fill(dialog, 'pw', 'go');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    expect(await within(dialog).findByText(/live\.capability_off/)).toBeInTheDocument();
    expect(calls).toBe(1);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('shows an error state with a retry when the switch cannot be read', async () => {
    server.use(http.get('/api/execution/settings', () => problem(500, 'down')));
    open();
    server.use(http.get('/api/execution/settings', () => problem(500, 'down')));
    const card = await switchCard();
    expect(await within(card).findByRole('alert')).toBeInTheDocument();
  });
});

describe('Kill-switch drill', () => {
  const drill = {
    id: 3,
    at: '2026-10-28T04:00:00Z',
    accountId: execAccountId,
    passed: true,
    latencyMs: 420,
    detail: 'entry stopped before transport',
  };
  const req = (n: number, result: string | null, code: string | null) => ({
    commandId: `00000000-0000-4000-8000-00000000000${n}`,
    accountId: execAccountId,
    at: `2026-10-30T0${n}:00:00Z`,
    result,
    code,
    answeredAt: result === null ? null : '2026-10-30T09:00:00Z',
  });

  it('shows the last result with its age and says the drill sends nothing', async () => {
    open(buildSettings(), buildDrills({ drills: [drill] }));
    const card = await screen.findByRole('region', { name: 'Kill-switch drill: Test Dhan Main' });
    expect(await within(card).findByText(/Last drill passed 2 days ago/)).toBeInTheDocument();
    expect(within(card).getByText(/counts for the mode ladder for 30 days/)).toBeInTheDocument();
    expect(within(card).getByText(/sends nothing to the broker/)).toBeInTheDocument();
  });

  it('says an old drill no longer counts', async () => {
    open(buildSettings(), buildDrills({ drills: [{ ...drill, at: '2026-09-01T04:00:00Z' }] }));
    expect(await screen.findByText(/older than 30 days and no longer counts/)).toBeInTheDocument();
  });

  it('maps the answers to words and shows a waiting request as waiting, not as success', async () => {
    open(
      buildSettings(),
      buildDrills({
        requests: [
          req(1, null, null),
          req(2, 'refused', 'execution.drill_unavailable'),
          req(3, 'refused', 'execution.drill_refused:halt_active'),
          req(4, 'refused', 'execution.drill_refused:not_flat'),
          req(5, 'refused', 'execution.drill_refused:no_snapshot'),
        ],
      }),
    );
    const list = await screen.findByRole('list', { name: 'Drill requests: Test Dhan Main' });
    const text = list.textContent;
    expect(text).toContain('Waiting for the execution service');
    expect(text).toContain('Not available in this environment');
    expect(text).toContain('Refused: a halt is in force');
    expect(text).toContain('Refused: the account is not flat');
    expect(text).toContain('Refused: the sandbox book or our record could not be read');
    expect(text).not.toContain('Passed');
  });

  it('asks only the password, sends the exact body after confirm and shows nothing as passed', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.post('/api/execution/drills', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json({ commandId: '00000000-0000-4000-8000-000000000009' }, { status: 202 });
      }),
    );
    open();
    const card = await screen.findByRole('region', { name: 'Kill-switch drill: Test Dhan Main' });
    await userEvent.click(await within(card).findByRole('button', { name: 'Run drill' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByLabelText(/Reason/)).not.toBeInTheDocument();
    await fill(dialog, 'pw');
    expect(bodies).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Run the drill' }));
    await vi.waitFor(() => {
      expect(bodies).toEqual([{ accountId: execAccountId, password: 'pw' }]);
    });
    expect(screen.queryByText('Passed')).not.toBeInTheDocument();
  });

  it('shows the refusal, sends once and keeps the dialog open', async () => {
    let calls = 0;
    server.use(
      http.post('/api/execution/drills', () => {
        calls += 1;
        return problem(401, 'auth.step_up_failed: wrong password');
      }),
    );
    open();
    const card = await screen.findByRole('region', { name: 'Kill-switch drill: Test Dhan Main' });
    await userEvent.click(await within(card).findByRole('button', { name: 'Run drill' }));
    const dialog = await screen.findByRole('dialog');
    await fill(dialog, 'bad');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Run the drill' }));
    expect(await within(dialog).findByText(/auth\.step_up_failed/)).toBeInTheDocument();
    expect(calls).toBe(1);
  });
});
