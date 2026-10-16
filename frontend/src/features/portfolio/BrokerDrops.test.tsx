import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildBrokerCounts,
  buildBrokerKind,
  buildBrokerRun,
  buildBrokerStatus,
  buildDroppedRow,
  buildDroppingKind,
} from '@/test/brokerHistoryFixtures';
import { pinClock } from '@/test/pinClock';
import { server } from '@/test/server';
import { renderPortfolio } from './testing';

// The lines show when the read ended ("… ago"), so the clock is pinned.
pinClock('2026-10-11T10:00:00Z');

const statusPath = '/api/accounts/:id/sync/broker-history/status';
const region = async () => within(await screen.findByRole('region', { name: 'Trades and ledger from Dhan' }));
const answer = (trades = buildBrokerKind(), ledger = buildBrokerKind()) => {
  server.use(http.get(statusPath, () => HttpResponse.json(buildBrokerStatus({ trades, ledger }))));
};

describe('Rows a trades or ledger read could not use', () => {
  it('says how many rows could not be read and why, largest reason first, after the run summary', async () => {
    answer(
      buildDroppingKind({ bad_time: 2, bad_quantity: 9, missing_id: 4 }, [
        buildDroppedRow(),
        buildDroppedRow({ page: 1, rowIndex: 0, reason: 'bad_quantity' }),
      ]),
    );
    renderPortfolio();

    const line = await region();
    expect(
      await line.findByText(
        '15 rows could not be read: 9 quantity missing or not a whole positive number, 4 no trade id, 2 time unreadable',
      ),
    ).toBeInTheDocument();
  });

  it('names three reasons and counts the rest', async () => {
    answer(buildDroppingKind({ bad_quantity: 9, missing_id: 4, bad_time: 2, bad_price: 1 }));
    renderPortfolio();

    const line = await region();
    expect(await line.findByText(/2 time unreadable, \+1 more/)).toBeInTheDocument();
    expect(line.queryByText(/1 price unreadable/)).not.toBeInTheDocument();
  });

  it('mentions the lines where no money moved quietly, without a warning or details', async () => {
    answer(buildBrokerKind(), buildDroppingKind({ zero_amount: 1 }, [buildDroppedRow({ reason: 'zero_amount' })]));
    renderPortfolio();

    const line = await region();
    const note = await line.findByText(/lines where no money moved ignored/);
    // Muted like the "Next scheduled read" line, not the body colour of a warning.
    expect(note).toHaveStyle({ color: getComputedStyle(line.getByText(/Next scheduled read/)).color });
    expect(line.queryByText(/could not be read/)).not.toBeInTheDocument();
    expect(line.queryByRole('button', { name: /Details/ })).not.toBeInTheDocument();
  });

  it.each([
    ['dropped nothing', buildBrokerKind()],
    ['dropped nothing, with empty reasons', buildDroppingKind({})],
    [
      'comes from an older run without the fields',
      buildBrokerKind({ lastRun: buildBrokerRun({ counts: buildBrokerCounts({ dropped: 2 }) }) }),
    ],
  ])('shows nothing when the run %s', async (_name, kind) => {
    answer(kind, kind);
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Trades read up to 11 Oct 2026')).toBeInTheDocument();
    expect(line.queryByText(/could not be read|no money moved/)).not.toBeInTheDocument();
    expect(line.queryByRole('button', { name: /Details/ })).not.toBeInTheDocument();
  });

  it('opens the places of the rows with the keyboard, and says no names or amounts are shown', async () => {
    const user = userEvent.setup();
    answer(
      buildDroppingKind({ bad_price: 7, missing_id: 3 }, [
        buildDroppedRow(),
        buildDroppedRow({
          page: 1,
          rowIndex: 4,
          reason: 'missing_id',
          windowFrom: '2026-10-01',
          windowTo: '2026-10-11',
        }),
      ]),
    );
    renderPortfolio();

    const line = await region();
    const details = await line.findByRole('button', { name: 'Details of the trades rows that could not be read' });
    expect(details).toHaveAttribute('aria-expanded', 'false');
    // The list is not in the page while closed, so the button points at nothing.
    expect(details).not.toHaveAttribute('aria-controls');
    expect(line.queryByText(/window 1 Aug–31 Aug/)).not.toBeInTheDocument();

    await user.tab();
    details.focus();
    expect(details).toHaveFocus();
    await user.keyboard('{Enter}');

    expect(details).toHaveAttribute('aria-expanded', 'true');
    expect(details.getAttribute('aria-controls')).toBe(line.getByRole('list').closest('[id]')?.id);
    const places = within(await line.findByRole('list'));
    expect(places.getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'window 1 Aug–31 Aug, page 1, row 3: price unreadable',
      'window 1 Oct–11 Oct, page 2, row 5: no trade id',
    ]);
    expect(line.getByText(/never its names or amounts/)).toBeInTheDocument();
    expect(line.getByText(/The first 2 of 10 rows\./)).toBeInTheDocument();

    await user.keyboard(' ');
    expect(details).toHaveAttribute('aria-expanded', 'false');
    await waitFor(() => {
      expect(line.queryByRole('list')).not.toBeInTheDocument();
    });
  });

  it('leaves the page out of a ledger place and keeps trades and ledger apart', async () => {
    const user = userEvent.setup();
    answer(
      buildBrokerKind(),
      buildDroppingKind({ bad_date: 1 }, [buildDroppedRow({ page: 0, rowIndex: 0, reason: 'bad_date' })]),
    );
    renderPortfolio();

    const line = await region();
    expect(line.queryByRole('button', { name: /trades rows/ })).not.toBeInTheDocument();
    await user.click(await line.findByRole('button', { name: 'Details of the ledger rows that could not be read' }));
    expect(await line.findByText('window 1 Aug–31 Aug, row 1: date unreadable')).toBeInTheDocument();
  });

  it('shows what the API sends as plain text and never a code or markup', async () => {
    const user = userEvent.setup();
    answer(
      buildDroppingKind({ '<img src=x onerror=alert(1)>': 2 }, [
        buildDroppedRow({ reason: '<img src=x onerror=alert(1)>' }),
      ]),
    );
    renderPortfolio();

    const line = await region();
    expect(await line.findByText(/2 rows could not be read: 2 other/)).toBeInTheDocument();
    await user.click(line.getByRole('button', { name: /Details of the trades rows/ }));
    expect(await line.findByText('window 1 Aug–31 Aug, page 1, row 3: other')).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    expect(line.queryByText(/onerror/)).not.toBeInTheDocument();
  });
});
