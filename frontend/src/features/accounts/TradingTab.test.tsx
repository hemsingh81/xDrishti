import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';

function renderTrading(path = '/accounts?tab=trading') {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], { initialPath: path });
}

async function capitalField() {
  return screen.findByLabelText(/^Capital \(₹\)/);
}

describe('Trading tab', () => {
  it('computes the effective limits from the profile and the overrides', async () => {
    renderTrading();

    expect(await screen.findByText(/Effective: 0.5% risk per trade = ₹1,000 per 1R/)).toHaveTextContent(
      'Effective: 0.5% risk per trade = ₹1,000 per 1R · max 3 new entries/day · max 5 open · daily loss 1.5%',
    );

    await userEvent.type(screen.getByLabelText('Risk per trade (%)'), '1');
    expect(screen.getByText(/Effective: 1% risk per trade = ₹2,000 per 1R/)).toBeInTheDocument();

    const capital = await capitalField();
    await userEvent.clear(capital);
    await userEvent.type(capital, '50000');
    expect(screen.getByText(/Effective: 1% risk per trade = ₹500 per 1R/)).toBeInTheDocument();

    // Another profile changes the base values the blank overrides fall back to.
    await userEvent.clear(screen.getByLabelText('Risk per trade (%)'));
    await userEvent.selectOptions(screen.getByLabelText(/^Risk profile/), 'Conservative');
    expect(
      screen.getByText(/Effective: 0.25% risk per trade = ₹125 per 1R · max 2 new entries\/day · max 3 open/),
    ).toBeInTheDocument();
  });

  it('shows the profile values as placeholders for the overrides', async () => {
    renderTrading();

    expect(await screen.findByLabelText('Max new entries per day')).toHaveAttribute('placeholder', '3');
  });

  it('validates before sending anything', async () => {
    let sent = false;
    server.use(
      http.put('/api/accounts/:id/trading', () => {
        sent = true;
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderTrading();

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Swing' }));
    await userEvent.type(screen.getByLabelText('Risk per trade (%)'), '3');
    await userEvent.clear(await capitalField());
    await userEvent.click(screen.getByRole('button', { name: 'Save trading settings' }));

    expect(await screen.findByText('Choose at least one style (intraday or swing).')).toBeInTheDocument();
    expect(screen.getByText('Risk per trade must be more than 0 and at most 2 %.')).toBeInTheDocument();
    expect(screen.getByText('Capital must be more than 0.')).toBeInTheDocument();
    expect(sent).toBe(false);
  });

  it('requires an instrument for each chosen style', async () => {
    renderTrading();

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Intraday' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save trading settings' }));

    expect(await screen.findByText('Choose at least one instrument for intraday.')).toBeInTheDocument();
  });

  it('saves after the password dialog (403 -> password -> retry) and sends the full configuration', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/trading', async ({ request, params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push({ id: params.id, body: await request.json() });
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderTrading();

    await userEvent.selectOptions(await screen.findByLabelText('Mode'), 'Manual live');
    await userEvent.type(screen.getByLabelText('Max open positions'), '4');
    await userEvent.click(screen.getByRole('button', { name: 'Save trading settings' }));

    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText(/Trading settings saved/)).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        id: ids.main,
        body: {
          mode: 'manual_live',
          riskProfileId: ids.standard,
          capitalSource: 'fixed',
          capitalAmount: 200000,
          styles: ['swing'],
          instruments: { intraday: [], swingBuy: ['EQ_CNC'], swingSell: [] },
          overrides: { riskPerTradePct: null, maxNewEntriesPerDay: null, maxOpenPositions: 4, dailyLossPct: null },
        },
      },
    ]);
  });

  it('maps server field errors back to the fields', async () => {
    server.use(
      http.put('/api/accounts/:id/trading', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            status: 400,
            errors: {
              capitalAmount: ['Capital must be more than 0.'],
              'overrides.dailyLossPct': ['Use a percentage above 0 and at most 100.'],
            },
          },
          400,
        ),
      ),
    );
    renderTrading();

    await userEvent.click(await screen.findByRole('button', { name: 'Save trading settings' }));

    const capital = await capitalField();
    await waitFor(() => {
      expect(capital).toHaveAccessibleDescription('Capital must be more than 0.');
    });
    expect(screen.getByLabelText('Daily loss limit (%)')).toHaveAccessibleDescription(
      'Use a percentage above 0 and at most 100.',
    );
  });

  it('shows a conflict message that is not about one field', async () => {
    server.use(
      http.put('/api/accounts/:id/trading', () =>
        problemResponse(
          { title: 'The account was changed elsewhere. Reload and try again.', code: 'accounts.conflict' },
          409,
        ),
      ),
    );
    renderTrading();

    await userEvent.click(await screen.findByRole('button', { name: 'Save trading settings' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The account was changed elsewhere. Reload and try again.',
    );
  });

  it('does not show an error when the password dialog is cancelled', async () => {
    const gate = createStepUpGate();
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/trading', () => gate.require() ?? HttpResponse.json(buildAccountDetail())),
    );
    renderTrading();

    await userEvent.click(await screen.findByRole('button', { name: 'Save trading settings' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('lets the owner prepare settings while the role is off, with a pointer to the Roles tab', async () => {
    renderTrading(`/accounts?account=${ids.swing}&tab=portfolio`);

    expect(await screen.findByText(/role is off for this account/)).toBeInTheDocument();
    expect(screen.getByLabelText('First import date')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enable in Roles' }));
    expect(await screen.findByRole('tab', { name: 'Roles', selected: true })).toBeInTheDocument();
  });
});
