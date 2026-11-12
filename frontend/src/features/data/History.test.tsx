import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, type JsonBodyType } from 'msw';
import { pendingResponse } from '@/test/gate';
import { problemResponse } from '@/test/server';
import { renderData } from './testing';

const chunks = (done: number, failed: number, total: number) => ({ done, failed, total });
const noProbe = { status: 'none', at: null, medianBps: null, withinMaxPct: null };

const row = (symbol: string, over: Record<string, unknown> = {}) => ({
  instrumentId: `00000000-0000-4000-8000-00000000000${String(symbol.length)}`,
  symbol,
  historyFrom: '2021-11-15T09:15:00+05:30',
  firstCsvBar: '2024-01-02T09:15:00+05:30',
  backfillStatus: 'done',
  chunks: chunks(10, 0, 10),
  probe: noProbe,
  seamJumpPct: null,
  ...over,
});

const overview = (over: Record<string, unknown> = {}) => ({
  switch: { enabled: false, editable: false, reason: 'Data:History:Backfill:Enabled is false.' },
  runWindow: '20:00-06:00',
  inWindow: false,
  running: false,
  serviceId: 'history-backfill',
  daily: { from: '2019-01-01', to: '2026-11-10' },
  totals: {
    instruments: 4,
    byStatus: { none: 0, planned: 0, partial: 1, done: 1, seam_unresolved: 1, seam_jump: 1 },
    chunks: chunks(24, 1, 40),
    years: { under2: 1, from2to3: 1, from3to5: 1, from5: 1 },
  },
  instruments: [
    row('AAA'),
    row('BBBB', { backfillStatus: 'partial', chunks: chunks(4, 1, 10) }),
    row('CCCCC', {
      backfillStatus: 'seam_unresolved',
      probe: { status: 'unresolved', at: '2026-11-10T22:00:00+05:30', medianBps: 12.5, withinMaxPct: 40 },
    }),
    row('DD', { backfillStatus: 'seam_jump', seamJumpPct: 32 }),
  ],
  ...over,
});

const on = { enabled: true, editable: false, reason: null };

const serve = (body: JsonBodyType) => http.get('/api/data/history', () => HttpResponse.json(body));
const renderHistory = (body: JsonBodyType = overview()) =>
  renderData({ search: '?tab=history', handlers: [serve(body)] });

describe('Data History tab', () => {
  it('shows a loading state while the history is read', async () => {
    renderData({ search: '?tab=history', handlers: [http.get('/api/data/history', () => pendingResponse())] });
    expect(await screen.findByText('Loading history')).toBeInTheDocument();
  });

  it('shows an error with a retry', async () => {
    renderData({
      search: '?tab=history',
      handlers: [http.get('/api/data/history', () => problemResponse({ title: 'History is down.' }, 500))],
    });
    expect(await screen.findByText('History is down.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('says when no instrument has CSV bars', async () => {
    renderHistory(overview({ instruments: [] }));
    expect(await screen.findByText('No tracked instrument has CSV bars yet.')).toBeInTheDocument();
  });

  it('shows the OFF banner and disables both acts with the reason as their description', async () => {
    renderHistory();
    expect(
      await screen.findByText(
        'The 1-minute backfill is off. It stays off until source-aware replay (CF-04d) is built.',
      ),
    ).toBeInTheDocument();
    for (const name of ['Start backfill', 'Probe seam']) {
      const button = screen.getByRole('button', { name });
      expect(button).toBeDisabled();
      expect(button).toHaveAccessibleDescription('Data:History:Backfill:Enabled is false.');
    }
  });

  it('lists every instrument with its status in words', async () => {
    renderHistory();
    const table = await screen.findByRole('table', { name: 'History per instrument' });
    expect(within(table).getByRole('rowheader', { name: 'AAA' })).toBeInTheDocument();
    expect(within(table).getByText('Seam jump')).toBeInTheDocument();
    expect(within(table).getByText('Jump of 32.0% at the seam')).toBeInTheDocument();
    expect(within(table).getByText('Seam unresolved')).toBeInTheDocument();
    expect(within(table).getByText(/Unresolved .*median 12.5 bps/)).toBeInTheDocument();
    expect(within(table).getByRole('progressbar', { name: 'BBBB chunks done' })).toBeInTheDocument();
    expect(within(table).getByText('4/10 done, 1 failed')).toBeInTheDocument();
    expect(screen.getByText(/Limits:/)).toBeInTheDocument();
  });

  describe('owner acts (switch on)', () => {
    const post = (path: string, respond: () => Response, seen?: (body: unknown) => void) =>
      http.post(path, async ({ request }) => {
        seen?.(await request.json());
        return respond();
      });

    it('starts the backfill after a fresh password and links to Services', async () => {
      let body: unknown;
      renderData({
        search: '?tab=history',
        handlers: [
          serve(overview({ switch: on })),
          post(
            '/api/data/history/backfill',
            () =>
              HttpResponse.json(
                {
                  started: true,
                  runId: 'r1',
                  chunksPending: 23,
                  estimatedRows: 1,
                  estimatedRequests: 23,
                  estimatedMinutes: 5,
                },
                { status: 202 },
              ),
            (b) => {
              body = b;
            },
          ),
        ],
      });
      await userEvent.click(await screen.findByRole('button', { name: 'Start backfill' }));
      const dialog = await screen.findByRole('dialog', { name: 'Start backfill' });
      await userEvent.type(within(dialog).getByLabelText(/password/i), 's3cret');
      await userEvent.click(within(dialog).getByRole('button', { name: 'Start backfill' }));
      expect(await screen.findByText(/Backfill started: 23 chunks pending/)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Services screen' })).toHaveAttribute('href', '/services');
      expect(body).toEqual({ password: 's3cret' });
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it.each([
      ['history.outside_window', 422, 'It can only run inside its time window. Next window starts 20:00.'],
      ['history.run_active', 409, 'A backfill or a probe is already running. Wait for it to finish.'],
      ['history.disk_low', 422, 'There is not enough free disk'],
      ['history.calendar_missing', 422, 'The trading calendar has gaps'],
      ['history.no_target', 404, 'There is no instrument to fetch history for.'],
      ['history.disabled', 422, 'The 1-minute backfill is switched off'],
      ['data.dhan_not_connected', 409, 'Dhan is not connected for the Data account.'],
      ['auth.invalid_credentials', 401, 'That password is not right.'],
    ])('shows %s in the dialog in plain words', async (code, status, text) => {
      renderData({
        search: '?tab=history',
        handlers: [
          serve(overview({ switch: on })),
          http.post('/api/data/history/backfill', () =>
            problemResponse(
              {
                title: 'Refused.',
                code,
                detail: code === 'history.outside_window' ? 'Next window starts 20:00.' : undefined,
              },
              status,
            ),
          ),
        ],
      });
      await userEvent.click(await screen.findByRole('button', { name: 'Start backfill' }));
      const dialog = await screen.findByRole('dialog');
      await userEvent.type(within(dialog).getByLabelText(/password/i), 'pw');
      await userEvent.click(within(dialog).getByRole('button', { name: 'Start backfill' }));
      expect(await within(dialog).findByRole('alert')).toHaveTextContent(text);
    });

    it('clears the password when the dialog is closed and opened again', async () => {
      renderHistory(overview({ switch: on }));
      await userEvent.click(await screen.findByRole('button', { name: 'Probe seam' }));
      await userEvent.type(within(await screen.findByRole('dialog')).getByLabelText(/password/i), 'pw');
      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      await userEvent.click(screen.getByRole('button', { name: 'Probe seam' }));
      expect(within(await screen.findByRole('dialog')).getByLabelText(/password/i)).toHaveValue('');
    });

    it('shows the probe result with its lines', async () => {
      renderData({
        search: '?tab=history',
        handlers: [
          serve(overview({ switch: on })),
          http.post('/api/data/history/probe', () =>
            HttpResponse.json({
              probed: 3,
              passed: 2,
              unresolved: 1,
              noOverlap: 0,
              failed: 0,
              requests: 3,
              lines: ['AAA passed', 'BBBB unresolved'],
            }),
          ),
        ],
      });
      await userEvent.click(await screen.findByRole('button', { name: 'Probe seam' }));
      const dialog = await screen.findByRole('dialog', { name: 'Probe seam' });
      await userEvent.type(within(dialog).getByLabelText(/password/i), 'pw');
      await userEvent.click(within(dialog).getByRole('button', { name: 'Probe seam' }));
      expect(await screen.findByText(/Probed 3 instruments: 2 passed, 1 unresolved/)).toBeInTheDocument();
      expect(screen.getByText('BBBB unresolved')).toBeInTheDocument();
    });
  });
});
