import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildInstrument, sampleInstruments, seededBaskets, trackedInfo } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';

async function openAdd() {
  await userEvent.click(screen.getAllByRole('button', { name: 'Add instruments' })[0]!);
  const dialog = await screen.findByRole('dialog', { name: 'Add instruments to MyChoice' });
  await within(dialog).findByRole('list', { name: 'Instruments to add' });
  return within(dialog);
}

const box = (dialog: Pick<typeof screen, 'getByRole'>, symbol: string) =>
  dialog.getByRole('checkbox', { name: new RegExp(symbol) });

describe('Add instruments dialog', () => {
  it('lists the master list, marking members and retired instruments as not selectable', async () => {
    renderBaskets();
    await membersGrid();

    const dialog = await openAdd();

    expect(box(dialog, 'NSE:TCS')).toBeChecked();
    expect(box(dialog, 'NSE:TCS')).toBeDisabled();
    expect(dialog.getAllByText('Already in MyChoice')).toHaveLength(3);
    expect(box(dialog, 'NSE:OLDCO')).toBeDisabled();
    expect(box(dialog, 'NSE:OLDCO')).toHaveAccessibleName(/Retired — cannot be added/);
    expect(box(dialog, 'NSE:YESBANK')).toBeEnabled();
    expect(box(dialog, 'NSE:YESBANK')).toHaveAccessibleName(/Will be tracked/);
    expect(dialog.getByRole('button', { name: 'Add' })).toBeDisabled();
  });

  it('searches, picks across searches and adds them, tracking the new ones', async () => {
    const { fake, tracking } = renderBaskets();
    await membersGrid();
    const dialog = await openAdd();

    await userEvent.type(dialog.getByRole('searchbox', { name: 'Search instruments' }), 'yes');
    await waitFor(() => {
      expect(dialog.queryByRole('checkbox', { name: /NSE:TINYCO/ })).not.toBeInTheDocument();
    });
    await userEvent.click(box(dialog, 'NSE:YESBANK'));
    await userEvent.click(dialog.getByRole('button', { name: 'Clear search' }));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /NSE:TINYCO/ }));
    expect(dialog.getByText('2 instruments selected')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Add 2 to MyChoice' }));

    expect(
      await screen.findByText('Added 2 instruments to MyChoice (2 were not tracked and now are)'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:YESBANK', 'NSE:TINYCO']);
    });
    expect(fake.symbolsIn(basketIds.myChoice)).toHaveLength(5);
    expect(
      tracking
        .items()
        .filter((i) => i.tracked.active)
        .map((i) => i.symbol),
    ).toEqual(['NSE:YESBANK', 'NSE:TINYCO']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('selects everything shown and clears it again', async () => {
    renderBaskets();
    await membersGrid();
    const dialog = await openAdd();

    await userEvent.click(dialog.getByRole('button', { name: 'Select all shown' }));
    expect(dialog.getByRole('button', { name: /^Add \d+ to MyChoice$/ })).toBeEnabled();
    expect(box(dialog, 'NSE:NIFTYBEES')).toBeChecked();
    expect(box(dialog, 'NSE:OLDCO')).not.toBeChecked();

    await userEvent.click(dialog.getByRole('button', { name: 'Clear shown' }));
    expect(dialog.getByText('Nothing selected')).toBeInTheDocument();
  });

  it('can search only the tracked instruments', async () => {
    const instruments = sampleInstruments.map((i) =>
      i.symbol === 'NSE:YESBANK' ? { ...i, tracked: trackedInfo() } : i,
    );
    renderBaskets({ instruments });
    await membersGrid();
    const dialog = await openAdd();

    await userEvent.click(dialog.getByRole('button', { name: 'Tracked' }));

    await waitFor(() => {
      expect(dialog.getAllByRole('checkbox')).toHaveLength(1);
    });
    expect(box(dialog, 'NSE:YESBANK')).toHaveAccessibleName(/Tracked/);
  });

  it('shows the reason and keeps the picks when the server refuses', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    const dialog = await openAdd();
    fake.failNext('add', 400, 'baskets.too_many_members', 'A basket holds at most 2,000 instruments.');

    await userEvent.click(box(dialog, 'NSE:YESBANK'));
    await userEvent.click(dialog.getByRole('button', { name: 'Add 1 to MyChoice' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent('A basket holds at most 2,000 instruments.');
    expect(box(dialog, 'NSE:YESBANK')).toBeChecked();
  });

  it('warns and turns Add off when the picks would not fit in the basket, and on again after unticking', async () => {
    const { fake, client } = renderBaskets();
    await membersGrid();
    // The list says the basket is nearly full (the members themselves are not needed for this).
    server.use(
      http.get('/api/baskets', () =>
        Response.json(seededBaskets().map((b) => (b.id === basketIds.myChoice ? { ...b, memberCount: 1_998 } : b))),
      ),
    );
    await client.invalidateQueries({ queryKey: ['baskets', 'list'] });
    const dialog = await openAdd();

    await userEvent.click(box(dialog, 'NSE:YESBANK'));
    await userEvent.click(box(dialog, 'NSE:TINYCO'));
    await userEvent.click(box(dialog, 'NSE:NIFTYBEES'));

    expect(dialog.getByRole('alert')).toHaveTextContent(
      'MyChoice holds 1,998 and a basket holds at most 2,000 instruments, so 3 more do not fit. Select 1 fewer.',
    );
    expect(dialog.getByRole('button', { name: 'Add 3 to MyChoice' })).toBeDisabled();

    await userEvent.click(box(dialog, 'NSE:NIFTYBEES'));

    expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add 2 to MyChoice' })).toBeEnabled();
    expect(fake.calls.add).toEqual([]);
  });

  it('says when nothing matches, and offers Retry when the search fails', async () => {
    renderBaskets();
    await membersGrid();
    const dialog = await openAdd();

    await userEvent.type(dialog.getByRole('searchbox', { name: 'Search instruments' }), 'zzzz');
    expect(await dialog.findByText('No instruments match.')).toBeInTheDocument();

    server.use(http.get('/api/instruments', () => problemResponse({ title: 'Search is down.', code: 'x' }, 500)));
    const search = dialog.getByRole('searchbox', { name: 'Search instruments' });
    await userEvent.clear(search);
    await userEvent.type(search, 'tc');
    expect(await dialog.findByText('Search is down.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('adds a single instrument by its symbol in the message', async () => {
    renderBaskets({
      instruments: [
        ...sampleInstruments,
        buildInstrument({
          id: '00000000-0000-4000-8000-000000000298',
          symbol: 'NSE:ZETA',
          displayName: 'Zeta',
          tracked: trackedInfo(),
        }),
      ],
    });
    await membersGrid();
    const dialog = await openAdd();

    await userEvent.click(await dialog.findByRole('checkbox', { name: /NSE:ZETA/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add 1 to MyChoice' }));

    expect(await screen.findByText('Added NSE:ZETA to MyChoice')).toBeInTheDocument();
  });
});
