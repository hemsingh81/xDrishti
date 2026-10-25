import { screen, waitFor, within, type BoundFunctions, type queries } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildCell, buildCellState, buildEntry, buildThinCell, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

const key = 'orb|15m|BUY|intraday|fixed_r|*';
const dialog = async () => within(await screen.findByRole('dialog', { name: /orb · 15m/ }));
const rowOf = (view: BoundFunctions<typeof queries>, name: string) => {
  const row = view.getByRole('rowheader', { name }).closest('tr');
  if (!row) throw new Error(`No row ${name}`);
  return within(row);
};

describe('Cell drawer', () => {
  it('opens from a tile, shows the windows with a thin one as counts only, and closes with Escape', async () => {
    mountLearning({
      cells: {
        all: [buildCell()],
        '250d': [buildCell({ window: '250d', n: 70, wins: 41, expectancyR: 0.22 })],
        '60d': [buildThinCell({ window: '60d' })],
      },
      changelog: [buildEntry({ id: 4, subject: key, reason: 'Passed the gates <b>twice</b>.' })],
      states: { [key]: buildCellState({ state: 'candidate', rows: 1, legalNext: ['paper', 'retired'] }) },
    });
    const { router } = renderLearning('?tab=setups');

    await userEvent.click(await screen.findByRole('button', { name: /orb, 15m, BUY.*enough evidence/ }));
    expect(router.state.location.search).toBe(`?tab=setups&cell=${encodeURIComponent(key)}`);

    const view = await dialog();
    const table = await view.findByRole('table', { name: 'Cell statistics by window' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((c) => c.textContent),
    ).toEqual(['Statistic', 'All', 'Last 250 days', 'Last 60 days']);
    const stats = within(table);
    expect(
      rowOf(stats, 'Evidence')
        .getAllByRole('cell')
        .map((c) => c.textContent),
    ).toEqual(['Enough', 'Enough', 'Not enough']);
    // Counts are facts in every window; the derived figures of the thin window are words.
    expect(
      rowOf(stats, 'Trades (n)')
        .getAllByRole('cell')
        .map((c) => c.textContent),
    ).toEqual(['142', '70', '12']);
    const expectancy = rowOf(stats, 'Expectancy').getAllByRole('cell');
    expect(expectancy[0]).toHaveTextContent('+0.31 R');
    expect(expectancy[1]).toHaveTextContent('+0.22 R');
    expect(expectancy[2]).toHaveTextContent('Insufficient data');
    expect(expectancy[2]).toHaveTextContent('n 12 of at least 30');
    expect(expectancy[2]).not.toHaveTextContent(/0\.91/);
    expect(rowOf(stats, 'Win-rate lower bound').getAllByRole('cell')[2]).toHaveTextContent('Insufficient data');
    expect(rowOf(stats, 'Profit factor').getAllByRole('cell')[2]).not.toHaveTextContent(/4\.40/);

    expect(view.getByText(/Minimum 30 trades for a statistic. Credible level 95\.0%/)).toBeInTheDocument();
    expect(view.getByText(/no interval on expectancy is stored/)).toBeInTheDocument();
    const evidence = view.getByRole('region', { name: 'Cell evidence' });
    expect(evidence).toHaveTextContent('n 142');
    expect(evidence).toHaveTextContent('Gross R (before costs)');

    // Where the cell is: a record, the moves the rules allow, and where moves are made.
    expect(view.getByText('candidate')).toBeInTheDocument();
    expect(view.getByText(/The lifecycle allows a move to: paper or retired/)).toBeInTheDocument();
    expect(view.getByText('xd learn lifecycle')).toBeInTheDocument();
    expect(view.queryByRole('button', { name: /paper|retired|move|promote/i })).not.toBeInTheDocument();

    // Its own changelog, with the server's text kept as text.
    expect(await view.findByText('Passed the gates <b>twice</b>.')).toBeInTheDocument();
    expect(view.getByRole('list', { name: 'Changelog of this cell' }).querySelector('b')).toBeNull();

    await userEvent.keyboard('{Escape}');
    await waitFor(() => {
      expect(router.state.location.search).toBe('?tab=setups');
    });
  });

  it('opens from an address and closes with the close button', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    const { router } = renderLearning(`?tab=setups&cell=${encodeURIComponent(key)}`);

    const view = await dialog();
    expect(await view.findByText(/The lifecycle allows a move to: candidate or retired/)).toBeInTheDocument();
    expect(view.getByText('No cell move or model designation has been recorded.')).toBeInTheDocument();

    await userEvent.click(view.getByRole('button', { name: 'Close cell' }));
    await waitFor(() => {
      expect(router.state.location.search).toBe('?tab=setups');
    });
  });

  it('says so when the cell is not in the newest snapshot', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    renderLearning(`?tab=setups&cell=${encodeURIComponent('gone|15m|BUY|intraday|fixed_r|*')}`);

    const view = within(await screen.findByRole('dialog', { name: /gone · 15m/ }));
    expect(await view.findByText('The cell is not in the newest snapshot.')).toBeInTheDocument();
  });

  it('shows the problems of a cell whose rows do not add up', async () => {
    mountLearning({
      cells: { all: [buildCell()] },
      states: { [key]: buildCellState({ problems: ['Row 2 starts from active, not paper.'] }) },
    });
    renderLearning(`?tab=setups&cell=${encodeURIComponent(key)}`);

    const view = await dialog();
    expect(await view.findByText('Row 2 starts from active, not paper.')).toBeInTheDocument();
    expect(view.getByText(/do not add up/)).toBeInTheDocument();
  });

  it('shows the errors of the state and of the changelog, each in its own section', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    server.use(
      http.get('/api/changelog/cell-state', () => problemResponse({ title: 'The state broke.' }, 500)),
      http.get('/api/changelog', () => problemResponse({ title: 'The changelog broke.' }, 500)),
    );
    renderLearning(`?tab=setups&cell=${encodeURIComponent(key)}`);

    const view = await dialog();
    expect(await view.findByText('The state broke.')).toBeInTheDocument();
    expect(await view.findByText('The changelog broke.')).toBeInTheDocument();
    expect(await view.findByRole('table', { name: 'Cell statistics by window' })).toBeInTheDocument();
  });

  it('opens Setups for a link with a cell and no tab', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    renderLearning(`?cell=${encodeURIComponent(key)}`);

    await dialog();
    // The page behind a modal is hidden from the accessibility tree, so the tabs are looked up with `hidden`.
    expect(screen.getByRole('tab', { name: 'Setups', selected: true, hidden: true })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Overview', selected: false, hidden: true })).toBeInTheDocument();
  });

  it('returns focus to the tile that opened the cell when Escape closes it', async () => {
    mountLearning({ cells: { all: [buildCell(), buildCell({ timeframe: '5m' })] } });
    renderLearning('?tab=setups');

    const tile = await screen.findByRole('button', { name: /orb, 15m, BUY.*enough evidence/ });
    await userEvent.click(tile);
    await dialog();
    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /orb, 15m, BUY.*enough evidence/ })).toHaveFocus();
  });

  it('leaving the Setups tab closes the cell', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    const { router } = renderLearning(`?tab=setups&cell=${encodeURIComponent(key)}`);
    await dialog();

    await router.navigate('/learning');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
