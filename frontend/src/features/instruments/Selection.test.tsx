import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { scrollToEnd } from '@/test/intersection';
import { problemResponse, server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { manyInstruments, renderTracking } from './trackingTestkit';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const bulkButton = (name: string) => screen.getByRole('button', { name });
const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const noSelection = 'Select instruments to track or untrack them in bulk';

describe('Instruments page: selection and the bulk bar', () => {
  it('starts with nothing selected: Track, Untrack and Add to basket are off, Paste symbols is on', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    expect(screen.getByText(noSelection)).toBeInTheDocument();
    expect(bulkButton('Track')).toBeDisabled();
    expect(bulkButton('Untrack')).toBeDisabled();
    expect(bulkButton('Paste symbols')).toBeEnabled();
    expect(bulkButton('Add to basket')).toBeDisabled();
  });

  it('counts the selected rows in the buttons and clears the selection', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(rowBox('NSE:TCS'));

    expect(screen.getByText('2 selected')).toBeInTheDocument();
    expect(bulkButton('Track (2)')).toBeEnabled();
    expect(bulkButton('Untrack (2)')).toBeEnabled();
    expect(bulkButton('Add to basket (2)')).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(rowBox('NSE:TCS')).not.toBeChecked();
    expect(bulkButton('Track')).toBeDisabled();
  });

  it('toggles a focused row with the Space key', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    screen.getByRole('row', { name: /NSE:RELIANCE / }).focus();
    await userEvent.keyboard(' ');

    expect(rowBox('NSE:RELIANCE')).toBeChecked();
    expect(screen.getByText('1 selected')).toBeInTheDocument();
  });

  it('tracks the selected rows in one call, reports the outcome and clears the selection', async () => {
    const { fake } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(rowBox('NSE:TCS'));

    await userEvent.click(bulkButton('Track (2)'));

    expect(await screen.findByText('Tracked 1 instrument (1 new, 1 already tracked)')).toBeInTheDocument();
    expect(fake.calls.track).toHaveLength(1);
    expect(fake.calls.track[0]).toHaveLength(2);
    expect(await screen.findByText(noSelection)).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Tracked 3' })).toBeInTheDocument();
  });

  it('asks before untracking the selection and then untracks it', async () => {
    const { fake } = renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:TCS' });
    await userEvent.click(rowBox('NSE:TCS'));

    await userEvent.click(bulkButton('Untrack (1)'));
    const dialog = await screen.findByRole('dialog', { name: 'Untrack this instrument?' });
    expect(within(dialog).getByText(/stop for 1 instrument\./)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Untrack' }));

    expect(await screen.findByText('1 instrument untracked — its history is kept')).toBeInTheDocument();
    expect(fake.calls.untrack).toHaveLength(1);
  });

  it('keeps the selection when a bulk action fails, and tells why', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    server.use(
      http.post('/api/instruments/tracked', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'instruments.invalid',
            errors: { instrumentIds: ['Unknown id.'] },
          },
          400,
        ),
      ),
    );
    await userEvent.click(rowBox('NSE:RELIANCE'));

    await userEvent.click(bulkButton('Track (1)'));

    expect(await screen.findByText('Unknown id.')).toBeInTheDocument();
    expect(rowBox('NSE:RELIANCE')).toBeChecked();
  });

  it('starts a new selection when the filters or the tab change, but not when the sort does', async () => {
    renderTracking();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));

    await userEvent.click(screen.getByRole('button', { name: 'Name' }));
    await vi.waitFor(() => {
      expect(screen.getByRole('columnheader', { name: 'Name' })).toHaveAttribute('aria-sort', 'ascending');
    });
    expect(screen.getByText('1 selected')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^ETF/ }));
    expect(await screen.findByText(noSelection)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^ETF/ }));
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Select NSE:TCS' }));
    await userEvent.click(screen.getByRole('tab', { name: /^Tracked/ }));
    await screen.findByRole('grid', { name: 'Tracked instruments' });
    expect(await screen.findByText(noSelection)).toBeInTheDocument();
  });
});

describe('Instruments page: selecting across pages', () => {
  const total = smallPage * 1.5; // two pages, the second one half full
  const items = manyInstruments(total);

  it('selects the loaded rows, offers all results, and keeps the selection while more rows load', async () => {
    renderTracking(items);
    await screen.findByText(`Showing ${String(smallPage)} of ${String(total)}`);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(screen.getByText(`${String(smallPage)} selected`)).toBeInTheDocument();

    scrollToEnd();
    await screen.findByText(`Showing ${String(total)} of ${String(total)}`);
    expect(screen.getByText(`${String(smallPage)} selected`)).toBeInTheDocument();
    expect(rowBox('NSE:M0000')).toBeChecked();
    expect(rowBox(`NSE:M${String(smallPage).padStart(4, '0')}`)).not.toBeChecked();
    await userEvent.click(rowBox(`NSE:M${String(smallPage).padStart(4, '0')}`));
    expect(screen.getByText(`${String(smallPage + 1)} selected`)).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select all loaded rows' })).toHaveAttribute(
      'data-indeterminate',
      'true',
    );
  });

  it('selects all N results by reading every page, and tracks them in one call', async () => {
    const { fake } = renderTracking(items);
    await screen.findByText(`Showing ${String(smallPage)} of ${String(total)}`);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    await userEvent.click(screen.getByRole('button', { name: `Select all ${String(total)} results` }));

    expect(await screen.findByText(`All ${String(total)} results are selected`)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: new RegExp(`Select all ${String(total)}`) })).not.toBeInTheDocument();
    await userEvent.click(bulkButton(`Track (${String(total)})`));

    expect(await screen.findByText(`Tracked ${String(total)} instruments (${String(total)} new)`)).toBeInTheDocument();
    expect(fake.calls.track.map((ids) => ids.length)).toEqual([total]);
  });

  it('sends more than 500 ids in calls of at most 500', async () => {
    const { fake } = renderTracking(manyInstruments(1_150));
    await screen.findByText(`Showing ${String(smallPage)} of 1,150`);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    await userEvent.click(screen.getByRole('button', { name: 'Select all 1,150 results' }));
    await screen.findByText('All 1,150 results are selected');

    await userEvent.click(bulkButton('Track (1,150)'));

    expect(await screen.findByText('Tracked 1,150 instruments (1,150 new)')).toBeInTheDocument();
    expect(fake.calls.track.map((ids) => ids.length)).toEqual([500, 500, 150]);
    expect(new Set(fake.calls.track.flat()).size).toBe(1_150);
  });

  it('says so when not all results could be selected, and keeps what was selected', async () => {
    renderTracking(items);
    await screen.findByText(`Showing ${String(smallPage)} of ${String(total)}`);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    server.use(
      http.get('/api/instruments', ({ request }) =>
        new URL(request.url).searchParams.get('pageSize') === String(smallPage)
          ? problemResponse({ title: 'Reading failed.' }, 500)
          : undefined,
      ),
    );

    await userEvent.click(screen.getByRole('button', { name: `Select all ${String(total)} results` }));

    expect(await screen.findByText('Reading failed.')).toBeInTheDocument();
    expect(screen.getByText(`${String(smallPage)} selected`)).toBeInTheDocument();
  });
});
