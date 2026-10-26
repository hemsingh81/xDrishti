import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { GovernancePage } from './components/GovernancePage';

pinClock('2026-11-10T06:30:00Z');

const config = (overrides: Partial<Schemas['ConfigView']> = {}): Schemas['ConfigView'] => ({
  basis: 'configuration governance',
  currentHash: '9f3c0a1b2c3d',
  last: { hash: '1a2b3c4d5e6f', takenAt: '2026-11-09T12:00:00Z', sessionDate: '2026-11-09' },
  components: [{ key: 'autonomy.max_auto_per_day', rule: 'raising is frozen', value: '3' }],
  pending: [],
  blockers: [],
  unreadable: [],
  ...overrides,
});

const epochs = (items: Schemas['EpochView'][] = []): Schemas['EpochList'] => ({ basis: 'b', items });

function mount(
  options: {
    config?: Schemas['ConfigView'];
    epochs?: Schemas['EpochList'];
    queue?: Schemas['QueueView'];
    fail?: boolean;
  } = {},
) {
  server.use(
    http.get('/api/paper/config', () =>
      options.fail
        ? HttpResponse.json({ title: 'down' }, { status: 500 })
        : HttpResponse.json(options.config ?? config()),
    ),
    http.get('/api/paper/epochs', () => HttpResponse.json(options.epochs ?? epochs())),
    http.get('/api/paper/queue', () =>
      HttpResponse.json(
        options.queue ?? {
          note: 'accepted means judged better offline; nothing was applied; applying it starts a new epoch',
          items: [],
          truncated: false,
        },
      ),
    ),
  );
  return renderRoutes([{ path: '/paper/governance', element: <GovernancePage /> }], {
    initialPath: '/paper/governance',
  });
}

describe('the Governance tab', () => {
  it('shows the empty states and the queue sentence, and offers no control that writes', async () => {
    mount();

    expect(await screen.findByText(/No capture has run yet/)).toBeInTheDocument();
    expect(await screen.findByText(/No accepted hypothesis is waiting/)).toBeInTheDocument();
    expect(screen.getByText(/nothing was applied; applying it starts a new epoch/)).toBeInTheDocument();
    expect(await screen.findByText(/None: the configuration equals the last snapshot/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('marks a pending frozen difference as one that would restart the count and lists the epochs and the queue', async () => {
    mount({
      config: config({
        pending: [
          { key: 'models.champion', class: 'frozen', before: '1', after: '2', meaning: 'would restart the count' },
        ],
      }),
      epochs: epochs([
        {
          id: 2,
          kind: 'detected_change',
          startedOn: '2026-11-11',
          endedOn: null,
          fingerprint: 'abc',
          counted: 0,
          straddle: 1,
          riskDownChanges: 0,
          unexplainedChanges: 1,
          reason: 'x',
        },
        {
          id: 1,
          kind: 'first',
          startedOn: '2026-11-02',
          endedOn: '2026-11-11',
          fingerprint: null,
          counted: 12,
          straddle: 0,
          riskDownChanges: 1,
          unexplainedChanges: 0,
          reason: 'y',
        },
      ]),
      queue: {
        note: 'accepted means judged better offline; nothing was applied; applying it starts a new epoch',
        items: [
          {
            hypothesisId: 9,
            ageDays: 3,
            cellKey: 'ORB|15m|BUY|INTRADAY|FIXED_RR{rr=2.0}|*',
            knob: 'rr',
            from: '2.0',
            to: '2.5',
            experimentId: 4,
          },
        ],
        truncated: false,
      },
    });

    expect(await screen.findByText('would restart the count')).toBeInTheDocument();
    expect(await screen.findByText('detected_change')).toBeInTheDocument();
    expect(screen.getByText('in force')).toBeInTheDocument();
    expect(await screen.findByText('H9')).toBeInTheDocument();
  });

  it('shows the learning services that would restart the count and the groups that could not be read', async () => {
    mount({
      config: config({
        blockers: [{ serviceId: 'cells', text: 'enabled during an epoch: each run restarts the count' }],
        unreadable: ['accounts'],
      }),
    });

    expect(await screen.findByText(/Service cells: enabled during an epoch/)).toBeInTheDocument();
    expect(screen.getByText(/Could not be read: accounts/)).toBeInTheDocument();
  });

  it('shows an error when the configuration cannot be read', async () => {
    mount({ fail: true });

    expect(await screen.findAllByRole('alert')).not.toHaveLength(0);
  });
});
