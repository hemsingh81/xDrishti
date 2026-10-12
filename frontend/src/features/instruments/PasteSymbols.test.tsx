import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { problemResponse, server } from '@/test/server';
import { renderTracking } from './trackingTestkit';

async function openDialog() {
  await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
  await userEvent.click(screen.getByRole('button', { name: 'Paste symbols' }));
  return screen.findByRole('dialog', { name: 'Paste symbols' });
}

const paste = async (dialog: HTMLElement, text: string) => {
  await userEvent.click(within(dialog).getByRole('textbox', { name: 'Symbols' }));
  await userEvent.paste(text);
};

describe('Paste symbols dialog', () => {
  it('opens with the box focused and nothing to look up yet', async () => {
    renderTracking();
    const dialog = await openDialog();

    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: 'Symbols' })).toHaveFocus();
    });
    expect(within(dialog).getByText('0 entries (at most 5,000)')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Look up' })).toBeDisabled();
  });

  it('looks the list up, previews matched and unmatched entries with reasons, and tracks the matched on confirm', async () => {
    const { fake } = renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'reliance, TCS\nINE040A01034\nnope\nNSE:TCS\nOLDCO');
    expect(within(dialog).getByText('6 entries (at most 5,000)')).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));

    expect(await within(dialog).findByText('4 matched · 1 not matched · 1 repeated')).toBeInTheDocument();
    expect(fake.calls.resolve).toEqual([['reliance', 'TCS', 'INE040A01034', 'nope', 'NSE:TCS', 'OLDCO']]);
    const matched = within(within(dialog).getByRole('table', { name: 'Matched instruments' }));
    expect(matched.getByRole('row', { name: /reliance NSE:RELIANCE/ })).toHaveTextContent('Will be tracked');
    expect(matched.getByRole('row', { name: /TCS NSE:TCS/ })).toHaveTextContent('Already tracked');
    expect(matched.getByRole('row', { name: /INE040A01034 NSE:HDFCBANK/ })).toHaveTextContent('Will be tracked');
    expect(matched.getByRole('row', { name: /OLDCO NSE:OLDCO/ })).toHaveTextContent('Retired, cannot be tracked');
    const unmatched = within(within(dialog).getByRole('list', { name: 'Entries that were not matched' }));
    expect(unmatched.getByText('nope')).toBeInTheDocument();
    expect(unmatched.getByText('Not found in the master list')).toBeInTheDocument();
    expect(fake.calls.track).toEqual([]); // nothing is tracked before the owner confirms

    await userEvent.click(within(dialog).getByRole('button', { name: 'Track 2 instruments' }));

    expect(await screen.findByText('Tracked 2 instruments (2 new)')).toBeInTheDocument();
    expect(fake.calls.track).toHaveLength(1);
    expect(fake.calls.track[0]).toHaveLength(2);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Paste symbols' })).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('tab', { name: 'Tracked 4' })).toBeInTheDocument(); // TCS + GONECO + 2 new
  });

  it('goes back to edit the list from the preview', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'RELIANCE');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));
    await within(dialog).findByText('1 matched · 0 not matched');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Back' }));

    expect(within(dialog).getByRole('textbox', { name: 'Symbols' })).toHaveValue('RELIANCE');
  });

  it('has nothing to confirm when everything is tracked already or nothing matched', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'TCS xyz');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));

    expect(await within(dialog).findByRole('button', { name: 'Nothing to track' })).toBeDisabled();
    expect(within(dialog).getByText('0 will be tracked now, 1 already are.')).toBeInTheDocument();
  });

  it('says when no entry matched at all', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'xyz abc');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));

    expect(await within(dialog).findByText('None of the entries matched an instrument.')).toBeInTheDocument();
    expect(within(dialog).getByText('Not matched (2)')).toBeInTheDocument();
  });

  it('refuses more than 5,000 entries before asking the server', async () => {
    const { fake } = renderTracking();
    const dialog = await openDialog();
    const field = within(dialog).getByRole('textbox', { name: 'Symbols' });
    await userEvent.click(field);
    await userEvent.paste(Array.from({ length: 5_001 }, (_, n) => `S${String(n)}`).join('\n'));

    expect(within(dialog).getByText('Too many: 5,001 entries, at most 5,000 at a time.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Look up' })).toBeDisabled();
    expect(fake.calls.resolve).toEqual([]);
  });

  it('reads a CSV file in the browser: the symbol column, or the first column without a header', async () => {
    renderTracking();
    const dialog = await openDialog();
    const upload = within(dialog).getByLabelText('Upload CSV file');

    await userEvent.upload(
      upload,
      new File(['name,symbol,qty\n"Reliance, Inds",RELIANCE,10\nTata Consultancy,TCS,5\n'], 'list.csv', {
        type: 'text/csv',
      }),
    );
    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: 'Symbols' })).toHaveValue('RELIANCE\nTCS');
    });

    await userEvent.upload(
      upload,
      new File(['INE040A01034,HDFC Bank\r\nNIFTY,Nifty 50\r\n'], 'plain.csv', { type: 'text/csv' }),
    );
    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: 'Symbols' })).toHaveValue(
        'RELIANCE\nTCS\nINE040A01034\nNIFTY',
      );
    });
  });

  it('says so when a file has no symbols or is too large', async () => {
    renderTracking();
    const dialog = await openDialog();
    const upload = within(dialog).getByLabelText('Upload CSV file');

    await userEvent.upload(upload, new File(['symbol\n'], 'empty.csv', { type: 'text/csv' }));
    expect(await within(dialog).findByText('No symbols were found in that file.')).toBeInTheDocument();

    await userEvent.upload(upload, new File(['x'.repeat(1_000_001)], 'big.csv', { type: 'text/csv' }));
    expect(await within(dialog).findByText(/That file is too large/)).toBeInTheDocument();
  });

  it('shows why a look-up failed and lets the owner try again', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'RELIANCE');
    server.use(
      http.post('/api/instruments/tracked/resolve', () => problemResponse({ title: 'Look-up unavailable.' }, 500)),
    );

    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));

    expect(await within(dialog).findByText('Look-up unavailable.')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: 'Symbols' })).toHaveValue('RELIANCE');
    expect(within(dialog).getByRole('button', { name: 'Look up' })).toBeEnabled();
  });

  it('keeps the preview and shows a toast when tracking the matched fails', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'RELIANCE');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Look up' }));
    await within(dialog).findByText('1 matched · 0 not matched');
    server.use(http.post('/api/instruments/tracked', () => HttpResponse.error()));

    await userEvent.click(within(dialog).getByRole('button', { name: 'Track 1 instrument' }));

    expect(await screen.findByText(/Could not reach the server/)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Paste symbols' })).toBeInTheDocument();
  });

  it('starts empty each time it is opened', async () => {
    renderTracking();
    const dialog = await openDialog();
    await paste(dialog, 'RELIANCE');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Paste symbols' })).not.toBeInTheDocument();
    });

    await userEvent.click(screen.getByRole('button', { name: 'Paste symbols' }));

    const again = await screen.findByRole('dialog', { name: 'Paste symbols' });
    expect(within(again).getByRole('textbox', { name: 'Symbols' })).toHaveValue('');
  });
});
