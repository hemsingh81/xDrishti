import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { answerLedger, buildLedgerEntry, ledgerIds, sampleLedger } from '@/test/brokerHistoryFixtures';
import { ids } from '@/test/fixtures';
import { server } from '@/test/server';
import { smallPage, threePages } from '@/test/smallPages';
import { renderPortfolio, twoPortfolioAccounts } from './testing';

// The list shows 50 entries a page in the app; here it pages by a few (the 50 is the constant `ledgerPageSize`).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const ledgerPath = '/api/accounts/:id/ledger';

/** Serves `entries` and remembers the parameters of every request. */
function serveLedger(entries = sampleLedger) {
  const requests: URLSearchParams[] = [];
  server.use(
    http.get(ledgerPath, ({ request, params }) => {
      const query = new URL(request.url).searchParams;
      requests.push(query);
      expect(params.id).toBe(ids.main);
      return HttpResponse.json(answerLedger(entries, query));
    }),
  );
  return requests;
}

async function openPanel() {
  renderPortfolio();
  await userEvent.click(await screen.findByRole('button', { name: 'Cash & dividends' }));
  return within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));
}

describe('Cash & dividends panel', () => {
  it('shows the totals of the five classes, the list newest first, and that Dhan rarely lists dividends', async () => {
    serveLedger();
    const panel = await openPanel();

    expect(
      panel.getByText(/Dhan credits dividends to your bank account, so they rarely appear here/),
    ).toBeInTheDocument();
    expect(panel.getByText(/enter them yourself/)).toBeInTheDocument();
    const totals = within(await panel.findByRole('region', { name: 'Totals by class' }));
    for (const [label, amount, hint] of [
      ['Funds in', '+₹20,000.00', '1 entry'],
      ['Funds out', '−₹3,000.00', '1 entry'],
      ['Dividends', '+₹840.00', '1 entry'],
      ['Charges', '−₹15.93', '1 entry'],
      ['Other', '+₹1.00', '1 entry'],
    ] as const) {
      expect(totals.getByText(label)).toBeInTheDocument();
      expect(totals.getByText(amount)).toBeInTheDocument();
      expect(totals.getAllByText(hint).length).toBeGreaterThan(0);
    }
    const table = within(await panel.findByRole('table', { name: 'Ledger entries' }));
    const rows = table.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(5);
    expect(within(rows[0]!).getByText('9 Oct 2026')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('+₹840.00')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('₹25,840.00')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('−₹15.93')).toBeInTheDocument();
    expect(within(rows[4]!).getByText('5 Oct 2026')).toBeInTheDocument();
  });

  it('shows narrations as plain text, never as markup', async () => {
    serveLedger();
    const panel = await openPanel();

    expect(await panel.findByText('<b>Withdrawal</b> to bank')).toBeInTheDocument();
    expect(document.querySelector('b')).toBeNull();
  });

  it('narrows the list to a class while the totals keep following the period only', async () => {
    const requests = serveLedger();
    const panel = await openPanel();
    await panel.findByRole('table', { name: 'Ledger entries' });

    await userEvent.selectOptions(panel.getByLabelText('Class'), 'dividend');

    await vi.waitFor(() => {
      expect(requests.at(-1)?.get('class')).toBe('dividend');
    });
    const table = within(await panel.findByRole('table', { name: 'Ledger entries' }));
    await vi.waitFor(() => {
      expect(table.getAllByRole('row')).toHaveLength(2);
    });
    expect(table.getByText('Final dividend INFY 21')).toBeInTheDocument();
    const totals = within(panel.getByRole('region', { name: 'Totals by class' }));
    expect(totals.getByText('+₹20,000.00')).toBeInTheDocument();
  });

  it('asks for the period that was chosen', async () => {
    const requests = serveLedger();
    const panel = await openPanel();
    await panel.findByRole('table', { name: 'Ledger entries' });

    await userEvent.type(panel.getByLabelText('From'), '2026-10-07');
    await userEvent.type(panel.getByLabelText('To'), '2026-10-08');

    await vi.waitFor(() => {
      expect(requests.at(-1)?.get('from')).toBe('2026-10-07');
      expect(requests.at(-1)?.get('to')).toBe('2026-10-08');
    });
    const table = within(await panel.findByRole('table', { name: 'Ledger entries' }));
    await vi.waitFor(() => {
      expect(table.getAllByRole('row')).toHaveLength(3);
    });
  });

  it('refuses a period that ends before it starts, without asking the API', async () => {
    const requests = serveLedger();
    const panel = await openPanel();
    await panel.findByRole('table', { name: 'Ledger entries' });
    const before = requests.length;

    await userEvent.type(panel.getByLabelText('From'), '2026-10-08');
    await userEvent.type(panel.getByLabelText('To'), '2026-10-01');

    expect(await panel.findByText('The start date is after the end date.')).toBeInTheDocument();
    expect(panel.queryByRole('table', { name: 'Ledger entries' })).not.toBeInTheDocument();
    // The "To" date was typed after "From", so no request carries both dates.
    expect(requests.slice(before).some((q) => q.get('from') === '2026-10-08' && q.get('to') === '2026-10-01')).toBe(
      false,
    );
  });

  it('says there are no entries yet, with the totals at zero', async () => {
    serveLedger([]);
    const panel = await openPanel();

    expect(await panel.findByText(/No ledger entries yet\./)).toBeInTheDocument();
    expect(panel.queryByRole('table', { name: 'Ledger entries' })).not.toBeInTheDocument();
    expect(panel.getAllByText('0 entries')).toHaveLength(5);
  });

  it('says that nothing matches when a class has no entries, and clears the filters', async () => {
    serveLedger(sampleLedger.filter((entry) => entry.class !== 'charges'));
    const panel = await openPanel();
    await panel.findByRole('table', { name: 'Ledger entries' });

    await userEvent.selectOptions(panel.getByLabelText('Class'), 'charges');

    expect(await panel.findByText('No ledger entries match this class and period.')).toBeInTheDocument();
    await userEvent.click(panel.getByRole('button', { name: 'Clear filters' }));
    expect(await panel.findByRole('table', { name: 'Ledger entries' })).toBeInTheDocument();
  });

  it('shows an error with Retry when the ledger cannot be loaded', async () => {
    let fail = true;
    server.use(
      http.get(ledgerPath, ({ request }) =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json(answerLedger(sampleLedger, new URL(request.url).searchParams)),
      ),
    );
    const panel = await openPanel();

    expect(await panel.findByText('The ledger could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(panel.getByRole('button', { name: 'Retry' }));
    expect(await panel.findByRole('table', { name: 'Ledger entries' })).toBeInTheDocument();
  });

  it('loads further pages with Show more', async () => {
    const many = Array.from({ length: threePages }, (_, i) =>
      buildLedgerEntry({
        id: ledgerIds(i + 10),
        voucherNo: `V${String(i)}`,
        date: `2026-09-${String(30 - Math.floor(i / 2)).padStart(2, '0')}`,
      }),
    );
    serveLedger(many);
    const panel = await openPanel();

    expect(await panel.findByText(`Showing ${String(smallPage)} of ${String(threePages)}`)).toBeInTheDocument();
    await userEvent.click(panel.getByRole('button', { name: 'Show more' }));

    expect(await panel.findByText(`Showing ${String(smallPage * 2)} of ${String(threePages)}`)).toBeInTheDocument();
  });

  it('keeps the keyboard on Show more while a page loads and moves it to the table after the last page', async () => {
    const many = Array.from({ length: smallPage * 2 }, (_, i) =>
      buildLedgerEntry({ id: ledgerIds(i + 10), voucherNo: `V${String(i)}`, date: '2026-09-30' }),
    );
    serveLedger(many);
    const panel = await openPanel();
    const more = await panel.findByRole('button', { name: 'Show more' });
    more.focus();

    await userEvent.keyboard('{Enter}');

    expect(await panel.findByText(`Showing ${String(smallPage * 2)} of ${String(smallPage * 2)}`)).toBeInTheDocument();
    expect(panel.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
    expect(panel.getByRole('region', { name: /Ledger entries/ })).toHaveFocus();
  });

  it('closes with its button', async () => {
    serveLedger();
    const panel = await openPanel();

    await userEvent.click(panel.getByRole('button', { name: 'Close cash and dividends' }));

    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Cash & dividends' })).not.toBeInTheDocument();
    });
  });

  it('moves the focus into the panel, closes with Escape and gives the focus back to its button', async () => {
    serveLedger();
    renderPortfolio();
    const opener = await screen.findByRole('button', { name: 'Cash & dividends' });
    await userEvent.click(opener);
    const dialog = await screen.findByRole('dialog', { name: 'Cash & dividends' });
    await vi.waitFor(() => {
      expect(dialog.contains(document.activeElement)).toBe(true);
    });

    await userEvent.keyboard('{Escape}');

    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Cash & dividends' })).not.toBeInTheDocument();
    });
    await vi.waitFor(() => {
      expect(opener).toHaveFocus();
    });
  });

  it('labels the date and class filters and the columns of the table', async () => {
    serveLedger();
    const panel = await openPanel();

    expect(panel.getByLabelText('From')).toHaveAttribute('type', 'date');
    expect(panel.getByLabelText('To')).toHaveAttribute('type', 'date');
    expect(panel.getByRole('combobox', { name: 'Class' })).toBeInTheDocument();
    for (const name of ['Date', 'Details', 'Class', 'Amount', 'Balance']) {
      expect(await panel.findByRole('columnheader', { name })).toBeInTheDocument();
    }
  });

  it('lets the owner choose the account when the page shows all of them', async () => {
    server.use(twoPortfolioAccounts());
    const asked: string[] = [];
    server.use(
      http.get(ledgerPath, ({ request, params }) => {
        asked.push(String(params.id));
        return HttpResponse.json(answerLedger(sampleLedger, new URL(request.url).searchParams));
      }),
    );
    const panel = await openPanel();

    expect(panel.getByText('Choose an account to see its cash movements.')).toBeInTheDocument();
    expect(asked).toEqual([]);
    await userEvent.selectOptions(panel.getByLabelText('Account'), ids.main);

    expect(await panel.findByRole('table', { name: 'Ledger entries' })).toBeInTheDocument();
    expect(asked[0]).toBe(ids.main);
  });
});
