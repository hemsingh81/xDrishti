import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { BasketsFake } from '@/test/basketsFake';
import { buildInstrument, sampleInstruments, trackedInfo } from '@/test/fixtures';
import { basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';

const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const toast = () => screen.findByRole('alert');

describe('Baskets page: removing members', () => {
  it('removes a row and Undo puts it back in its place', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Remove NSE:TCS from MyChoice' }));

    expect(await toast()).toHaveTextContent('Removed NSE:TCS from MyChoice');
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
    expect(fake.calls.remove).toEqual([{ id: basketIds.myChoice, instrumentIds: [expect.any(String)] }]);

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(await screen.findByText('Restored NSE:TCS in MyChoice')).toBeInTheDocument();
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
  });

  it('removes with the keyboard alone, and Undo is reached and pressed with the keyboard', async () => {
    renderBaskets();
    await membersGrid();
    const remove = screen.getByRole('button', { name: 'Remove NSE:TCS from MyChoice' });
    remove.focus();

    await userEvent.keyboard('{Enter}');

    expect(await toast()).toHaveTextContent('Removed NSE:TCS from MyChoice');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Undo' })).toHaveFocus();
    });

    await userEvent.keyboard('{Enter}');

    expect(await screen.findByText('Restored NSE:TCS in MyChoice')).toBeInTheDocument();
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
  });

  it('keeps the notes when Undo puts removed rows back', async () => {
    const { fake } = renderBaskets({
      setup: (f) => {
        f.setNote(basketIds.myChoice, 'NSE:TCS', 'Core holding');
        f.setNote(basketIds.myChoice, 'NSE:RELIANCE', 'Core holding');
        f.setNote(basketIds.myChoice, 'NSE:HDFCBANK', 'Watch the results');
      },
    });
    await membersGrid();
    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(rowBox('NSE:HDFCBANK'));
    await userEvent.click(screen.getByRole('button', { name: 'Remove (3)' }));
    await toast();

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(await screen.findByText('Restored 3 instruments in MyChoice')).toBeInTheDocument();
    // One call per distinct note, each with its note.
    expect(fake.calls.add.map((c) => ({ note: c.note, count: c.instrumentIds.length }))).toEqual([
      { note: 'Core holding', count: 2 },
      { note: 'Watch the results', count: 1 },
    ]);
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
  });

  it('removes the selection with one call and clears it', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:HDFCBANK'));

    await userEvent.click(screen.getByRole('button', { name: 'Remove (2)' }));

    expect(await toast()).toHaveTextContent('Removed 2 instruments from MyChoice');
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:RELIANCE']);
    });
    expect(fake.calls.remove).toHaveLength(1);
    expect(screen.getByText('Select instruments to copy, move or remove them')).toBeInTheDocument();
  });

  it('keeps the rows and says why when the removal fails', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('remove', 409, 'baskets.archived', 'The basket is archived.');

    await userEvent.click(screen.getByRole('button', { name: 'Remove NSE:TCS from MyChoice' }));

    expect(await toast()).toHaveTextContent('The basket is archived.');
    expect(symbolsShown()).toHaveLength(3);
  });
});

describe('Baskets page: status of members', () => {
  it("says both facts for an instrument that left Dhan's list but is still tracked", async () => {
    const gone = buildInstrument({
      id: '00000000-0000-4000-8000-000000000299',
      symbol: 'NSE:GONECO',
      displayName: 'Gone Co',
      retired: true,
      tracked: trackedInfo({ retiredButTracked: true }),
    });
    const retired = buildInstrument({
      id: '00000000-0000-4000-8000-000000000298',
      symbol: 'NSE:OLDER',
      displayName: 'Older',
      retired: true,
    });
    renderBaskets({
      instruments: [...sampleInstruments, gone, retired],
      members: { [basketIds.myChoice]: ['NSE:TCS', 'NSE:GONECO', 'NSE:OLDER'] },
    });
    await membersGrid();

    expect(
      within(screen.getByRole('row', { name: /NSE:GONECO/ })).getByText("Tracked · no longer in Dhan's list"),
    ).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /NSE:OLDER/ })).getByText('Retired')).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /NSE:TCS/ })).getByText('Not tracked')).toBeInTheDocument();
  });
});

describe('Baskets page: history', () => {
  it('lists additions and removals with dates, newest first', async () => {
    renderBaskets();
    await membersGrid();
    await userEvent.click(screen.getByRole('button', { name: 'Remove NSE:TCS from MyChoice' }));
    await screen.findByRole('alert');

    await userEvent.click(screen.getByRole('button', { name: 'History' }));

    const drawer = await screen.findByLabelText('History of MyChoice');
    const list = await within(drawer).findByRole('list', { name: 'Changes' });
    const entries = within(list).getAllByRole('listitem');
    expect(entries).toHaveLength(4);
    expect(entries[0]).toHaveTextContent('Removed');
    expect(entries[0]).toHaveTextContent('NSE:TCS');
    expect(entries[0]).toHaveTextContent('by example@invalid.test');
    expect(entries[0]).toHaveTextContent(/Oct 2026/);
    expect(within(list).getAllByText('Added')).toHaveLength(3);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close history' }));
    await waitFor(() => {
      expect(screen.queryByLabelText('History of MyChoice')).not.toBeInTheDocument();
    });
  });

  it('says it shows the newest 200 and that older ones are not shown when it is that long', async () => {
    renderBaskets({
      setup: (fake) => {
        fake.seedHistory(basketIds.myChoice, 'NSE:TCS', 150);
      },
    });
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'History' }));

    const drawer = await screen.findByLabelText('History of MyChoice');
    const list = await within(drawer).findByRole('list', { name: 'Changes' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(200);
    expect(within(drawer).getByText('The newest 200 additions and removals')).toBeInTheDocument();
    expect(within(drawer).getByText('Showing the newest 200 changes. Older ones are not shown.')).toBeInTheDocument();
  });

  it('shows no such hint for a short history', async () => {
    renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'History' }));

    const drawer = await screen.findByLabelText('History of MyChoice');
    await within(drawer).findByRole('list', { name: 'Changes' });
    expect(within(drawer).queryByText(/Older ones are not shown/)).not.toBeInTheDocument();
  });

  it('says when nothing happened yet, and offers Retry on failure', async () => {
    const { fake } = renderBaskets({ members: {} });
    await membersGrid();
    fake.failNext('history', 500, 'baskets.error', 'The history could not be read.');

    await userEvent.click(screen.getByRole('button', { name: 'History' }));
    expect(await screen.findByText('The history could not be read.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Nothing has been added or removed yet.')).toBeInTheDocument();
  });
});

describe('Baskets page: view as of a date', () => {
  const setup = (fake: BasketsFake) => {
    fake.seedStay(basketIds.myChoice, 'NSE:YESBANK', '2025-01-10T04:00:00Z', '2025-06-05T04:00:00Z');
    fake.seedStay(basketIds.myChoice, 'NSE:TINYCO', '2025-01-10T04:00:00Z');
  };

  it('shows the members as they were, read only, and goes back to today', async () => {
    const { fake, router } = renderBaskets({ setup });
    await membersGrid();

    fireEvent.change(screen.getByLabelText('View as of'), { target: { value: '2025-03-15' } });

    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:YESBANK', 'NSE:TINYCO']);
    });
    expect(fake.calls.members.at(-1)).toEqual({ id: basketIds.myChoice, asOf: '2025-03-15' });
    expect(router.state.location.search).toBe('?asOf=2025-03-15');
    expect(screen.getByText(/Showing the members as they were at the end of .*2025/)).toBeInTheDocument();
    expect(screen.getByText(/This view is read only/)).toBeInTheDocument();
    // Read only: no grips, actions, selection toolbar or adding.
    expect(screen.queryByRole('button', { name: /^Drag / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove / })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Add instruments' })[0]).toBeDisabled();
    // The member that left later says when.
    expect(screen.getByRole('row', { name: /NSE:YESBANK/ })).toHaveTextContent(/Jun 2025/);

    await userEvent.click(screen.getByRole('button', { name: 'Back to today' }));

    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TINYCO']);
    });
    expect(router.state.location.search).toBe('');
  });

  it('opens at the date given in the URL and says when the basket had nothing then', async () => {
    renderBaskets({ search: '?asOf=2020-01-01' });

    expect(await screen.findByText(/This basket had no instruments at the end of/)).toBeInTheDocument();
    expect(screen.getByLabelText('View as of')).toHaveValue('2020-01-01');
  });
});
