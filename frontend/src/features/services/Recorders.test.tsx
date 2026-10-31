import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { problemResponse, server } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import { buildServiceView } from '@/test/servicesFixtures';
import { renderServices } from './testing';

vi.mock('./lib/polling', () => ({ servicesRunningPollMs: 25, servicesIdlePollMs: 60_000 }));

const view = (over: object = {}) => ({
  enabled: false,
  switchId: null,
  state: null,
  lastSeenAt: null,
  notRunningBecause: ['Your switch is OFF.', 'The service tick-recorder is paused on the Services screen.'],
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

function mockRecorders(depth = view(), ticks = view()) {
  const puts: Put[] = [];
  const reads: string[] = [];
  let refuse = false;
  server.use(
    http.get('/api/depth/recorder', () => {
      reads.push('depth');
      return HttpResponse.json(depth);
    }),
    http.get('/api/ticks/recorder', () => {
      reads.push('ticks');
      return HttpResponse.json(ticks);
    }),
    ...(['depth', 'ticks'] as const).map((kind) =>
      http.put(`/api/${kind}/recorder`, async ({ request }) => {
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
  return screen.findByRole('region', { name: 'Trade tick recorder' });
}

describe('Services: recorders', () => {
  it('shows both recorders off, with the reasons and the notice, and reads nothing until opened', async () => {
    const m = mockRecorders();
    renderServices();
    await screen.findByRole('list', { name: 'Services' });
    expect(m.reads).toEqual([]);

    const ticks = await openSection();
    const depth = await screen.findByRole('region', { name: 'Depth recorder' });
    expect(await within(ticks).findByText('Switch OFF')).toBeInTheDocument();
    expect(within(ticks).getByText('Your switch is OFF.')).toBeInTheDocument();
    expect(within(ticks).getByText(/Off until the deployment flag/)).toBeInTheDocument();
    expect(within(depth).getByRole('button', { name: 'Turn on Depth recorder' })).toBeInTheDocument();
    expect(within(ticks).getByText('The switch was never changed.')).toBeInTheDocument();
  });

  it('shows an ON recorder with its state and history', async () => {
    mockRecorders(
      view(),
      view({ enabled: true, switchId: 2, state: 'recording', notRunningBecause: [], history: onHistory }),
    );
    renderServices();
    const ticks = await openSection();
    expect(await within(ticks).findByText('Switch ON')).toBeInTheDocument();
    expect(within(ticks).getByText('State: recording')).toBeInTheDocument();
    expect(within(ticks).getByText('pilot day')).toBeInTheDocument();
    expect(within(ticks).getByText(/Turned on by owner:me@x.in/)).toBeInTheDocument();
    expect(within(ticks).getByRole('button', { name: 'Turn off Trade tick recorder' })).toBeInTheDocument();
  });

  it('sends nothing before confirm or after cancel, then one PUT with the password only in that body', async () => {
    const m = mockRecorders();
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on Trade tick recorder' }));
    let dialog = await screen.findByRole('dialog', { name: /Turn on the trade tick recorder/ });
    expect(within(dialog).getByRole('button', { name: 'Turn on' })).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'pilot day');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    expect(m.puts).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(m.puts).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Turn on Trade tick recorder' }));
    dialog = await screen.findByRole('dialog', { name: /Turn on the trade tick recorder/ });
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    expect(within(dialog).getByLabelText(/Reason/)).toHaveValue('');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'pilot day');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn on' }));
    expect(await screen.findByText('The switch was changed.')).toBeInTheDocument();
    expect(m.puts).toEqual([{ kind: 'ticks', body: { enabled: true, reason: 'pilot day', password: 'secret-pw' } }]);
  });

  it('turns an ON recorder off with enabled false', async () => {
    const m = mockRecorders(view({ enabled: true, switchId: 2, history: onHistory, notRunningBecause: [] }));
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off Depth recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn off the depth recorder/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'done piloting');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'secret-pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));
    await waitFor(() => {
      expect(m.puts).toHaveLength(1);
    });
    expect(m.puts[0]).toEqual({
      kind: 'depth',
      body: { enabled: false, reason: 'done piloting', password: 'secret-pw' },
    });
  });

  it('shows the server refusal only in the dialog, sends no second PUT, and clears the fields on reopen', async () => {
    const m = mockRecorders();
    m.refuse();
    renderServices();
    await openSection();
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on Depth recorder' }));
    const dialog = await screen.findByRole('dialog', { name: /Turn on the depth recorder/ });
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
    await userEvent.click(screen.getByRole('button', { name: 'Turn on Depth recorder' }));
    const again = await screen.findByRole('dialog', { name: /Turn on the depth recorder/ });
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
    server.use(http.get('/api/depth/recorder', () => problemResponse({ title: 'Down', code: 'x' }, 500)));
    renderServices();
    const ticks = await openSection();
    expect(await within(ticks).findByText('Switch OFF')).toBeInTheDocument();
    const depth = screen.getByRole('region', { name: 'Depth recorder' });
    expect(await within(depth).findByRole('alert')).toBeInTheDocument();
  });
});

describe('Services: the tick recorder service', () => {
  const tick = buildServiceView({
    id: 'tick-recorder',
    name: 'Trade tick recorder',
    enabled: false,
    status: 'paused',
    scheduleWords: 'Weekdays 09:14 to 15:31 IST',
  });

  it('is listed, and Run now shows the refusal the server gives', async () => {
    const fake = createServicesFake({ services: [tick] });
    renderServices({
      fake,
      handlers: [
        http.post('/api/services/:id/run', () =>
          problemResponse(
            { title: 'This service is started by its own host.', code: 'services.run_now_unavailable' },
            409,
          ),
        ),
      ],
    });
    const card = await screen.findByRole('listitem', { name: 'Trade tick recorder' });
    await userEvent.click(within(card).getByRole('button', { name: 'Run now: Trade tick recorder' }));
    expect(await screen.findByText('This service is started by its own host.')).toBeInTheDocument();
  });
});
