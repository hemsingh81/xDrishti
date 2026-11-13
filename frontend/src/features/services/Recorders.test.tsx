import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { problemResponse, server } from '@/test/server';
import { buildBackfillView, buildOptionRecorderView } from '@/test/optionsFixtures';
import { renderServices } from './testing';

vi.mock('./lib/polling', () => ({ servicesRunningPollMs: 25, servicesIdlePollMs: 60_000 }));

const view = (over: object = {}) => ({
  enabled: false,
  switchId: null,
  state: null,
  lastSeenAt: null,
  notRunningBecause: ['Your switch is OFF.', 'The service opening-bars is paused on the Services screen.'],
  notice: 'Off until the deployment flag, your switch (with your password) and the service are all on.',
  history: [],
  ...over,
});

const onHistory = [
  { id: 2, enabled: true, actor: 'owner:me@x.in', reason: 'pilot day', changedAt: '2026-10-30T04:00:00Z' },
];

interface Put {
  kind: string;
  body: Record<string, unknown>;
}

function mockRecorders(opening = view(), chain = buildOptionRecorderView(), backfill = buildBackfillView()) {
  const puts: Put[] = [];
  const reads: string[] = [];
  let refuse = false;
  server.use(
    http.get('/api/options/chain-recorder', () => {
      reads.push('chain');
      return HttpResponse.json(chain);
    }),
    http.get('/api/options/backfill', () => HttpResponse.json(backfill)),
    http.get('/api/opening-bars/recorder', () => HttpResponse.json(opening)),
    ...(
      [
        ['chain', '/api/options/chain-recorder'],
        ['backfill', '/api/options/backfill'],
        ['opening', '/api/opening-bars/recorder'],
      ] as const
    ).map(([kind, path]) =>
      http.put(path, async ({ request }) => {
        puts.push({ kind, body: (await request.json()) as Record<string, unknown> });
        return refuse
          ? problemResponse(
              { title: 'Wrong password', detail: 'The password is not right.', code: 'auth.invalid_credentials' },
              401,
            )
          : HttpResponse.json({ id: 3, enabled: true });
      }),
    ),
  );
  return {
    puts,
    reads,
    refuse: () => {
      refuse = true;
    },
  };
}

async function openSection() {
  await userEvent.click(await screen.findByRole('button', { name: 'Recorders' }));
  return screen.findByRole('region', { name: 'Opening-minute bars recorder' });
}

describe('Services: recorders', () => {
  it('shows the recorders off, with the reasons and the notice, and reads nothing until opened', async () => {
    const m = mockRecorders();
    renderServices();
    await screen.findByRole('list', { name: 'Services' });
    expect(m.reads).toEqual([]);

    const opening = await openSection();
    expect(await within(opening).findByText('Switch OFF')).toBeInTheDocument();
    expect(within(opening).getByText('Your switch is OFF.')).toBeInTheDocument();
    expect(within(opening).getByText(/Off until the deployment flag/)).toBeInTheDocument();
    expect(within(opening).getByRole('button', { name: 'Turn on Opening-minute bars recorder' })).toBeInTheDocument();
    expect(within(opening).getByText('The switch was never changed.')).toBeInTheDocument();
  });

  it('shows an ON recorder with its state and history', async () => {
    mockRecorders(view({ enabled: true, switchId: 2, state: 'recording', notRunningBecause: [], history: onHistory }));
    renderServices();
    const opening = await openSection();
    expect(await within(opening).findByText('Switch ON')).toBeInTheDocument();
    expect(within(opening).getByText('State: recording')).toBeInTheDocument();
    expect(within(opening).getByText('pilot day')).toBeInTheDocument();
    expect(within(opening).getByText(/Turned on by owner:me@x.in/)).toBeInTheDocument();
    expect(within(opening).getByRole('button', { name: 'Turn off Opening-minute bars recorder' })).toBeInTheDocument();
  });

  it('sends nothing before confirm or after cancel, then one PUT with the password only in that body', async () => {
    const m = mockRecorders();
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on Opening-minute bars recorder' }));
    let dialog = await screen.findByRole('dialog', { name: /Turn on the opening-minute bars recorder/ });
    expect(within(dialog).getByRole('button', { name: 'Turn on' })).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'pilot day');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    expect(m.puts).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(m.puts).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Turn on Opening-minute bars recorder' }));
    dialog = await screen.findByRole('dialog', { name: /Turn on the opening-minute bars recorder/ });
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    expect(within(dialog).getByLabelText(/Reason/)).toHaveValue('');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'pilot day');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    expect(await screen.findByText('The switch was changed.')).toBeInTheDocument();
    expect(m.puts).toEqual([{ kind: 'opening', body: { enabled: true, reason: 'pilot day', password: 'secret-pw' } }]);
  });

  it('turns an ON recorder off with enabled false', async () => {
    const m = mockRecorders(view({ enabled: true, switchId: 2, history: onHistory, notRunningBecause: [] }));
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off Opening-minute bars recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn off the opening-minute bars recorder/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'done piloting');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));
    await waitFor(() => {
      expect(m.puts).toHaveLength(1);
    });
    expect(m.puts[0]).toEqual({
      kind: 'opening',
      body: { enabled: false, reason: 'done piloting', password: 'secret-pw' },
    });
  });

  it('shows the server refusal only in the dialog, sends no second PUT, and clears the fields on reopen', async () => {
    const m = mockRecorders();
    m.refuse();
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on Opening-minute bars recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn on the opening-minute bars recorder/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'why');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'bad');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    expect(await within(dialog).findByText('The password is not right.')).toBeInTheDocument();
    expect(screen.getAllByText('The password is not right.')).toHaveLength(1);
    expect(screen.queryByText('The switch was changed.')).not.toBeInTheDocument();
    expect(m.puts).toHaveLength(1);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(m.puts).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Turn on Opening-minute bars recorder' }));
    const again = await screen.findByRole('dialog', { name: /Turn on the opening-minute bars recorder/ });
    expect(within(again).getByLabelText(/Password/)).toHaveValue('');
    expect(within(again).getByLabelText(/Reason/)).toHaveValue('');
    expect(within(again).queryByText('The password is not right.')).not.toBeInTheDocument();
  });

  it('keeps the same name on the disclosure button and points it at the section', async () => {
    mockRecorders();
    renderServices();
    const toggle = await screen.findByRole('button', { name: 'Recorders' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(screen.getByRole('button', { name: 'Recorders' })).toHaveAttribute('aria-expanded', 'true');
    expect((await screen.findByRole('region', { name: 'Recorders' })).id).toBe(toggle.getAttribute('aria-controls'));
  });

  it('says a recorder that could not be read', async () => {
    mockRecorders();
    server.use(http.get('/api/options/chain-recorder', () => problemResponse({ title: 'Down', code: 'x' }, 500)));
    renderServices();
    const opening = await openSection();
    expect(await within(opening).findByText('Switch OFF')).toBeInTheDocument();
    const chain = screen.getByRole('region', { name: 'Option chain recorder' });
    expect(await within(chain).findByRole('alert')).toBeInTheDocument();
  });
});

describe('Services: the option recorders', () => {
  it('shows the chain recorder and the backfill off, with reasons and no progress before any is reported', async () => {
    mockRecorders();
    renderServices();
    await openSection();
    const chain = await screen.findByRole('region', { name: 'Option chain recorder' });
    const backfill = screen.getByRole('region', { name: 'Expired-options backfill' });
    expect(await within(chain).findByText('Your switch is OFF.')).toBeInTheDocument();
    expect(await within(backfill).findByText('The probe result has not been accepted yet.')).toBeInTheDocument();
    expect(within(backfill).getByText('No progress reported yet.')).toBeInTheDocument();
  });

  it('shows the backfill progress and the accepted probe', async () => {
    mockRecorders(
      view(),
      buildOptionRecorderView(),
      buildBackfillView({
        enabled: true,
        switchId: 4,
        state: 'running',
        notRunningBecause: [],
        history: onHistory,
        probeAccepted: { id: 1, actor: 'owner:me@x.in', note: 'read', acceptedAt: '2026-10-30T04:00:00Z' },
        progress: { planned: 31444, done: 1200, empty: 300, noData: 10, givenUp: 2, left: 29942, estimatedHours: 12.5 },
      }),
    );
    renderServices();
    await openSection();
    const backfill = await screen.findByRole('region', { name: 'Expired-options backfill' });
    const progress = await within(backfill).findByRole('list', { name: 'Backfill progress' });
    expect(within(progress).getByText('Keys done: 1,200')).toBeInTheDocument();
    expect(within(progress).getByText('Keys empty: 300')).toBeInTheDocument();
    expect(within(progress).getByText('Keys with no data: 10')).toBeInTheDocument();
    expect(within(progress).getByText('Keys given up: 2')).toBeInTheDocument();
    expect(within(progress).getByText('Keys left: 29,942')).toBeInTheDocument();
    expect(within(progress).getByText('Estimated finish: about 12.5 hours')).toBeInTheDocument();
    expect(within(backfill).getByText(/Probe result accepted by owner:me@x.in/)).toBeInTheDocument();
    expect(within(backfill).getByText('State: running')).toBeInTheDocument();
  });

  it.each([
    ['Option chain recorder', 'option chain recorder', 'chain'],
    ['Expired-options backfill', 'expired-options backfill', 'backfill'],
  ])('switches %s on with the password and a reason in one PUT', async (title, lower, kind) => {
    const m = mockRecorders();
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: `Turn on ${title}` }));
    const dialog = await screen.findByRole('dialog', { name: new RegExp(`Turn on the ${lower}`) });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'start');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    await waitFor(() => {
      expect(m.puts).toHaveLength(1);
    });
    expect(m.puts[0]).toEqual({ kind, body: { enabled: true, reason: 'start', password: 'secret-pw' } });
  });

  it('turns the chain recorder off with enabled false', async () => {
    const m = mockRecorders(
      view(),
      buildOptionRecorderView({ enabled: true, switchId: 2, notRunningBecause: [], history: onHistory }),
    );
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off Option chain recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn off the option chain recorder/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'stop');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));
    await waitFor(() => {
      expect(m.puts).toHaveLength(1);
    });
    expect(m.puts[0]).toEqual({ kind: 'chain', body: { enabled: false, reason: 'stop', password: 'secret-pw' } });
  });

  it('turns the opening-minute bars recorder on with its own switch, password and reason', async () => {
    const m = mockRecorders();
    renderServices();
    await openSection();
    const card = await screen.findByRole('region', { name: 'Opening-minute bars recorder' });
    expect(await within(card).findByText('Switch OFF')).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: 'Turn on Opening-minute bars recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn on the opening-minute bars recorder/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'form the opening baskets');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    await waitFor(() => {
      expect(m.puts).toHaveLength(1);
    });
    expect(m.puts[0]).toEqual({
      kind: 'opening',
      body: { enabled: true, reason: 'form the opening baskets', password: 'secret-pw' },
    });
  });
});
