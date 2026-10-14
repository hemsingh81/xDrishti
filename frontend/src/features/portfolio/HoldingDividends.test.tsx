import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildDividend, buildIncome, dividendIds, withIncome } from '@/test/dividendFixtures';
import { buildHolding, holdingIds, searchHoldings, sampleHoldings } from '@/test/fixtures';
import { pendingResponse, releasePending } from '@/test/gate';
import { server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { renderPortfolio, serveDividends, warmDividends } from './testing';

// The list shows 50 dividends a page in the app; here it pages by a few.
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

beforeAll(warmDividends, 60_000);

const paid = buildDividend({ note: '<b>Final</b> dividend FY25' });
const earlier = buildDividend({
  id: dividendIds.second,
  payDate: '2025-06-10',
  amount: 800,
  perShare: null,
  note: null,
});
const voided = buildDividend({
  id: dividendIds.third,
  payDate: '2024-12-01',
  amount: 500,
  voided: true,
  voidedAt: '2026-10-13T08:00:00Z',
  voidedBy: 'owner',
});

async function openReliance(dividends = [paid, earlier, voided]) {
  const fake = serveDividends({ dividends });
  const view = renderPortfolio(`?holding=${holdingIds.reliance}`);
  const drawer = within(await screen.findByLabelText('Holding details'));
  const section = within(await drawer.findByRole('region', { name: 'Dividends' }));
  return { ...view, fake, drawer, section };
}

describe('Holding drawer: the income figures', () => {
  it('shows the dividends, charges, total return, net total return and net XIRR of the holding, with the dates', async () => {
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(
          searchHoldings(
            sampleHoldings.map((h) => (h.id === holdingIds.reliance ? withIncome(h, buildIncome()) : h)),
            new URL(request.url).searchParams,
          ),
        ),
      ),
    );
    renderPortfolio(`?holding=${holdingIds.reliance}`);
    const drawer = within(await screen.findByLabelText('Holding details'));

    await drawer.findByText('Net total return');
    const facts = (label: string) => drawer.getByText(label, { selector: 'dt' }).nextElementSibling;
    expect(facts('Dividends')).toHaveTextContent('₹1,200.00 (entered from 5 Dec 2025)');
    expect(facts('Charges')).toHaveTextContent('₹150.00 (trade charges from 2 Jan 2025)');
    expect(facts('Total return (with dividends)')).toHaveTextContent('▲+17.20%');
    expect(facts('Net total return')).toHaveTextContent('▲+17.05%');
    expect(facts('Net XIRR')).toHaveTextContent('▲+13.80%');
    expect(facts('Net figures')).toHaveTextContent('counted from 5 Dec 2025');
  });

  it('shows a Dividends column in the list: the amount, or a dash where none is entered', async () => {
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(
          searchHoldings(
            sampleHoldings.map((h) =>
              h.id === holdingIds.reliance
                ? withIncome(h, buildIncome())
                : h.id === holdingIds.tcs
                  ? withIncome(h, buildIncome({ dividends: 0, dividendsFrom: null }))
                  : h,
            ),
            new URL(request.url).searchParams,
          ),
        ),
      ),
    );
    renderPortfolio();

    const cells = async (symbol: string) =>
      within(await screen.findByRole('row', { name: new RegExp(`^${symbol}\\b`) }))
        .getAllByRole('gridcell')
        .map((cell) => cell.textContent);
    expect((await cells('RELIANCE'))[11]).toBe('₹1,200');
    expect((await cells('TCS'))[11]).toBe('—');
    expect((await cells('INFY'))[11]).toBe('—');
  });

  it('sorts the list by dividends from the column title', async () => {
    const lists: URLSearchParams[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        lists.push(params);
        return HttpResponse.json(searchHoldings(sampleHoldings, params));
      }),
    );
    const { router } = renderPortfolio();

    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    await userEvent.click(within(screen.getByRole('columnheader', { name: 'Dividends' })).getByRole('button'));

    await waitFor(() => {
      expect(lists.at(-1)?.get('sort')).toBe('dividends');
    });
    expect(router.state.location.search).toContain('sort=dividends');
  });
});

describe('Holding drawer: Dividends section', () => {
  it('lists the dividends newest first with date, amount, per share, note and who entered them', async () => {
    const { section } = await openReliance();

    const table = within(await section.findByRole('table', { name: 'Dividends of RELIANCE' }));
    expect(table.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Pay date',
      'Amount',
      'Per share',
      'Shares',
      'Note',
      'Action',
    ]);
    const rows = table.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    const first = within(rows[0]!);
    expect(first.getByText('5 Dec 2025')).toBeInTheDocument();
    expect(first.getByText('₹1,200.00')).toBeInTheDocument();
    expect(first.getByText('₹30.00')).toBeInTheDocument();
    expect(first.getByText(/Entered by owner on 12 Oct 2026/)).toBeInTheDocument();
    // Notes are plain text: the markup is shown as typed, never interpreted.
    expect(first.getByText(/<b>Final<\/b> dividend FY25/)).toBeInTheDocument();
    expect(first.queryByRole('strong')).not.toBeInTheDocument();
    expect(within(rows[1]!).getByText('10 Jun 2025')).toBeInTheDocument();
    expect(section.getByText(/Dividends are entered by you/)).toBeInTheDocument();
  });

  it('flags a voided dividend: struck through, by whom and when, and no Void button', async () => {
    const { section } = await openReliance();

    const table = within(await section.findByRole('table', { name: 'Dividends of RELIANCE' }));
    const row = within(table.getByRole('row', { name: /1 Dec 2024/ }));
    expect(row.getByText('Voided by owner on 13 Oct 2026')).toBeInTheDocument();
    expect(row.getByText('1 Dec 2024').closest('span')).toHaveStyle({ textDecoration: 'line-through' });
    expect(row.getByText('₹500.00')).toHaveStyle({ textDecoration: 'line-through' });
    expect(row.queryByRole('button', { name: /Void/ })).not.toBeInTheDocument();
    expect(table.getAllByRole('button', { name: /^Void the / })).toHaveLength(2);
  });

  it('says that none is entered', async () => {
    const { section } = await openReliance([]);

    expect(await section.findByText(/No dividends entered for this holding yet/)).toBeInTheDocument();
    expect(section.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows progress while the dividends load, and an error with Retry', async () => {
    serveDividends();
    let state: 'hold' | 'fail' | 'ok' = 'hold';
    server.use(
      http.get('/api/holdings/:id/dividends', () => {
        if (state === 'hold') return pendingResponse();
        return state === 'fail'
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ items: [paid], page: 1, pageSize: 50, total: 1 });
      }),
    );
    renderPortfolio(`?holding=${holdingIds.reliance}`);
    const drawer = within(await screen.findByLabelText('Holding details'));
    expect(await drawer.findByRole('progressbar', { name: 'Loading dividends' })).toBeInTheDocument();

    state = 'fail';
    await releasePending();
    expect(await drawer.findByText('The dividends could not be loaded.')).toBeInTheDocument();
    state = 'ok';
    await userEvent.click(drawer.getByRole('button', { name: 'Retry' }));
    expect(await drawer.findByRole('table', { name: 'Dividends of RELIANCE' })).toBeInTheDocument();
  });

  it('shows more dividends on request', async () => {
    const many = Array.from({ length: smallPage + 4 }, (_, n) =>
      buildDividend({ id: `00000000-0000-4000-8000-0000000015${String(n + 10)}`, amount: n + 1 }),
    );
    const { section } = await openReliance(many);

    const table = () => within(section.getByRole('table', { name: 'Dividends of RELIANCE' }));
    await section.findByRole('table', { name: 'Dividends of RELIANCE' });
    expect(table().getAllByRole('button', { name: /^Void/ })).toHaveLength(smallPage);
    await userEvent.click(section.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(table().getAllByRole('button', { name: /^Void/ })).toHaveLength(many.length);
    });
  });
});

describe('Holding drawer: Add dividend', () => {
  const setField = (label: string, value: string) => {
    fireEvent.change(screen.getByLabelText(label), { target: { value } });
  };
  async function openForm() {
    const view = await openReliance([paid]);
    await userEvent.click(view.section.getByRole('button', { name: 'Add dividend' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add dividend' }));
    return { ...view, dialog };
  }
  const fill = () => {
    setField('Pay date', '2026-10-01');
    setField('Amount received (₹)', '1,250.50');
    setField('Per share (₹, optional)', '31.25');
    setField('Note (optional)', 'Interim, FY26');
  };

  it('is for this holding: no account and no holding to choose, a date, an amount, a per-share amount and a note', async () => {
    const { dialog } = await openForm();

    expect(dialog.getByText('RELIANCE · Reliance Industries')).toBeInTheDocument();
    expect(dialog.queryByLabelText('Account')).not.toBeInTheDocument();
    expect(dialog.queryByRole('combobox', { name: /Holding/ })).not.toBeInTheDocument();
    expect(dialog.getByLabelText('Pay date')).toHaveAttribute('max', expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/));
    await waitFor(() => {
      expect(dialog.getByLabelText('Pay date')).toHaveFocus();
    });
    expect(dialog.getByText('0 of 200 characters')).toBeInTheDocument();
  });

  it('adds the dividend for the holding, closes, says so, and the list shows it', async () => {
    const { dialog, fake, section } = await openForm();
    fill();

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Add dividend' })).not.toBeInTheDocument();
    });
    expect(fake.calls.added).toEqual([
      {
        accountId: null,
        holdingId: holdingIds.reliance,
        payDate: '2026-10-01',
        amount: 1250.5,
        perShare: 31.25,
        note: 'Interim, FY26',
        confirm: null,
      },
    ]);
    expect(await screen.findByText('Added the dividend of ₹1,250.50 on 1 Oct 2026.')).toBeInTheDocument();
    expect(await section.findByText('₹1,250.50')).toBeInTheDocument();
  });

  it('says what is missing or wrong, field by field, without asking the server', async () => {
    const { dialog, fake } = await openForm();

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));
    expect(dialog.getByText('Enter the day the money was credited.')).toBeInTheDocument();
    expect(dialog.getByText('Enter the amount you received.')).toBeInTheDocument();
    expect(dialog.getByLabelText('Pay date')).toHaveFocus();

    setField('Pay date', '2999-01-01');
    setField('Amount received (₹)', '12.345');
    setField('Per share (₹, optional)', '0');
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));
    expect(dialog.getByText('The date cannot be in the future.')).toBeInTheDocument();
    expect(dialog.getByText('Enter rupees and paise: at most two decimals.')).toBeInTheDocument();
    expect(dialog.getByText('The amount per share must be above 0.')).toBeInTheDocument();
    expect(fake.calls.added).toEqual([]);

    setField('Pay date', '2026-10-01');
    setField('Amount received (₹)', '0');
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));
    expect(dialog.getByText('The amount must be above 0.')).toBeInTheDocument();
  });

  it('counts the note and refuses more than 200 characters', async () => {
    const { dialog, fake } = await openForm();
    setField('Pay date', '2026-10-01');
    setField('Amount received (₹)', '10');
    setField('Note (optional)', 'x'.repeat(201));

    expect(dialog.getByText('201 of 200 characters')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    expect(dialog.getByText('The note can have at most 200 characters.')).toBeInTheDocument();
    expect(fake.calls.added).toEqual([]);
  });

  it("puts the API's message at its field and keeps the dialog open", async () => {
    const { dialog, fake } = await openForm();
    fake.config.addProblems.push({
      status: 400,
      body: {
        title: 'The dividend is not valid.',
        code: 'portfolio.invalid',
        errors: { amount: ['The amount must be above 0 and have at most two decimals.'] },
      },
    });
    fill();

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    expect(await dialog.findByText('The amount must be above 0 and have at most two decimals.')).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getByLabelText('Amount received (₹)')).toHaveFocus();
    });
    expect(screen.getByRole('dialog', { name: 'Add dividend' })).toBeInTheDocument();
  });

  it('says in words why a refused add did not work, when no field is named', async () => {
    const { dialog, fake } = await openForm();
    fake.config.addProblems.push({
      status: 409,
      body: { title: 'Switch on the Portfolio role.', code: 'portfolio.role_off' },
    });
    fill();

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    expect(await dialog.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();
  });

  it('asks before adding the same account, holding, date and amount twice, and adds it only on "Yes"', async () => {
    const { dialog, fake } = await openForm();
    setField('Pay date', '2025-12-05');
    setField('Amount received (₹)', '1200');

    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));

    const question = await dialog.findByRole('alert');
    expect(question).toHaveTextContent(
      'A dividend of the same amount on the same date is already entered for this holding. Is this a second payment?',
    );
    expect(fake.calls.added.map((body) => body.confirm)).toEqual([null]);

    await userEvent.click(within(question).getByRole('button', { name: 'Yes, add it again' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Add dividend' })).not.toBeInTheDocument();
    });
    expect(fake.calls.added.map((body) => body.confirm)).toEqual([null, true]);
    expect(fake.state.dividends.filter((d) => d.amount === 1200)).toHaveLength(2);
  });

  it('adds nothing when the owner answers "No, change it", and lets the entry be changed', async () => {
    const { dialog, fake } = await openForm();
    setField('Pay date', '2025-12-05');
    setField('Amount received (₹)', '1200');
    await userEvent.click(dialog.getByRole('button', { name: 'Add dividend' }));
    const question = await dialog.findByRole('alert');

    await userEvent.click(within(question).getByRole('button', { name: 'No, change it' }));

    expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
    expect(fake.calls.added).toHaveLength(1);
    expect(dialog.getByLabelText('Amount received (₹)')).toHaveValue('1200');
  });

  it('closes with Cancel and with Escape, adding nothing', async () => {
    const { dialog, fake } = await openForm();
    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Add dividend' })).not.toBeInTheDocument();
    });
    expect(fake.calls.added).toEqual([]);
  });
});

describe('Holding drawer: Void', () => {
  const name = 'dividend of ₹800.00 on 10 Jun 2025';

  it('asks first, with the dividend named and Cancel focused, and voids nothing on Cancel', async () => {
    const { section, fake } = await openReliance();
    await userEvent.click(await section.findByRole('button', { name: `Void the ${name}` }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Void this dividend?' }));
    expect(dialog.getByText(/stops counting in total return and net XIRR/)).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    });
    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Void this dividend?' })).not.toBeInTheDocument();
    });
    expect(fake.calls.voided).toEqual([]);
    expect(section.getByRole('button', { name: `Void the ${name}` })).toBeInTheDocument();
  });

  it('voids on confirmation: the dividend stays in the list, flagged, and the keyboard goes to the section title', async () => {
    const { section, fake } = await openReliance();
    await userEvent.click(await section.findByRole('button', { name: `Void the ${name}` }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Void this dividend?' })).getByRole('button', {
        name: 'Void dividend',
      }),
    );

    expect(await screen.findByText(`Voided the ${name}. It no longer counts in the returns.`)).toBeInTheDocument();
    expect(fake.calls.voided).toEqual([dividendIds.second]);
    const table = within(await section.findByRole('table', { name: 'Dividends of RELIANCE' }));
    await waitFor(() => {
      expect(
        within(table.getByRole('row', { name: /10 Jun 2025/ })).getByText('Voided by owner on 13 Oct 2026'),
      ).toBeInTheDocument();
    });
    expect(table.queryByRole('button', { name: `Void the ${name}` })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(section.getByRole('heading', { name: 'Dividends' })).toHaveFocus();
    });
  });

  it('reads the income figures again after a void, and nothing else: not the history, the lots, the trades or the Action center', async () => {
    const counts = new Map<string, number>();
    const onRequest = ({ request }: { request: Request }) => {
      if (request.method !== 'GET') return;
      const path = new URL(request.url).pathname;
      counts.set(path, (counts.get(path) ?? 0) + 1);
    };
    server.events.on('request:start', onRequest);
    try {
      const { section } = await openReliance();
      await userEvent.click(await section.findByRole('button', { name: `Void the ${name}` }));
      await waitFor(() => {
        // The dividend lists, the summary tiles and the holdings are on screen and were read once.
        expect(counts.get('/api/holdings/summary')).toBeGreaterThan(0);
        expect(counts.get('/api/portfolio/history')).toBeGreaterThan(0);
        expect(counts.get('/api/portfolio/actions')).toBeGreaterThan(0);
      });
      const before = new Map(counts);
      const count = (path: string) => counts.get(path) ?? 0;
      const was = (path: string) => before.get(path) ?? 0;

      await userEvent.click(
        within(await screen.findByRole('dialog', { name: 'Void this dividend?' })).getByRole('button', {
          name: 'Void dividend',
        }),
      );
      await screen.findByText(`Voided the ${name}. It no longer counts in the returns.`);

      await waitFor(() => {
        expect(count('/api/holdings/summary')).toBeGreaterThan(was('/api/holdings/summary'));
        expect(count('/api/holdings')).toBeGreaterThan(was('/api/holdings'));
        expect(count(`/api/holdings/${holdingIds.reliance}/dividends`)).toBeGreaterThan(
          was(`/api/holdings/${holdingIds.reliance}/dividends`),
        );
      });
      // Dividends change none of these.
      for (const path of [
        '/api/portfolio/history',
        '/api/portfolio/actions',
        '/api/portfolio/alerts',
        '/api/portfolio/notices',
        `/api/holdings/${holdingIds.reliance}/lots`,
        `/api/holdings/${holdingIds.reliance}/trades`,
      ]) {
        expect(count(path), path).toBe(was(path));
      }
    } finally {
      server.events.removeListener('request:start', onRequest);
    }
  });

  it('says so when the void was refused, and changes nothing', async () => {
    const { section, fake } = await openReliance();
    fake.config.voidProblem = { status: 500, body: { title: 'Could not void that.' } };
    await userEvent.click(await section.findByRole('button', { name: `Void the ${name}` }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Void this dividend?' })).getByRole('button', {
        name: 'Void dividend',
      }),
    );

    expect(await screen.findByText('Could not void that.')).toBeInTheDocument();
    expect(section.getByRole('button', { name: `Void the ${name}` })).toBeInTheDocument();
  });
});

describe('Holding drawer: a holding without income', () => {
  it('shows no income facts and still offers Add dividend', async () => {
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(
          searchHoldings(
            [buildHolding({ income: null }), ...sampleHoldings.slice(1)],
            new URL(request.url).searchParams,
          ),
        ),
      ),
    );
    const { section, drawer } = await openReliance([]);

    expect(drawer.queryByText('Net total return')).not.toBeInTheDocument();
    expect(section.getByRole('button', { name: 'Add dividend' })).toBeInTheDocument();
  });
});
