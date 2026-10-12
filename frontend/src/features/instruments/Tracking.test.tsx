import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { searchInstruments } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { renderInstruments } from './testing';
import { gridOf, renderTracking, symbolsShown, trackedSample } from './trackingTestkit';

const tracked = (name: string) => screen.getByRole('row', { name: new RegExp(name) });

describe('Instruments page: tabs and counts', () => {
  it('shows the tracked count in the header and on the tab, and the master list by default', async () => {
    renderTracking();

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(await screen.findByText(/2 tracked/)).toHaveTextContent('2 tracked (1 no longer in Dhan');
    expect(screen.getByRole('tab', { name: 'Master list 8', selected: true })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Tracked 2', selected: false })).toBeInTheDocument();
  });

  it('opens the Tracked tab from the URL and keeps the tab in the URL', async () => {
    const { router } = renderTracking(undefined, '?tab=tracked');

    await gridOf('Tracked instruments');
    await vi.waitFor(() => {
      expect(symbolsShown('Tracked instruments')).toEqual(['NSE:GONECO', 'NSE:TCS']);
    });

    await userEvent.click(screen.getByRole('tab', { name: /^Master list/ }));
    await gridOf('Instruments');
    expect(router.state.location.search).toBe('');
    await userEvent.click(screen.getByRole('tab', { name: /^Tracked/ }));
    await gridOf('Tracked instruments');
    expect(router.state.location.search).toBe('?tab=tracked');
  });

  it('lists only tracked instruments, retired ones included, with coverage columns and the retired warning', async () => {
    renderTracking(undefined, '?tab=tracked');

    await screen.findByRole('gridcell', { name: 'NSE:TCS' });
    for (const name of ['Data from', 'Data to', 'Last update', 'Quality', 'Status']) {
      expect(screen.getByRole('columnheader', { name })).toBeInTheDocument();
    }
    expect(screen.queryByRole('columnheader', { name: 'Baskets' })).not.toBeInTheDocument();
    expect(screen.queryByRole('gridcell', { name: 'NSE:RELIANCE' })).not.toBeInTheDocument();

    const tcs = within(tracked('NSE:TCS'));
    expect(tcs.getByText('History collected')).toBeInTheDocument();
    const gone = within(tracked('NSE:GONECO'));
    expect(gone.getByText('History not collected yet')).toBeInTheDocument();
    expect(gone.getByText("No longer in Dhan's list")).toBeInTheDocument();
    // No history yet: every coverage cell is a dash.
    expect(gone.getAllByRole('gridcell').filter((cell) => cell.textContent === '—')).toHaveLength(4);
  });

  it('hides the retired switch on the Tracked tab and keeps the tab when filters are cleared', async () => {
    const { router } = renderTracking(undefined, '?tab=tracked&q=tcs');

    await screen.findByRole('gridcell', { name: 'NSE:TCS' });
    expect(screen.queryByRole('checkbox', { name: /Show retired/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear all filters' }));
    await screen.findByRole('gridcell', { name: 'NSE:GONECO' });
    expect(router.state.location.search).toBe('?tab=tracked');
  });

  it('says honestly that history is not collected yet when no tracked instrument has any', async () => {
    renderTracking(
      trackedSample.filter((i) => i.symbol !== 'NSE:TCS'),
      '?tab=tracked',
    );

    expect(await screen.findByText(/History collection starts with the EOD service \(P1-12\)/)).toBeInTheDocument();
  });

  it('shows a helpful empty state when nothing is tracked, with the way forward', async () => {
    const { router } = renderTracking(
      trackedSample.map((i) => ({ ...i, tracked: { ...i.tracked, active: false } })),
      '?tab=tracked',
    );

    expect(await screen.findByText('No instruments are tracked yet')).toBeInTheDocument();
    expect(screen.getByText(/History collection starts with the EOD service \(P1-12\)/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Browse the master list' }));
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(router.state.location.search).toBe('');
  });

  it('filters the master list to the instruments that are not tracked', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('button', { name: 'Not tracked only 7' }));

    await vi.waitFor(() => {
      expect(symbolsShown()).not.toContain('NSE:TCS');
    });
    expect(symbolsShown()).toHaveLength(7);
  });

  it('marks tracked instruments in the master list', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:TCS' });

    expect(within(tracked('NSE:TCS')).getByText('Tracked')).toBeInTheDocument();
    expect(within(tracked('NSE:RELIANCE ')).queryByText('Tracked')).not.toBeInTheDocument();
  });
});

describe('Instruments page: track and untrack one instrument', () => {
  it('tracks from the row button, tells the owner and updates the row, the counts and the tab', async () => {
    const { fake } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));

    expect(await screen.findByText('NSE:RELIANCE tracked')).toBeInTheDocument();
    expect(fake.calls.track).toEqual([[fake.items()[0]?.id]]);
    expect(await screen.findByRole('button', { name: 'Untrack NSE:RELIANCE' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Tracked 3' })).toBeInTheDocument();
    expect(await screen.findByText(/3 tracked/)).toBeInTheDocument();
  });

  it('does not open the drawer when the row button is used', async () => {
    const { router } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));

    await screen.findByText('NSE:RELIANCE tracked');
    expect(router.state.location.search).not.toContain('id=');
  });

  it('asks before untracking, explains that history is kept, and untracks on confirm', async () => {
    const { fake } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:TCS' });

    await userEvent.click(screen.getByRole('button', { name: 'Untrack NSE:TCS' }));
    const dialog = await screen.findByRole('dialog', { name: 'Untrack this instrument?' });
    expect(within(dialog).getByText(/Existing history is kept/)).toBeInTheDocument();
    expect(fake.calls.untrack).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Untrack' }));

    expect(await screen.findByText('NSE:TCS untracked — its history is kept')).toBeInTheDocument();
    expect(fake.calls.untrack).toHaveLength(1);
    expect(await screen.findByRole('button', { name: 'Track NSE:TCS' })).toBeInTheDocument();
  });

  it("marks the baskets' members as outdated, so their Tracked chip follows a tracking change", async () => {
    const { client } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:TCS' });
    const key = ['baskets', 'members', 'some-basket', 'now'];
    client.setQueryData(key, { items: [] });

    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));

    await screen.findByText('NSE:RELIANCE tracked');
    await waitFor(() => {
      expect(client.getQueryState(key)?.isInvalidated).toBe(true);
    });
  });

  it('does nothing when the untrack question is cancelled', async () => {
    const { fake } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:TCS' });

    await userEvent.click(screen.getByRole('button', { name: 'Untrack NSE:TCS' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.untrack).toEqual([]);
    expect(screen.getByRole('button', { name: 'Untrack NSE:TCS' })).toBeInTheDocument();
  });

  it('cannot track an instrument that has retired', async () => {
    renderTracking(undefined, '?retired=1');
    await screen.findByRole('gridcell', { name: 'NSE:OLDCO' });

    expect(screen.getByRole('button', { name: 'Track NSE:OLDCO' })).toBeDisabled();
  });

  it('shows the reason in a toast when the server refuses (retired, collision, unreachable)', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    server.use(
      http.post('/api/instruments/tracked', () =>
        problemResponse(
          {
            title: 'Retired instruments cannot be tracked.',
            code: 'instruments.retired',
            errors: { instrumentIds: ["Not in Dhan's list any more, so it cannot be tracked: NSE:RELIANCE."] },
          },
          400,
        ),
      ),
    );

    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));
    expect(await screen.findByText(/cannot be tracked: NSE:RELIANCE/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Track NSE:RELIANCE' })).toBeInTheDocument();

    server.use(
      http.post('/api/instruments/tracked', () =>
        problemResponse(
          { title: 'The tracked list was changed at the same time. Try again.', code: 'instruments.tracking_collided' },
          409,
        ),
      ),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));
    expect(await screen.findByText('The tracked list was changed at the same time. Try again.')).toBeInTheDocument();

    server.use(http.post('/api/instruments/tracked', () => HttpResponse.error()));
    await userEvent.click(screen.getByRole('button', { name: 'Track NSE:RELIANCE' }));
    expect(await screen.findByText(/Could not reach the server/)).toBeInTheDocument();
  });

  it('shows the tracking status in the instrument drawer and tracks from it', async () => {
    renderTracking();
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:TCS' }));

    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    expect(await within(drawer).findByText(/Tracked since/)).toBeInTheDocument();
    const term = (name: string) => within(drawer).getByText(name, { selector: 'dt' }).nextElementSibling?.textContent;
    expect(term('Data coverage')).toBe('1 Jan 2021 to 9 Oct 2026');
    expect(within(drawer).getByRole('button', { name: 'Untrack NSE:TCS' })).toBeInTheDocument();
  });

  it('says in the drawer that history is not collected yet, and warns about a retired tracked instrument', async () => {
    renderTracking(undefined, '?tab=tracked');
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:GONECO' }));

    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    const term = async (name: string) =>
      (await within(drawer).findByText(name, { selector: 'dt' })).nextElementSibling?.textContent;
    expect(await term('Data coverage')).toBe('History not collected yet');
    expect(await term('History')).toMatch(/EOD service \(P1-12\)/);
    expect(await term('Warning')).toMatch(/No longer in Dhan's list/);
    expect(within(drawer).getByRole('button', { name: 'Untrack NSE:GONECO' })).toBeInTheDocument();
  });

  it('shows an error with Retry when the Tracked tab cannot load, and recovers', async () => {
    let failing = true;
    server.use(
      http.get('/api/instruments', ({ request }) =>
        failing
          ? problemResponse({ title: 'The tracked list failed.' }, 500)
          : HttpResponse.json(searchInstruments(trackedSample, new URL(request.url).searchParams)),
      ),
    );
    renderInstruments('?tab=tracked');

    expect(await screen.findByText('The tracked list failed.')).toBeInTheDocument();
    failing = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('gridcell', { name: 'NSE:TCS' })).toBeInTheDocument();
  });
});
