import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildPortfolioSaved } from '@/test/fixtures';
import { problemResponse } from '@/test/server';
import { buildAccountSyncService, buildMasterService, buildServiceView } from '@/test/servicesFixtures';
import { chooseAction, renderServices, serviceCard } from './testing';

const sync = 'Account & portfolio sync';
const openSettings = async () => {
  await userEvent.click(await screen.findByRole('button', { name: `Settings: ${sync}` }));
  return screen.findByRole('dialog', { name: 'Account sync settings' });
};

describe('Services: the settings of the account sync', () => {
  it('opens from the Settings button with the accounts that have the Portfolio role and the sync’s next run', async () => {
    renderServices();

    const dialog = await openSettings();

    expect(within(dialog).getByText('Next sync Tue 13 Oct 16:00 IST')).toBeInTheDocument();
    const form = await within(dialog).findByRole('form', { name: 'Automatic refresh: Test Dhan Main' });
    expect(within(form).getByRole('radio', { name: 'Off' })).toBeChecked();
    // The account without the Portfolio role is not listed.
    expect(within(dialog).queryByRole('form', { name: /Test Dhan Swing/ })).not.toBeInTheDocument();
  });

  it('also opens from the Settings… item of the menu, and closes with Done, back to the button', async () => {
    renderServices();

    await chooseAction(sync, /^Settings/);
    const dialog = await screen.findByRole('dialog', { name: 'Account sync settings' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(screen.getByRole('button', { name: `Actions for ${sync}` })).toHaveFocus();
    });
  });

  it('saves the refresh through the accounts call, leaves everything else as stored and shows the new next run', async () => {
    const bodies: unknown[] = [];
    let saved = false;
    let listReads = 0;
    let account = buildAccountDetail();
    renderServices({
      handlers: [
        http.get('/api/services', () => {
          listReads += 1;
          return HttpResponse.json({
            items: [
              buildServiceView(),
              buildMasterService(),
              buildAccountSyncService({ nextRun: saved ? '2026-10-13T18:45:00+05:30' : '2026-10-13T16:00:00+05:30' }),
            ],
          });
        }),
        http.get('/api/accounts/:id', () => HttpResponse.json(account)),
        http.put('/api/accounts/:id/portfolio', async ({ request }) => {
          bodies.push(await request.json());
          saved = true;
          // The saved account has a new `updatedAt`, which replaces the form that saved: the page is read again anyway.
          account = buildAccountDetail({
            updatedAt: '2026-10-13T11:00:00Z',
            portfolio: { ...buildAccountDetail().portfolio, refreshMode: 'daily', refreshDailyTimeIst: '18:45' },
          });
          return HttpResponse.json(buildPortfolioSaved(account));
        }),
      ],
    });
    const dialog = await openSettings();
    const reads = listReads;

    const form = await within(dialog).findByRole('form', { name: 'Automatic refresh: Test Dhan Main' });
    await userEvent.click(within(form).getByRole('radio', { name: 'Daily at' }));
    const time = within(form).getByLabelText('Time of day (IST)');
    await userEvent.clear(time);
    await userEvent.type(time, '18:45');
    await userEvent.click(within(form).getByRole('button', { name: 'Save: Test Dhan Main' }));

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        includeInTotals: true,
        refreshMode: 'daily',
        refreshDailyTimeIst: '18:45',
        firstImportDate: '2025-01-01',
      },
    ]);
    // The services are read again, so the card shows the new next run.
    await waitFor(() => {
      expect(listReads).toBeGreaterThan(reads);
    });
    // The dialog's own line about the sync follows the list that was read again.
    expect(await within(dialog).findByText('Next sync Tue 13 Oct 18:45 IST')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }));
    expect(await within(await serviceCard(sync)).findByText('Tue 13 Oct 18:45')).toBeInTheDocument();
  });

  it('says it when saving fails, and keeps the dialog open', async () => {
    renderServices({
      handlers: [
        http.put('/api/accounts/:id/portfolio', () =>
          problemResponse({ title: 'The broker is unreachable.', code: 'x' }, 503),
        ),
      ],
    });
    const dialog = await openSettings();

    const form = await within(dialog).findByRole('form', { name: 'Automatic refresh: Test Dhan Main' });
    await userEvent.click(within(form).getByRole('radio', { name: 'Daily at' }));
    await userEvent.click(within(form).getByRole('button', { name: 'Save: Test Dhan Main' }));

    expect(await within(form).findByText('The broker is unreachable.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Account sync settings' })).toBeInTheDocument();
  });
});
