import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { answerLedger, buildLedgerEntry, ledgerIds, sampleLedger } from '@/test/brokerHistoryFixtures';
import { buildDividend, dividendIds } from '@/test/dividendFixtures';
import { holdingIds, ids, sampleHoldings } from '@/test/fixtures';
import { server } from '@/test/server';
import { renderPortfolio, serveDividends, warmDividends } from './testing';

beforeAll(warmDividends, 60_000);

/** The holding search of the form answers with RELIANCE only. */
const relianceRow = sampleHoldings.filter((h) => h.symbol === 'RELIANCE');

const reliance = buildDividend();
const unallocated = buildDividend({
  id: dividendIds.second,
  holdingId: null,
  symbol: null,
  isin: null,
  payDate: '2025-06-10',
  amount: 300,
  perShare: null,
  note: 'Old holding, sold in 2023',
});

const suggested = buildLedgerEntry({
  id: ledgerIds(11),
  date: '2026-09-12',
  amount: 450,
  description: 'Dividend on rented stock',
  narration: 'Lender transfer <i>INFY</i>',
});

function serveLedger(entries = sampleLedger) {
  server.use(
    http.get('/api/accounts/:id/ledger', ({ request }) =>
      HttpResponse.json(answerLedger(entries, new URL(request.url).searchParams)),
    ),
  );
}

async function openPanel(options: Parameters<typeof serveDividends>[0] = {}, search = '?cash=open') {
  const fake = serveDividends(options);
  const view = renderPortfolio(search);
  const panel = within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));
  return { ...view, fake, panel };
}

describe('Cash & dividends panel: what it says about dividends and the ledger', () => {
  it('says that Dhan credits dividends to the bank account, so they are entered by the owner', async () => {
    const { panel } = await openPanel();

    expect(
      panel.getByText(/Dhan credits dividends to your bank account, so they rarely appear here/),
    ).toBeInTheDocument();
    expect(panel.getByText(/enter them yourself/)).toBeInTheDocument();
  });

  it('says from which day the ledger is stored and that costs before it are not counted', async () => {
    serveLedger();
    const { panel } = await openPanel();

    expect(
      await panel.findByText('Ledger read from 5 Oct 2026. Costs before that day are not counted.'),
    ).toBeInTheDocument();
  });

  it('says that no ledger is stored when there is none', async () => {
    const { panel } = await openPanel();

    expect(
      await panel.findByText('No ledger is stored for this account yet, so its costs are not counted.'),
    ).toBeInTheDocument();
  });
});

describe('Cash & dividends panel: the dividends of the account', () => {
  it('lists every holding’s dividends and the unallocated ones, with the holding or "Unallocated"', async () => {
    const { panel } = await openPanel({ dividends: [reliance, unallocated] });

    const table = within(await panel.findByRole('table', { name: 'Dividends of Test Dhan Main' }));
    expect(table.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Pay date',
      'Holding',
      'Amount',
      'Per share',
      'Shares',
      'Note',
      'Action',
    ]);
    const first = within(table.getByRole('row', { name: /5 Dec 2025/ }));
    expect(first.getByText('RELIANCE')).toBeInTheDocument();
    const second = within(table.getByRole('row', { name: /10 Jun 2025/ }));
    expect(second.getByText('Unallocated')).toBeInTheDocument();
    expect(second.getByText('Old holding, sold in 2023')).toBeInTheDocument();
  });

  it('says that none is entered', async () => {
    const { panel } = await openPanel();

    expect(await panel.findByText('No dividends entered for this account yet.')).toBeInTheDocument();
  });

  it('adds an unallocated dividend of the account when no holding is chosen', async () => {
    const { panel, fake } = await openPanel();
    await userEvent.click(await panel.findByRole('button', { name: 'Add dividend' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add dividend' }));

    expect(dialog.getByLabelText('Account')).toHaveValue(ids.main);
    expect(
      dialog.getByText('Without a holding the dividend counts for the account only (unallocated).'),
    ).toBeInTheDocument();
    fireEvent.change(dialog.getByLabelText('Pay date'), { target: { value: '2026-09-01' } });
    fireEvent.change(dialog.getByLabelText('Amount received (₹)'), { target: { value: '321' } });
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Add dividend' })).not.toBeInTheDocument();
    });
    expect(fake.calls.added).toEqual([
      {
        accountId: ids.main,
        holdingId: null,
        payDate: '2026-09-01',
        amount: 321,
        perShare: null,
        note: null,
        confirm: null,
      },
    ]);
    const table = within(await panel.findByRole('table', { name: 'Dividends of Test Dhan Main' }));
    expect(table.getByText('Unallocated')).toBeInTheDocument();
  });

  it('adds a dividend for a holding of the account chosen by searching, sold ones included', async () => {
    const searches: URLSearchParams[] = [];
    const { panel, fake } = await openPanel();
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        searches.push(params);
        return HttpResponse.json({
          items: relianceRow,
          page: 1,
          pageSize: 20,
          total: 1,
        });
      }),
    );
    await userEvent.click(await panel.findByRole('button', { name: 'Add dividend' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add dividend' }));

    await userEvent.click(dialog.getByRole('combobox', { name: 'Holding (optional)' }));
    await userEvent.paste('REL');
    await userEvent.click(await screen.findByRole('option', { name: /RELIANCE/ }));
    fireEvent.change(dialog.getByLabelText('Pay date'), { target: { value: '2026-09-01' } });
    fireEvent.change(dialog.getByLabelText('Amount received (₹)'), { target: { value: '321' } });
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    await waitFor(() => {
      expect(fake.calls.added).toHaveLength(1);
    });
    expect(fake.calls.added[0]).toMatchObject({ accountId: null, holdingId: holdingIds.reliance });
    expect(searches.some((params) => params.get('status') === 'all' && params.get('accountId') === ids.main)).toBe(
      true,
    );
  });

  it('refuses to add without an account, a date and an amount', async () => {
    const { panel, fake } = await openPanel({}, '?cash=open');
    await userEvent.click(await panel.findByRole('button', { name: 'Add dividend' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add dividend' }));
    fireEvent.change(dialog.getByLabelText('Account'), { target: { value: '' } });

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    expect(dialog.getByText('Choose the account.')).toBeInTheDocument();
    expect(fake.calls.added).toEqual([]);
  });
});

describe('Cash & dividends panel: suggestions from the ledger', () => {
  const name = 'suggested dividend of ₹450.00 of 12 Sept 2026';

  it('shows nothing about suggestions when there are none (Dhan’s ledger rarely has one)', async () => {
    const { panel } = await openPanel();
    await panel.findByText('No dividends entered for this account yet.');

    expect(panel.queryByRole('region', { name: 'Suggested dividends' })).not.toBeInTheDocument();
  });

  it('lists the ledger entries that look like a dividend, as plain text, and adds nothing by itself', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });

    const section = within(await panel.findByRole('region', { name: 'Suggested dividends' }));
    expect(
      section.getByText(/1 entry of Dhan's ledger look like a dividend\. Nothing is added until you accept it\./),
    ).toBeInTheDocument();
    expect(section.getByText('Dividend on rented stock')).toBeInTheDocument();
    expect(section.getByText('Lender transfer <i>INFY</i>')).toBeInTheDocument();
    expect(section.getByText('₹450.00')).toBeInTheDocument();
    expect(fake.calls.accepted).toEqual([]);
    expect(fake.calls.added).toEqual([]);
  });

  it('accepts a suggestion for a chosen holding with a note: the amount and date are the entry’s', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    server.use(
      http.get('/api/holdings', () => HttpResponse.json({ items: relianceRow, page: 1, pageSize: 20, total: 1 })),
    );
    await userEvent.click(await panel.findByRole('button', { name: `Accept the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Accept the suggested dividend' }));

    expect(dialog.getByText('₹450.00 on 12 Sept 2026')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('combobox', { name: 'Holding (optional)' }));
    await userEvent.paste('REL');
    await userEvent.click(await screen.findByRole('option', { name: /RELIANCE/ }));
    fireEvent.change(dialog.getByLabelText('Note (optional)'), { target: { value: 'Checked with the bank' } });
    await userEvent.click(dialog.getByRole('button', { name: 'Add as a dividend' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Accept the suggested dividend' })).not.toBeInTheDocument();
    });
    expect(fake.calls.accepted).toEqual([
      { entryId: suggested.id, body: { holdingId: holdingIds.reliance, note: 'Checked with the bank' } },
    ]);
    expect(await screen.findByText('Added the dividend of ₹450.00 on 12 Sept 2026.')).toBeInTheDocument();
    // The suggestion is gone, the section says so, and the dividend is in the list.
    const section = within(await panel.findByRole('region', { name: 'Suggested dividends' }));
    expect(await section.findByText('All suggestions are decided.')).toBeInTheDocument();
    expect(await panel.findByRole('table', { name: 'Dividends of Test Dhan Main' })).toBeInTheDocument();
    await waitFor(() => {
      expect(section.getByRole('heading', { name: 'Suggested dividends' })).toHaveFocus();
    });
  });

  it('accepts without a holding: unallocated', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    await userEvent.click(await panel.findByRole('button', { name: `Accept the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Accept the suggested dividend' }));

    await userEvent.click(dialog.getByRole('button', { name: 'Add as a dividend' }));

    await waitFor(() => {
      expect(fake.calls.accepted).toEqual([{ entryId: suggested.id, body: { holdingId: null, note: null } }]);
    });
  });

  it('dismisses a suggestion after asking, and nothing is added', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    await userEvent.click(await panel.findByRole('button', { name: `Dismiss the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Dismiss this suggestion?' }));
    expect(
      dialog.getByText('It will not be offered again and nothing is added. This cannot be undone.'),
    ).toBeInTheDocument();

    await userEvent.click(dialog.getByRole('button', { name: 'Dismiss' }));

    expect(await screen.findByText('Dismissed the suggested dividend of ₹450.00 of 12 Sept 2026.')).toBeInTheDocument();
    expect(fake.calls.dismissed).toEqual([suggested.id]);
    expect(fake.calls.added).toEqual([]);
    expect(await panel.findByText('All suggestions are decided.')).toBeInTheDocument();
  });

  it('keeps the suggestion when the owner says no to dismissing it', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    await userEvent.click(await panel.findByRole('button', { name: `Dismiss the ${name}` }));

    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Dismiss this suggestion?' })).getByRole('button', {
        name: 'Cancel',
      }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Dismiss this suggestion?' })).not.toBeInTheDocument();
    });
    expect(fake.calls.dismissed).toEqual([]);
    expect(panel.getByRole('button', { name: `Accept the ${name}` })).toBeInTheDocument();
  });

  it('says so when a decision was taken meanwhile (409), and the list is read again', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    fake.config.acceptProblem = {
      status: 409,
      body: { title: 'Already decided.', code: 'portfolio.conflict' },
    };
    await userEvent.click(await panel.findByRole('button', { name: `Accept the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Accept the suggested dividend' }));

    await userEvent.click(dialog.getByRole('button', { name: 'Add as a dividend' }));

    expect(
      await dialog.findByText('This suggestion was decided meanwhile. The list is up to date now.'),
    ).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add as a dividend' })).toBeDisabled();
  });

  it("shows the API's words when it refuses the holding or the note of an accepted suggestion", async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    fake.config.acceptProblem = {
      status: 400,
      body: {
        title: 'The dividend is not valid.',
        code: 'portfolio.invalid',
        errors: { holdingId: ['The holding belongs to another account.'] },
      },
    };
    await userEvent.click(await panel.findByRole('button', { name: `Accept the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Accept the suggested dividend' }));

    await userEvent.click(dialog.getByRole('button', { name: 'Add as a dividend' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent('The holding belongs to another account.');
    expect(dialog.getByRole('button', { name: 'Add as a dividend' })).toBeEnabled();
  });

  it('shows the refusal of the ledger amount (400, field amount) at the Amount field in plain words', async () => {
    const { panel, fake } = await openPanel({ suggestions: [suggested] });
    const words =
      'The ledger amount cannot be entered as a dividend (above 0 and at most 1,00,00,000); dismiss the suggestion and enter the dividend yourself.';
    fake.config.acceptProblem = {
      status: 400,
      body: { title: 'The dividend is not valid.', code: 'portfolio.invalid', errors: { amount: [words] } },
    };
    await userEvent.click(await panel.findByRole('button', { name: `Accept the ${name}` }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Accept the suggested dividend' }));
    expect(dialog.getByLabelText('Amount')).toHaveValue('₹450.00');
    expect(dialog.getByLabelText('Amount')).toBeValid();

    await userEvent.click(dialog.getByRole('button', { name: 'Add as a dividend' }));

    await waitFor(() => {
      expect(dialog.getByLabelText('Amount')).toBeInvalid();
    });
    expect(dialog.getByLabelText('Amount')).toHaveAccessibleDescription(words);
    // Said once, at the field: no second banner repeating it.
    expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows an error with Retry when the suggestions cannot be loaded', async () => {
    serveDividends();
    let fail = true;
    server.use(
      http.get('/api/accounts/:id/dividends/suggestions', () =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ items: [suggested], page: 1, pageSize: 50, total: 1 }),
      ),
    );
    renderPortfolio('?cash=open');
    const panel = within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));
    expect(await panel.findByText('The suggested dividends could not be loaded.')).toBeInTheDocument();

    fail = false;
    await userEvent.click(panel.getByRole('button', { name: 'Retry' }));

    expect(await panel.findByRole('region', { name: 'Suggested dividends' })).toBeInTheDocument();
  });
});
