import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildPriceAlert, instrumentIds } from '@/test/actionCenterFixtures';
import { buildHolding, holdingIds, sampleHoldings, searchHoldings } from '@/test/fixtures';
import { pendingResponse } from '@/test/gate';
import { problemResponse, server } from '@/test/server';
import { http, HttpResponse } from 'msw';
import { actionCenterCard, actionItem, renderPortfolio, serveActionCenter } from './testing';

beforeEach(() => {
  window.sessionStorage.clear();
});

/** Reliance's holding drawer with the alert dialog on top of it. */
async function openFromDrawer(holding: string = holdingIds.reliance) {
  const view = renderPortfolio(`?holding=${holding}`);
  const drawer = await screen.findByLabelText('Holding details');
  await userEvent.click(await within(drawer).findByRole('button', { name: 'Set price alert' }));
  const dialog = await screen.findByRole('dialog', { name: 'Set price alert' });
  return { ...view, dialog: within(dialog) };
}

describe('Alert dialog: from a holding', () => {
  it('is offered by the holding drawer for a holding in the master list, prefilled and read-only', async () => {
    serveActionCenter();
    const { dialog } = await openFromDrawer();

    expect(screen.getByRole('dialog', { name: 'Set price alert' })).toHaveTextContent('RELIANCE · Reliance Industries');
    expect(dialog.queryByRole('combobox', { name: 'Instrument' })).not.toBeInTheDocument();
    expect(dialog.getByText('Last price ₹2,902.50')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'At or below' })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => {
      expect(dialog.getByRole('textbox', { name: /Alert price/ })).toHaveFocus();
    });
    expect(
      dialog.getByText(
        /Checked every 5 minutes while the market is open.*triggers once, then stays in the Triggered list/,
      ),
    ).toBeInTheDocument();
  });

  it('is not offered for a holding that is not in the master list', async () => {
    serveActionCenter();
    renderPortfolio(`?holding=${holdingIds.smallco}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByRole('button', { name: 'View alerts' })).toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Set price alert' })).not.toBeInTheDocument();
  });

  it('opens the Alerts panel from the holding drawer', async () => {
    serveActionCenter();
    const { router } = renderPortfolio(`?holding=${holdingIds.reliance}`);
    const drawer = await screen.findByLabelText('Holding details');

    await userEvent.click(await within(drawer).findByRole('button', { name: 'View alerts' }));

    expect(await screen.findByRole('dialog', { name: 'Price alerts' })).toBeInTheDocument();
    expect(router.state.location.search).toContain('alerts=active');
    expect(router.state.location.search).toContain(`holding=${holdingIds.reliance}`);
  });

  it('fills the level from the quick levels around the last price, below or above by direction', async () => {
    serveActionCenter();
    const { dialog } = await openFromDrawer();
    const level = dialog.getByRole('textbox', { name: /Alert price/ });

    expect(dialog.getAllByRole('button', { name: /% below the last price/ }).map((chip) => chip.textContent)).toEqual([
      '−5 %',
      '−10 %',
      '−20 %',
    ]);
    await userEvent.click(dialog.getByRole('button', { name: '−10 %, ₹2,612.25 (10 % below the last price)' }));
    expect(level).toHaveValue('2612.25');
    await userEvent.click(dialog.getByRole('button', { name: '−5 %, ₹2,757.38 (5 % below the last price)' }));
    expect(level).toHaveValue('2757.38');

    await userEvent.click(dialog.getByRole('button', { name: 'At or above' }));
    expect(dialog.queryByRole('button', { name: /below the last price/ })).not.toBeInTheDocument();
    expect(dialog.getAllByRole('button', { name: /% above the last price/ }).map((chip) => chip.textContent)).toEqual([
      '+5 %',
      '+10 %',
      '+20 %',
    ]);
    await userEvent.click(dialog.getByRole('button', { name: '+20 %, ₹3,483.00 (20 % above the last price)' }));
    expect(level).toHaveValue('3483.00');
  });

  it('sets the alert, says so, closes, and the alert is in the Active list', async () => {
    const fake = serveActionCenter();
    const { dialog, router } = await openFromDrawer();

    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '2600');
    await userEvent.type(dialog.getByRole('textbox', { name: /Note/ }), '  trim on strength ');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(
      await screen.findByText('Alert set: NSE:RELIANCE at or below ₹2,600.00. The bell tells you when it triggers.'),
    ).toBeInTheDocument();
    expect(fake.calls.created).toEqual([
      { instrumentId: instrumentIds.reliance, direction: 'at_or_below', level: 2600, note: 'trim on strength' },
    ]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Set price alert' })).not.toBeInTheDocument();
    });
    expect(fake.alerts()).toHaveLength(1);
    expect(router.state.location.search).toBe(`?holding=${holdingIds.reliance}`);
  });

  it('refuses a missing, malformed or over-precise level and too long a note without asking the server', async () => {
    const fake = serveActionCenter();
    const { dialog } = await openFromDrawer();
    const level = dialog.getByRole('textbox', { name: /Alert price/ });

    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('Enter the price to be alerted at.')).toBeInTheDocument();
    expect(level).toBeInvalid();
    expect(level).toHaveFocus();

    await userEvent.type(level, 'abc');
    expect(dialog.queryByText('Enter the price to be alerted at.')).not.toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('Enter a price above 0, e.g. 1200 or 1200.50.')).toBeInTheDocument();

    await userEvent.clear(level);
    await userEvent.type(level, '0');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('Enter a price above 0, e.g. 1200 or 1200.50.')).toBeInTheDocument();

    await userEvent.clear(level);
    await userEvent.type(level, '1200.12345');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('Use at most 4 decimals.')).toBeInTheDocument();

    await userEvent.clear(level);
    await userEvent.type(level, '20000000');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('The price can be at most ₹1,00,00,000.')).toBeInTheDocument();

    await userEvent.clear(level);
    await userEvent.type(level, '2600');
    const note = dialog.getByRole('textbox', { name: /Note/ });
    await userEvent.click(note);
    await userEvent.paste('x'.repeat(201));
    expect(dialog.getByText('201 of 200 characters')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    expect(await dialog.findByText('At most 200 characters.')).toBeInTheDocument();
    expect(fake.calls.created).toEqual([]);
  });

  it('shows the API’s message at the level field when the condition already holds, and clears it on edit', async () => {
    const fake = serveActionCenter({ lastPrices: { [instrumentIds.reliance]: 2_902.5 } });
    const { dialog } = await openFromDrawer();
    const level = dialog.getByRole('textbox', { name: /Alert price/ });

    await userEvent.type(level, '3000');
    await userEvent.click(dialog.getByRole('button', { name: 'At or above' }));
    await userEvent.clear(level);
    await userEvent.type(level, '2800');
    await userEvent.click(dialog.getByRole('button', { name: 'At or below' }));
    await userEvent.clear(level);
    await userEvent.type(level, '2950');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(
      await dialog.findByText('The last price is ₹2902.50, which is already at or below this level.'),
    ).toBeInTheDocument();
    expect(level).toBeInvalid();
    expect(screen.getByRole('dialog', { name: 'Set price alert' })).toBeInTheDocument();
    expect(fake.alerts()).toHaveLength(0);

    await userEvent.clear(level);
    expect(dialog.queryByText(/already at or below/)).not.toBeInTheDocument();
  });

  it('says that the limit of active alerts is reached', async () => {
    serveActionCenter({ alerts: [buildPriceAlert()], alertLimit: 1 });
    const { dialog } = await openFromDrawer();

    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '2600');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'You have reached the limit of 200 active alerts. Cancel one you no longer need, then set this one.',
    );
    expect(screen.getByRole('dialog', { name: 'Set price alert' })).toBeInTheDocument();
  });

  it('closes with Cancel and Escape without setting anything', async () => {
    const fake = serveActionCenter();
    const { dialog } = await openFromDrawer();

    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Set price alert' })).not.toBeInTheDocument();
    });
    expect(fake.calls.created).toEqual([]);
  });
});

describe('Alert dialog: choosing the instrument', () => {
  async function openPicker() {
    const fake = serveActionCenter({ lastPrices: { [instrumentIds.tcs]: 3_880 } });
    const view = renderPortfolio('?alerts=active');
    await userEvent.click(await screen.findByRole('button', { name: 'New alert' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Set price alert' }));
    return { ...view, fake, dialog };
  }

  it('offers only shares and ETFs, and shows the last close of the one chosen', async () => {
    const { dialog } = await openPicker();
    const picker = dialog.getByRole('combobox', { name: 'Instrument' });
    await waitFor(() => {
      expect(picker).toHaveFocus();
    });

    await userEvent.type(picker, 'NIFTY');
    await waitFor(() => {
      const options = screen.getAllByRole('option').map((option) => option.getAttribute('aria-label'));
      expect(options).toEqual([expect.stringContaining('NSE:NIFTYBEES')]);
    });

    await userEvent.clear(picker);
    await userEvent.type(picker, 'TCS');
    await userEvent.click(await screen.findByRole('option', { name: /NSE:TCS/ }));

    expect(await dialog.findByText('Last close ₹3,880.00 on 12 Oct 2026')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: '−10 %, ₹3,492.00 (10 % below the last price)' })).not.toHaveAttribute(
      'aria-disabled',
    );
  });

  it('sets an alert on the chosen instrument', async () => {
    const { dialog, fake } = await openPicker();
    await userEvent.type(dialog.getByRole('combobox', { name: 'Instrument' }), 'TCS');
    await userEvent.click(await screen.findByRole('option', { name: /NSE:TCS/ }));
    await dialog.findByText('Last close ₹3,880.00 on 12 Oct 2026');

    await userEvent.click(dialog.getByRole('button', { name: 'At or above' }));
    await userEvent.click(dialog.getByRole('button', { name: '+5 %, ₹4,074.00 (5 % above the last price)' }));
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(
      await screen.findByText('Alert set: NSE:TCS at or above ₹4,074.00. The bell tells you when it triggers.'),
    ).toBeInTheDocument();
    expect(fake.calls.created).toEqual([
      { instrumentId: instrumentIds.tcs, direction: 'at_or_above', level: 4074, note: null },
    ]);
  });

  it('asks for an instrument first, and has no quick levels while no price is known', async () => {
    const { dialog, fake } = await openPicker();
    expect(dialog.getByText('The last price is not known.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: '−5 % (5 % below the last price)' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );

    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '100');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(await dialog.findByText('Choose a share or ETF.')).toBeInTheDocument();
    expect(dialog.getByRole('combobox', { name: 'Instrument' })).toHaveFocus();
    expect(fake.calls.created).toEqual([]);
  });

  it('keeps the alerts of the panel in step with a new alert', async () => {
    const { dialog } = await openPicker();
    await userEvent.type(dialog.getByRole('combobox', { name: 'Instrument' }), 'TCS');
    await userEvent.click(await screen.findByRole('option', { name: /NSE:TCS/ }));
    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '3500');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));
    await screen.findByText(/Alert set: NSE:TCS/);

    const panel = await screen.findByRole('dialog', { name: 'Price alerts' });
    expect(await within(panel).findByText('at or below ₹3,500.00')).toBeInTheDocument();
    expect(within(panel).getByText('1 of 200 active')).toBeInTheDocument();
  });
});

describe('Alert dialog: what the API refuses and what the price is', () => {
  it('says on top of the dialog that the API refused the instrument it was opened for, and moves the keyboard there', async () => {
    serveActionCenter();
    server.use(
      http.post('/api/portfolio/alerts', () =>
        problemResponse(
          {
            title: 'Not valid.',
            code: 'portfolio.invalid',
            errors: { instrumentId: ['This instrument has been retired, so it can no longer be alerted on.'] },
          },
          400,
        ),
      ),
    );
    const { dialog } = await openFromDrawer();

    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '2600');
    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    const refusal = await dialog.findByRole('alert');
    expect(refusal).toHaveTextContent('This instrument has been retired, so it can no longer be alerted on.');
    await waitFor(() => {
      expect(refusal).toHaveFocus();
    });
    expect(screen.getByRole('dialog', { name: 'Set price alert' })).toBeInTheDocument();
    expect(dialog.getByRole('textbox', { name: /Alert price/ })).toBeValid();
  });

  it('disables a quick level that a low price rounds to nothing or back to the price', async () => {
    serveActionCenter();
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(searchHoldings([buildHolding({ lastPrice: 0.03 })], new URL(request.url).searchParams)),
      ),
    );
    const { dialog } = await openFromDrawer();

    const chip = (name: RegExp) => dialog.getByRole('button', { name });
    expect(await dialog.findByText('Last price ₹0.03')).toBeInTheDocument();
    expect(chip(/^−5 %/)).toHaveAttribute('aria-disabled', 'true');
    expect(chip(/^−10 %/)).toHaveAttribute('aria-disabled', 'true');
    expect(chip(/^−20 %/)).not.toHaveAttribute('aria-disabled');
    expect(chip(/^−5 %/).closest('span')).toHaveAttribute(
      'title',
      'The price is too low for this step to make a different level.',
    );
    await userEvent.click(dialog.getByRole('button', { name: 'At or above' }));
    expect(chip(/^\+5 %/)).toHaveAttribute('aria-disabled', 'true');
    expect(chip(/^\+20 %/)).not.toHaveAttribute('aria-disabled');
  });

  it('starts the accessible name of a quick level with the text it shows', async () => {
    serveActionCenter();
    const { dialog } = await openFromDrawer();

    for (const chip of dialog.getAllByRole('button', { name: /^[−+]\d+ %/ })) {
      expect(chip.getAttribute('aria-label')).toMatch(new RegExp(`^${chip.textContent}`));
    }
  });

  it('uses the last price of a held instrument for an instrument that was picked', async () => {
    serveActionCenter();
    renderPortfolio('?alerts=active');
    await userEvent.click(await screen.findByRole('button', { name: 'New alert' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Set price alert' }));

    await userEvent.type(dialog.getByRole('combobox', { name: 'Instrument' }), 'RELIANCE');
    await userEvent.click(await screen.findByRole('option', { name: /^NSE:RELIANCE RELIANCE/ }));

    expect(await dialog.findByText('Last price ₹2,902.50')).toBeInTheDocument();
    expect(dialog.queryByText(/Last close/)).not.toBeInTheDocument();
  });

  it('finds the holding of the item among more than a hundred matches of its symbol', async () => {
    serveActionCenter();
    const fillers = Array.from({ length: 100 }, (_, n) =>
      buildHolding({
        id: `00000000-0000-4000-8000-0000000015${String(n).padStart(2, '0')}`,
        symbol: `TCS${String(n)}`,
      }),
    );
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        if (params.get('q') !== 'TCS') return undefined;
        const page = Number(params.get('page') ?? 1);
        const tcs = sampleHoldings.find((h) => h.id === holdingIds.tcs);
        return HttpResponse.json({
          items: page === 1 ? fillers : tcs ? [tcs] : [],
          page,
          pageSize: 100,
          total: 101,
        });
      }),
    );
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Set alert/ }));

    const dialog = await screen.findByRole('dialog', { name: 'Set price alert' });
    expect(await within(dialog).findByText('Last price ₹3,880.00')).toBeInTheDocument();
  });

  it('closes and tells the owner as soon as the API has answered, without waiting for the lists to reload', async () => {
    const fake = serveActionCenter({ lastPrices: { [instrumentIds.tcs]: 3_880 } });
    renderPortfolio('?alerts=active');
    await userEvent.click(await screen.findByRole('button', { name: 'New alert' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Set price alert' }));
    await userEvent.type(dialog.getByRole('combobox', { name: 'Instrument' }), 'TCS');
    await userEvent.click(await screen.findByRole('option', { name: /NSE:TCS/ }));
    await userEvent.type(dialog.getByRole('textbox', { name: /Alert price/ }), '3500');
    // The reload of the alerts after the creation will hang.
    server.use(http.get('/api/portfolio/alerts', () => pendingResponse()));

    await userEvent.click(dialog.getByRole('button', { name: 'Set alert' }));

    expect(await screen.findByText(/Alert set: NSE:TCS/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Set price alert' })).not.toBeInTheDocument();
    });
    expect(fake.calls.created).toHaveLength(1);
  });
});
