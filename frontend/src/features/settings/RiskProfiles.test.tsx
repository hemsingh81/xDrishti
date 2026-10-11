import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildRiskProfile, buildRiskProfiles, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

function renderProfiles() {
  return renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings' });
}

async function confirmPassword() {
  const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
  await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
}

const custom = () =>
  buildRiskProfile({ id: ids.conservative, name: 'My own', isSeeded: false, accountCount: 0, riskPerTradePct: 0.3 });

describe('Risk profiles', () => {
  it('shows every limit, the usage count and which profiles are built in', async () => {
    renderProfiles();

    const standard = await screen.findByRole('article', { name: 'Risk profile Standard' });
    expect(within(standard).getByText('Built-in')).toBeInTheDocument();
    expect(within(standard).getByText('Used by 1 account')).toBeInTheDocument();
    expect(within(standard).getByText('Risk per trade')).toBeInTheDocument();
    expect(within(standard).getByText('0.5%')).toBeInTheDocument();
    expect(within(standard).getByText('Max pair correlation (0–1)')).toBeInTheDocument();
    expect(within(standard).getByText('Halve position size')).toBeInTheDocument();
    const conservative = screen.getByRole('article', { name: 'Risk profile Conservative' });
    expect(within(conservative).getByText('Not used by any account')).toBeInTheDocument();
  });

  it('explains why a profile cannot be deleted', async () => {
    renderProfiles();

    const standard = await screen.findByRole('article', { name: 'Risk profile Standard' });
    expect(within(standard).getByRole('button', { name: 'Delete Standard' })).toBeDisabled();
    expect(within(standard).getByText('Built-in profiles cannot be deleted.')).toBeInTheDocument();
  });

  it('explains a profile that accounts still use', async () => {
    server.use(
      http.get('/api/risk-profiles', () => HttpResponse.json([buildRiskProfile(), { ...custom(), accountCount: 2 }])),
    );
    renderProfiles();

    const card = await screen.findByRole('article', { name: 'Risk profile My own' });
    expect(within(card).getByRole('button', { name: 'Delete My own' })).toBeDisabled();
    expect(within(card).getByText(/Used by 2 accounts — move them to another profile first/)).toBeInTheDocument();
  });

  it('creates a profile starting from an existing one (password asked on 403)', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.post('/api/risk-profiles', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        const body = await request.json();
        bodies.push(body);
        return HttpResponse.json(
          { ...buildRiskProfile(), id: ids.swing, name: 'Steady', isSeeded: false },
          { status: 201 },
        );
      }),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Start a new profile from Standard' }));
    const dialog = await screen.findByRole('dialog', { name: 'New risk profile' });
    const name = within(dialog).getByLabelText(/^Name/);
    expect(name).toHaveValue('Standard copy');
    expect(within(dialog).getByLabelText(/^Risk per trade/)).toHaveValue(0.5);
    await userEvent.clear(name);
    await userEvent.type(name, 'Steady');
    await userEvent.clear(within(dialog).getByLabelText(/^Max open positions/));
    await userEvent.type(within(dialog).getByLabelText(/^Max open positions/), '4');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create profile' }));
    await confirmPassword();

    expect(await screen.findByText('Risk profile created.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        name: 'Steady',
        riskPerTradePct: 0.5,
        maxCapitalPerTradePct: 20,
        maxNewEntriesPerDay: 3,
        maxOpenPositions: 4,
        maxTotalRiskPct: 3,
        maxPerSector: 2,
        maxPairCorrelation: 0.8,
        maxIntraday: 3,
        maxSwing: 5,
        dailyLossR: 3,
        dailyLossPct: 1.5,
        weeklyLossR: 6,
        weeklyLossAction: 'halve_size',
      },
    ]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('can start a new profile from the picker inside the dialog', async () => {
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'New profile' }));
    const dialog = await screen.findByRole('dialog', { name: 'New risk profile' });
    await waitFor(() => {
      expect(within(dialog).getByLabelText(/^Name/)).toHaveFocus();
    });
    expect(within(dialog).getByLabelText(/^Max open positions/)).toHaveValue(5);

    await userEvent.selectOptions(within(dialog).getByLabelText('Start from'), 'Conservative');

    expect(within(dialog).getByLabelText(/^Max open positions/)).toHaveValue(3);
    expect(within(dialog).getByLabelText(/^Name/)).toHaveValue('Conservative copy');
  });

  it('validates limits next to the fields before sending', async () => {
    let sent = false;
    server.use(
      http.put('/api/risk-profiles/:id', () => {
        sent = true;
        return HttpResponse.json(buildRiskProfile());
      }),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit Standard' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit Standard' });
    const risk = within(dialog).getByLabelText(/^Risk per trade/);
    await userEvent.clear(risk);
    await userEvent.type(risk, '2.5');
    await userEvent.clear(within(dialog).getByLabelText(/^Max per sector/));
    await userEvent.type(within(dialog).getByLabelText(/^Max per sector/), '1.5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save profile' }));

    expect(await within(dialog).findByText('Use a value above 0 and at most 2.')).toBeInTheDocument();
    expect(within(dialog).getByText('Use a whole number from 1 to 100.')).toBeInTheDocument();
    expect(sent).toBe(false);
  });

  it('edits a profile and maps server field errors to the fields', async () => {
    server.use(
      http.put('/api/risk-profiles/:id', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: { maxTotalRiskPct: ['Must be at least the risk per trade.'] },
          },
          400,
        ),
      ),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit Standard' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit Standard' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save profile' }));

    expect(await within(dialog).findByText('Must be at least the risk per trade.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Max total open risk/)).toHaveAccessibleDescription(
      'Must be at least the risk per trade.',
    );
  });

  it('saves an edited profile', async () => {
    const gate = createStepUpGate();
    let saved: { id: string; name: string } | undefined;
    server.use(
      gate.handler,
      http.put('/api/risk-profiles/:id', async ({ request, params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        saved = { id: String(params.id), name: ((await request.json()) as { name: string }).name };
        return HttpResponse.json(buildRiskProfile());
      }),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit Standard' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit Standard' });
    await userEvent.type(within(dialog).getByLabelText(/^Name/), ' plus');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save profile' }));
    await confirmPassword();

    expect(await screen.findByText('Risk profile saved.')).toBeInTheDocument();
    expect(saved).toEqual({ id: ids.standard, name: 'Standard plus' });
  });

  it('shows the server message when a delete is refused', async () => {
    server.use(
      http.get('/api/risk-profiles', () => HttpResponse.json([buildRiskProfile(), custom()])),
      http.delete('/api/risk-profiles/:id', () =>
        problemResponse(
          {
            title: 'The risk profile is used by 1 account(s). Move them to another profile first.',
            code: 'accounts.conflict',
          },
          409,
        ),
      ),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Delete My own' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete My own?' });
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus(); // the safe choice
    });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete profile' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'The risk profile is used by 1 account(s). Move them to another profile first.',
    );
  });

  it('deletes an unused profile after the password dialog and refreshes the list', async () => {
    const gate = createStepUpGate();
    let profiles = [buildRiskProfile(), custom()];
    server.use(
      gate.handler,
      http.get('/api/risk-profiles', () => HttpResponse.json(profiles)),
      http.delete('/api/risk-profiles/:id', () => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        profiles = buildRiskProfiles().slice(0, 1);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderProfiles();

    await userEvent.click(await screen.findByRole('button', { name: 'Delete My own' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Delete My own?' })).getByRole('button', {
        name: 'Delete profile',
      }),
    );
    await confirmPassword();

    expect(await screen.findByText(/deleted/)).toBeInTheDocument();
    expect(screen.queryByRole('article', { name: 'Risk profile My own' })).not.toBeInTheDocument();
  });
});
