import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { holdingIds, ids, sampleHoldings, searchHoldings } from '@/test/fixtures';
import { releaseStep, holdStep } from '@/test/gate';
import { server } from '@/test/server';
import { buildRebuild } from '@/test/tradeFixtures';
import { pinClock } from '@/test/pinClock';
import { renderPortfolio, serveTrades, tradesPanel, twoPortfolioAccounts, warmTrades } from './testing';

beforeAll(warmTrades, 60_000);

async function openDialog() {
  const fake = serveTrades();
  const view = renderPortfolio('?trades=import');
  await userEvent.click(within(await tradesPanel()).getByRole('button', { name: 'Add trade' }));
  const dialog = within(await screen.findByRole('dialog', { name: 'Add trade' }));
  return { ...view, fake, dialog };
}

type Dialog = Awaited<ReturnType<typeof openDialog>>['dialog'];

const setDate = (dialog: Dialog, value: string) => {
  fireEvent.change(dialog.getByLabelText('Trade date'), { target: { value } });
};

/** A value put into a field in one step (typing key by key through the searchable field is what makes these tests slow). */
const enter = (dialog: Dialog, label: string, value: string) => {
  fireEvent.change(dialog.getByLabelText(label), { target: { value } });
};

async function searchSecurity(dialog: Dialog, text: string) {
  await userEvent.click(dialog.getByRole('combobox', { name: 'Security' }));
  await userEvent.paste(text);
}

async function pickInfy(dialog: Dialog) {
  await searchSecurity(dialog, 'INF');
  await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
}

async function fillValid(dialog: Dialog) {
  await pickInfy(dialog);
  await userEvent.click(dialog.getByRole('button', { name: 'Buy' }));
  setDate(dialog, '2026-10-05');
  fireEvent.change(dialog.getByLabelText('Time (optional, IST)'), { target: { value: '09:15' } });
  enter(dialog, 'Quantity', '10');
  enter(dialog, 'Price per share (₹)', '1,600.50');
  enter(dialog, 'Charges (₹, optional)', '12');
}

const submit = (dialog: Dialog) => userEvent.click(dialog.getByRole('button', { name: 'Add trade' }));

// The dates typed here are past days of October 2026 and a form refuses a future date, so the clock is pinned.
pinClock('2026-10-14T06:00:00Z');

describe('Add trade: the form', () => {
  it('offers the account, the security, the side, the date, the time, the quantity, the price and the charges', async () => {
    const { dialog } = await openDialog();

    expect(dialog.getByLabelText('Account')).toHaveValue(ids.main);
    expect(dialog.getByRole('combobox', { name: 'Security' })).toBeInTheDocument();
    const side = within(dialog.getByRole('group', { name: 'Side' }));
    expect(side.getByRole('button', { name: 'Buy', pressed: false })).toBeInTheDocument();
    expect(side.getByRole('button', { name: 'Sell', pressed: false })).toBeInTheDocument();
    expect(dialog.getByLabelText('Trade date')).toHaveAttribute('min', '1990-01-02');
    expect(dialog.getByLabelText('Trade date')).toHaveAttribute('max', expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/));
    expect(dialog.getByLabelText('Time (optional, IST)')).toHaveAttribute('type', 'time');
    expect(dialog.getByText('Whole shares')).toBeInTheDocument();
    expect(dialog.getByText(/Adding a trade changes no lot/)).toBeInTheDocument();
  });

  it('says what is missing, field by field, and puts the keyboard on the first one', async () => {
    const { dialog, fake } = await openDialog();

    await submit(dialog);

    expect(dialog.getByText('Choose the security.')).toBeInTheDocument();
    expect(dialog.getByText('Choose buy or sell.')).toBeInTheDocument();
    expect(dialog.getByText('Enter the trade date.')).toBeInTheDocument();
    expect(dialog.getByText('Enter the quantity.')).toBeInTheDocument();
    expect(dialog.getByText('Enter the price.')).toBeInTheDocument();
    expect(dialog.getByRole('combobox', { name: 'Security' })).toHaveFocus();
    expect(fake.calls.added).toEqual([]);
  });

  it.each([
    ['a date in the future', '2999-01-01', '1', '10', 'The date cannot be in the future.', 'Trade date'],
    ['a date before 1990', '1989-12-31', '1', '10', 'The date cannot be before 2 Jan 1990.', 'Trade date'],
    ['a fractional quantity', '2026-10-05', '2.5', '10', 'The quantity must be a whole number of shares.', 'Quantity'],
    ['a quantity of zero', '2026-10-05', '0', '10', 'The quantity must be above 0.', 'Quantity'],
    ['a price of zero', '2026-10-05', '1', '0', 'The price must be above 0.', 'Price per share (₹)'],
  ])('refuses %s without asking the server', async (_name, date, quantity, price, message, field) => {
    const { dialog, fake } = await openDialog();
    await pickInfy(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Sell' }));
    setDate(dialog, date);
    enter(dialog, 'Quantity', quantity);
    enter(dialog, 'Price per share (₹)', price);

    await submit(dialog);

    expect(dialog.getByText(message)).toBeInTheDocument();
    expect(dialog.getByLabelText(field)).toHaveFocus();
    expect(fake.calls.added).toEqual([]);
  });

  it('refuses negative charges', async () => {
    const { dialog } = await openDialog();
    await fillValid(dialog);
    await userEvent.clear(dialog.getByLabelText('Charges (₹, optional)'));
    enter(dialog, 'Charges (₹, optional)', '-3');

    await submit(dialog);

    expect(dialog.getByText('The charges cannot be negative.')).toBeInTheDocument();
  });

  it('searches the holdings of the account and lists them by symbol and name', async () => {
    const { dialog } = await openDialog();

    await searchSecurity(dialog, 'TCS');

    const option = await screen.findByRole('option', { name: /TCS/ });
    expect(option).toHaveTextContent('Tata Consultancy Services');
  });
});

describe('Add trade: adding', () => {
  it('sends the holding, the side, the date, the time, the quantity, the price and the charges', async () => {
    const { dialog, fake } = await openDialog();
    await fillValid(dialog);

    await submit(dialog);

    await dialog.findByRole('heading', { name: 'Trade added' });
    expect(fake.calls.added).toEqual([
      {
        accountId: ids.main,
        holdingId: holdingIds.infy,
        symbol: null,
        isin: null,
        side: 'buy',
        tradeDate: '2026-10-05',
        tradeTime: '09:15:00',
        quantity: 10,
        price: 1_600.5,
        charges: 12,
      },
    ]);
    expect(dialog.getByRole('heading', { name: 'Trade added' })).toHaveFocus();
    expect(dialog.getByText('Buy 10 shares of INFY at ₹1,600.50 on 5 Oct 2026.')).toBeInTheDocument();
    expect(dialog.getByText(/No lot has changed\. Rebuild the lots of this holding/)).toBeInTheDocument();
    expect(await screen.findByText('Added the buy of 10 shares of INFY on 5 Oct 2026.')).toBeInTheDocument();
  });

  it('sends a security that is typed in by symbol and ISIN, without a holding', async () => {
    const { dialog, fake } = await openDialog();
    await userEvent.click(dialog.getByRole('button', { name: 'Not in the list? Enter a symbol or ISIN' }));
    enter(dialog, 'Symbol', 'wipro');
    enter(dialog, 'ISIN (optional)', 'INE075A01022');
    await userEvent.click(dialog.getByRole('button', { name: 'Sell' }));
    setDate(dialog, '2026-10-05');
    enter(dialog, 'Quantity', '3');
    enter(dialog, 'Price per share (₹)', '250');

    await submit(dialog);

    await dialog.findByRole('heading', { name: 'Trade added' });
    expect(fake.calls.added).toEqual([
      {
        accountId: ids.main,
        holdingId: null,
        symbol: 'wipro',
        isin: 'INE075A01022',
        side: 'sell',
        tradeDate: '2026-10-05',
        tradeTime: null,
        quantity: 3,
        price: 250,
        charges: null,
      },
    ]);
    // WIPRO is not held, so there is no holding to rebuild.
    expect(dialog.queryByRole('button', { name: 'Rebuild lots' })).not.toBeInTheDocument();
    expect(dialog.getByText(/Open the holding and use Rebuild lots in its Trades section/)).toBeInTheDocument();
  });

  it('needs a symbol or an ISIN when the security is typed in', async () => {
    const { dialog } = await openDialog();
    await userEvent.click(dialog.getByRole('button', { name: 'Not in the list? Enter a symbol or ISIN' }));

    await submit(dialog);

    expect(dialog.getByText('Enter the symbol or the ISIN of an NSE share or ETF.')).toBeInTheDocument();
    expect(dialog.getByLabelText('Symbol')).toHaveFocus();
  });

  it('shows the API’s messages at the fields they name and focuses the first', async () => {
    const { dialog, fake } = await openDialog();
    await fillValid(dialog);
    fake.config.addProblem = {
      status: 400,
      body: {
        title: 'One or more values are not valid.',
        code: 'portfolio.invalid',
        errors: {
          quantity: ['At most 100,000,000.'],
          tradeDate: ['The date cannot be in the future.'],
          somethingElse: ['A message the form has no field for.'],
        },
      },
    };

    await submit(dialog);

    expect(await dialog.findByText('At most 100,000,000.')).toBeInTheDocument();
    expect(dialog.getByText('The date cannot be in the future.')).toBeInTheDocument();
    expect(dialog.getByText('A message the form has no field for.')).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getByLabelText('Trade date')).toHaveFocus();
    });
    // Typing again takes the messages away.
    enter(dialog, 'Quantity', '0');
    expect(dialog.queryByText('At most 100,000,000.')).not.toBeInTheDocument();
  });

  it('shows a refusal that is not about a field, and a trade can be added on the next try', async () => {
    const { dialog, fake } = await openDialog();
    await fillValid(dialog);
    fake.config.addProblem = { status: 409, body: { title: 'Role off', code: 'portfolio.role_off' } };

    await submit(dialog);

    expect(await dialog.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();
    fake.config.addProblem = null;
    await submit(dialog);
    await dialog.findByRole('heading', { name: 'Trade added' });
  });

  it('adds another trade from the confirmation with an empty form', async () => {
    const { dialog } = await openDialog();
    await fillValid(dialog);
    await submit(dialog);
    await dialog.findByRole('heading', { name: 'Trade added' });

    await userEvent.click(dialog.getByRole('button', { name: 'Add another' }));

    expect(dialog.getByLabelText('Quantity')).toHaveValue('');
    expect(dialog.getByRole('combobox', { name: 'Security' })).toHaveValue('');
  });

  it('closes with Cancel, Escape and Done', async () => {
    const { dialog } = await openDialog();

    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Add trade' })).not.toBeInTheDocument();
    });
    // The Trades panel is still there.
    expect(screen.getByRole('dialog', { name: 'Trades' })).toBeInTheDocument();
  });
});

describe('Add trade: refusals and races', () => {
  it('says something when the API refuses with no field the form has (or none at all)', async () => {
    const { dialog, fake } = await openDialog();
    await fillValid(dialog);
    fake.config.addProblem = {
      status: 400,
      body: { title: 'One or more values are not valid.', code: 'portfolio.invalid' },
    };

    await submit(dialog);

    expect(await dialog.findByText('One or more values are not valid.')).toBeInTheDocument();
  });

  it('does not take numbers like Infinity, 1e3 or 0x10 for a quantity or a price', async () => {
    const { dialog, fake } = await openDialog();
    await pickInfy(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Buy' }));
    setDate(dialog, '2026-10-05');
    enter(dialog, 'Quantity', '1e3');
    enter(dialog, 'Price per share (₹)', 'Infinity');

    await submit(dialog);

    expect(dialog.getByText('Enter the quantity as a plain number, for example 10.')).toBeInTheDocument();
    expect(dialog.getByText('Enter the price as a plain number, for example 1250.50.')).toBeInTheDocument();
    expect(fake.calls.added).toEqual([]);
  });

  it('forgets the holding that was picked when another account is chosen', async () => {
    server.use(twoPortfolioAccounts());
    const { dialog } = await openDialog();
    await userEvent.selectOptions(await dialog.findByLabelText('Account'), ids.main);
    await pickInfy(dialog);
    expect(dialog.getByRole('combobox', { name: 'Security' })).toHaveValue('INFY');

    await userEvent.selectOptions(dialog.getByLabelText('Account'), ids.swing);

    expect(dialog.getByRole('combobox', { name: 'Security' })).toHaveValue('');
    await userEvent.click(dialog.getByRole('combobox', { name: 'Security' }));
    expect(await screen.findByText('No holding of this account matches')).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /INFY/ })).not.toBeInTheDocument();
    await submit(dialog);
    expect(dialog.getByText('Choose the security.')).toBeInTheDocument();
  });

  it('does not offer Rebuild lots for the first trade’s holding when the owner already added another', async () => {
    const { dialog } = await openDialog();
    // The look-up of the holding of a typed security answers when the test says so, in the order it asked.
    server.use(
      http.get('/api/holdings', async ({ request }) => {
        const params = new URL(request.url).searchParams;
        await holdStep();
        return HttpResponse.json(searchHoldings(sampleHoldings, params));
      }),
    );
    const addTyped = async (symbol: string) => {
      await userEvent.click(dialog.getByRole('button', { name: 'Not in the list? Enter a symbol or ISIN' }));
      enter(dialog, 'Symbol', symbol);
      await userEvent.click(dialog.getByRole('button', { name: 'Buy' }));
      setDate(dialog, '2026-10-05');
      enter(dialog, 'Quantity', '3');
      enter(dialog, 'Price per share (₹)', '100');
      await submit(dialog);
      await dialog.findByRole('heading', { name: 'Trade added' });
    };
    await addTyped('infy');
    await userEvent.click(dialog.getByRole('button', { name: 'Add another' }));
    await addTyped('wipro');

    await releaseStep(); // the answer for INFY (the first trade) arrives now

    expect(dialog.getByRole('heading', { name: 'Trade added' })).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Rebuild lots' })).not.toBeInTheDocument();
    await releaseStep(); // WIPRO is not held
    expect(dialog.queryByRole('button', { name: 'Rebuild lots' })).not.toBeInTheDocument();
  });
});

describe('Add trade: Rebuild lots afterwards', () => {
  it('offers Rebuild lots for the holding, shows its dry run first and applies it on confirm', async () => {
    const { dialog, fake } = await openDialog();
    fake.config.rebuild = buildRebuild({
      lotsBefore: 1,
      lotsAfter: 3,
      openingQty: { before: 25, after: 10 },
      realisedPnl: 1_250,
    });
    await fillValid(dialog);
    await submit(dialog);
    await dialog.findByRole('heading', { name: 'Trade added' });

    await userEvent.click(dialog.getByRole('button', { name: 'Rebuild lots' }));

    const rebuild = within(await screen.findByRole('dialog', { name: 'Rebuild the lots of INFY' }));
    expect(await rebuild.findByText('1 → 3')).toBeInTheDocument();
    expect(rebuild.getByText('25 → 10')).toBeInTheDocument();
    expect(rebuild.getByText('+₹1,250')).toBeInTheDocument();
    expect(fake.calls.rebuilds).toEqual([{ holdingId: holdingIds.infy, dryRun: true }]);

    await userEvent.click(rebuild.getByRole('button', { name: 'Rebuild lots' }));

    expect(await screen.findByText('Rebuilt the lots of INFY: 1 lot replaced by 3 lots.')).toBeInTheDocument();
    expect(fake.calls.rebuilds.at(-1)).toEqual({ holdingId: holdingIds.infy, dryRun: false });
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Rebuild the lots of INFY' })).not.toBeInTheDocument();
    });
  });

  it('finds the holding of a typed security that the account holds, so Rebuild lots is offered', async () => {
    const { dialog } = await openDialog();
    await userEvent.click(dialog.getByRole('button', { name: 'Not in the list? Enter a symbol or ISIN' }));
    enter(dialog, 'Symbol', 'infy');
    await userEvent.click(dialog.getByRole('button', { name: 'Buy' }));
    setDate(dialog, '2026-10-05');
    enter(dialog, 'Quantity', '3');
    enter(dialog, 'Price per share (₹)', '1700');

    await submit(dialog);

    expect(await dialog.findByRole('button', { name: 'Rebuild lots' })).toBeInTheDocument();
  });
});
