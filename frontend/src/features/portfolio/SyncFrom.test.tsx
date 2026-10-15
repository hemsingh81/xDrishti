import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  brokerRunIds,
  buildBrokerCounts,
  buildBrokerKind,
  buildBrokerRun,
  buildBrokerStatus,
  buildRunningStatus,
} from '@/test/brokerHistoryFixtures';
import { buildAccountDetail, buildConnection, summaryOf, withConnection } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { pinClock } from '@/test/pinClock';
import { renderPortfolio } from './testing';

const statusPath = '/api/accounts/:id/sync/broker-history/status';
const syncPath = '/api/accounts/:id/sync/broker-history';

const line = async () => within(await screen.findByRole('region', { name: 'Trades and ledger from Dhan' }));
const earlierButton = () => screen.findByRole('button', { name: 'Read from an earlier date' });
const dialogOf = async () => within(await screen.findByRole('dialog', { name: 'Read from an earlier date' }));

/** Takes the POSTs of "Sync now" for the trades and the ledger, remembering what the app sent. */
function captureSyncs(answer?: () => Response) {
  const bodies: string[] = [];
  server.use(
    http.post(syncPath, async ({ request }) => {
      bodies.push(await request.text());
      return answer ? answer() : HttpResponse.json({ trades: buildRunningStatus().trades.lastRun, ledger: null });
    }),
  );
  return bodies;
}

// The dates typed here are past days of October 2026 and a form refuses a future date, so the clock is pinned.
pinClock('2026-10-14T06:00:00Z');

describe('Sync trades & ledger: read from an earlier date', () => {
  it('opens a small dialog with the date field focused and says that Dhan does not state how far back it goes', async () => {
    renderPortfolio();

    await userEvent.click(await earlierButton());

    const dialog = await dialogOf();
    expect(dialog.getByText('Test Dhan Main')).toBeInTheDocument();
    expect(dialog.getByText(/Dhan does not say how far back its history goes/)).toBeInTheDocument();
    expect(dialog.getByText(/nothing is stored twice/)).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getByLabelText('Read from')).toHaveFocus();
    });
    expect(dialog.getByLabelText('Read from')).toHaveAttribute('type', 'date');
    expect(dialog.getByLabelText('Read from')).toHaveAttribute('min', '1990-01-02');
  });

  it('sends the date as {from}, closes, and the lines show the read running', async () => {
    const bodies = captureSyncs();
    let running = false;
    server.use(http.get(statusPath, () => HttpResponse.json(running ? buildRunningStatus() : buildBrokerStatus())));
    renderPortfolio();
    await userEvent.click(await earlierButton());
    const dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2023-04-01' } });
    running = true;

    await userEvent.click(dialog.getByRole('button', { name: 'Read from this date' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Read from an earlier date' })).not.toBeInTheDocument();
    });
    expect(bodies.map((body) => JSON.parse(body) as unknown)).toEqual([{ from: '2023-04-01' }]);
    expect(await (await line()).findByText('Reading trades…')).toBeInTheDocument();
  });

  it('is sent with Enter from the date field too', async () => {
    const bodies = captureSyncs();
    renderPortfolio();
    await userEvent.click(await earlierButton());
    const dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2023-04-01' } });

    await userEvent.type(dialog.getByLabelText('Read from'), '{Enter}');

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
  });

  it('leaves the plain Sync trades & ledger as it was: no body at all', async () => {
    const bodies = captureSyncs();
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Sync trades & ledger' }));

    await waitFor(() => {
      expect(bodies).toEqual(['']);
    });
  });

  it.each([
    ['no date', '', 'Enter the first date to read.'],
    ['a date in the future', '2999-01-01', 'The date cannot be in the future.'],
    ['a date before 1990', '1989-12-31', 'The date cannot be before 2 Jan 1990.'],
  ])('refuses %s without asking the server, and keeps the keyboard on the field', async (_name, value, message) => {
    const bodies = captureSyncs();
    renderPortfolio();
    await userEvent.click(await earlierButton());
    const dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value } });

    await userEvent.click(dialog.getByRole('button', { name: 'Read from this date' }));

    expect(dialog.getByText(message)).toBeInTheDocument();
    expect(dialog.getByLabelText('Read from')).toHaveFocus();
    expect(dialog.getByLabelText('Read from')).toBeInvalid();
    expect(bodies).toEqual([]);
  });

  it("shows the API's refusal of the date at the field (400 on from), keeps the dialog open and clears it when the date changes", async () => {
    const bodies = captureSyncs(() =>
      problemResponse(
        {
          title: 'The read cannot start there.',
          code: 'portfolio.invalid',
          errors: { from: ['Start on or before 12 Oct 2026, the day after the trades were last read.'] },
        },
        400,
      ),
    );
    renderPortfolio();
    await userEvent.click(await earlierButton());
    const dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2026-10-13' } });

    await userEvent.click(dialog.getByRole('button', { name: 'Read from this date' }));

    expect(
      await dialog.findByText('Start on or before 12 Oct 2026, the day after the trades were last read.'),
    ).toBeInTheDocument();
    expect(dialog.getByLabelText('Read from')).toBeInvalid();
    expect(bodies).toHaveLength(1);
    // The message is at the field, not repeated as a banner on the page or in the dialog.
    expect(screen.queryByText('The read did not start.')).not.toBeInTheDocument();
    expect(screen.getAllByText(/Start on or before 12 Oct 2026/)).toHaveLength(1);

    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2026-10-10' } });
    await waitFor(() => {
      expect(dialog.queryByText(/Start on or before 12 Oct 2026/)).not.toBeInTheDocument();
    });
  });

  it('says in plain words, with the way out, when the sign-in has expired', async () => {
    captureSyncs(() => problemResponse({ title: 'The Dhan token has expired.', code: 'portfolio.token_expired' }, 409));
    renderPortfolio();
    await userEvent.click(await earlierButton());
    const dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2023-04-01' } });

    await userEvent.click(dialog.getByRole('button', { name: 'Read from this date' }));

    expect(await dialog.findByText(/The Dhan sign-in of this account has expired\./)).toBeInTheDocument();
    expect(dialog.getByRole('link', { name: 'Open the Connection tab' })).toBeInTheDocument();
    // Said once: the page behind the dialog does not repeat it.
    expect(screen.getAllByText(/The Dhan sign-in of this account has expired\./)).toHaveLength(1);
  });

  it("names Dhan's refusal of the range in the lines of the read, in the words of the run", async () => {
    const refused = buildBrokerStatus({
      trades: buildBrokerKind({
        lastRun: buildBrokerRun({
          id: brokerRunIds.trades2,
          status: 'failed',
          from: '2015-01-01',
          error: 'Dhan refused the date range: the history is too large. Try a later start date.',
          counts: buildBrokerCounts({ stored: 0 }),
        }),
      }),
    });
    server.use(http.get(statusPath, () => HttpResponse.json(refused)));
    renderPortfolio();

    const lines = await line();
    expect(await lines.findByText('Trades read failed')).toBeInTheDocument();
    expect(
      lines.getByText(/Dhan refused the date range: the history is too large\. Try a later start date\./),
    ).toBeInTheDocument();
    expect(lines.getByRole('alert')).toHaveTextContent('Trades read failed');
  });

  it('closes with Cancel, sends nothing and forgets an earlier refusal', async () => {
    const bodies = captureSyncs(() =>
      problemResponse({ title: 'Nope.', code: 'portfolio.invalid', errors: { from: ['Too late.'] } }, 400),
    );
    renderPortfolio();
    await userEvent.click(await earlierButton());
    let dialog = await dialogOf();
    fireEvent.change(dialog.getByLabelText('Read from'), { target: { value: '2023-04-01' } });
    await userEvent.click(dialog.getByRole('button', { name: 'Read from this date' }));
    await dialog.findByText('Too late.');

    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Read from an earlier date' })).not.toBeInTheDocument();
    });
    expect(screen.queryByText('Too late.')).not.toBeInTheDocument();

    await userEvent.click(await earlierButton());
    dialog = await dialogOf();
    expect(dialog.queryByText('Too late.')).not.toBeInTheDocument();
    expect(bodies).toHaveLength(1);
  });

  it('is off while the account is not connected or a read is running', async () => {
    const expired = withConnection(buildAccountDetail(), buildConnection({ status: 'expired' }));
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(expired)])));
    renderPortfolio();
    expect(await earlierButton()).toBeDisabled();
  });

  it('is off while a read is running', async () => {
    server.use(http.get(statusPath, () => HttpResponse.json(buildRunningStatus())));
    renderPortfolio();
    await screen.findByText('Reading trades…');
    expect(await earlierButton()).toBeDisabled();
  });
});
