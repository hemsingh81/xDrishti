import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildCell, buildThinCell, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

const heatmap = async () => within(await screen.findByRole('grid', { name: 'Setups by strategy and timeframe' }));

describe('Learning, Setups tab', () => {
  it('says there is no snapshot yet, and shows no heatmap and no row of zeros', async () => {
    mountLearning();
    renderLearning('?tab=setups');

    expect(await screen.findByRole('heading', { name: 'Setups by timeframe' })).toBeInTheDocument();
    expect((await screen.findAllByText(/No snapshot yet; run `xd learn cells`/)).length).toBe(2);
    expect(screen.queryByRole('grid', { name: 'Setups by strategy and timeframe' })).not.toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Cells by lifecycle state' })).not.toBeInTheDocument();
    expect(screen.getByText('No cell move or model designation has been recorded.')).toBeInTheDocument();
  });

  it('never lets a thin cell read as a winner: hatched in words, no expectancy and no bound', async () => {
    mountLearning({
      cells: {
        all: [
          buildThinCell(),
          buildThinCell({ timeframe: '5m' }),
          buildThinCell({ strategy: 'vwap', timeframe: '5m', n: 29 }),
        ],
      },
    });
    renderLearning('?tab=setups');

    const table = await heatmap();
    expect(table.getAllByText('Not enough')).toHaveLength(3);
    expect(table.queryByText('Enough')).not.toBeInTheDocument();
    expect(table.getByText('n 29 of at least 30')).toBeInTheDocument();
    // The server stored expectancy 0.91 and a lower bound of 33.7 % for these cells; neither is printed anywhere on the page.
    const page = document.body.textContent;
    expect(page).not.toMatch(/0\.91/);
    expect(page).not.toMatch(/33\.7/);
    expect(table.getAllByText('Insufficient data')).toHaveLength(3);
    expect(
      table.getByRole('button', {
        name: /orb, 15m, BUY, intraday, fixed_r: insufficient data, n 12 of at least 30, not enough evidence/,
      }),
    ).toBeInTheDocument();
    // The legend says what the hatching means.
    expect(screen.getByText(/A hatched tile is labelled Not enough and prints no expectancy/)).toBeInTheDocument();
  });

  it('shows enough cells with a signed expectancy, thin ones apart, and a missing one as a word', async () => {
    mountLearning({
      cells: {
        all: [
          buildCell(),
          buildCell({ timeframe: '5m', expectancyR: -0.2, winRateLower: 0.41, n: 60 }),
          buildCell({ timeframe: '1h', expectancyR: null, n: 45 }),
          buildThinCell({ strategy: 'vwap', timeframe: '5m' }),
          buildCell({ strategy: 'trend', timeframe: '1D', side: 'SELL', exit: 'trail', expectancyR: 0.12 }),
        ],
      },
    });
    renderLearning('?tab=setups');

    const table = await heatmap();
    // Columns run from the shortest to the longest timeframe.
    expect(table.getAllByRole('columnheader').map((c) => c.textContent)).toEqual(['Strategy', '5m', '15m', '1h', '1D']);
    expect(
      table.getByRole('button', {
        name: /orb, 15m, BUY, intraday, fixed_r: expectancy \+0\.31 R, n 142, win-rate lower bound 48\.2%, enough evidence/,
      }),
    ).toBeInTheDocument();
    expect(
      table.getByRole('button', { name: /orb, 5m.*expectancy -0\.20 R, n 60.*enough evidence/ }),
    ).toBeInTheDocument();
    expect(table.getByText('+0.31 R')).toBeInTheDocument();
    expect(table.getByText('-0.20 R')).toBeInTheDocument();
    expect(
      table.getByRole('button', { name: /orb, 1h.*expectancy not available.*enough evidence/ }),
    ).toBeInTheDocument();
    expect(table.getByText('Not available')).toBeInTheDocument();
    expect(table.getByText('No expectancy is stored for this cell')).toBeInTheDocument();
    expect(table.getByText('Not enough')).toBeInTheDocument();
    // The row header carries the rest of the key; a pair with no cell says so.
    expect(table.getByRole('rowheader', { name: /orb BUY · intraday · fixed_r/ })).toBeInTheDocument();
    expect(table.getByRole('rowheader', { name: /trend SELL · intraday · trail/ })).toBeInTheDocument();
    expect(table.getAllByText('No cell').length).toBeGreaterThan(0);
    // The evidence under the heatmap: trades summed, basis from the rows, the snapshot, the limits and the missing range.
    const evidence = screen.getByRole('region', { name: 'Setups evidence' });
    expect(evidence).toHaveTextContent('n 401');
    expect(evidence).toHaveTextContent('range not stated');
    expect(evidence).toHaveTextContent('Gross R (before costs)');
    expect(evidence).toHaveTextContent('snapshot cells-2026-10-20, as of 20 Oct 2026');
    expect(evidence).toHaveTextContent('a description of the past, not a forecast');
  });

  it('keeps cells with the same strategy and timeframe apart instead of merging them', async () => {
    mountLearning({ cells: { all: [buildCell(), buildCell({ side: 'SELL', expectancyR: -0.4 })] } });
    renderLearning('?tab=setups');

    const table = await heatmap();
    expect(table.getAllByRole('rowheader')).toHaveLength(2);
    expect(table.getByText('+0.31 R')).toBeInTheDocument();
    expect(table.getByText('-0.40 R')).toBeInTheDocument();
  });

  it('asks the server for the chosen window and writes it to the address', async () => {
    const fake = mountLearning({
      cells: { all: [buildCell()], '250d': [buildCell({ window: '250d', n: 20, enough: false })] },
    });
    const { router } = renderLearning('?tab=setups');
    await heatmap();
    expect(screen.getByRole('button', { name: 'All', pressed: true })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Last 250 days' }));

    expect(router.state.location.search).toBe('?tab=setups&window=250d');
    expect(await screen.findByText('n 20 of at least 30')).toBeInTheDocument();
    expect(fake.asked).toContain('250d');
    expect(screen.getByRole('button', { name: 'Last 250 days', pressed: true })).toBeInTheDocument();

    // Choosing the default window again drops it from the address.
    await userEvent.click(screen.getByRole('button', { name: 'All' }));
    expect(router.state.location.search).toBe('?tab=setups');
  });

  it('reads the window from the address', async () => {
    const fake = mountLearning({
      cells: { all: [buildCell()], '60d': [buildCell({ window: '60d', n: 9, enough: false })] },
    });
    renderLearning('?tab=setups&window=60d');
    expect(await screen.findByText('n 9 of at least 30')).toBeInTheDocument();
    // The lifecycle funnel always counts the cells of the all window.
    expect(fake.asked).toContain('60d');
  });

  it('uses the all window for an unknown one', async () => {
    const fake = mountLearning({ cells: { all: [buildCell()] } });
    renderLearning('?tab=setups&window=forever');
    await heatmap();
    expect(new Set(fake.asked)).toEqual(new Set(['all']));
  });

  it('keeps the tab in the address and Back returns to the previous tab', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    const { router } = renderLearning();
    expect(await screen.findByRole('tab', { name: 'Overview', selected: true })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Setups' }));
    expect(router.state.location.search).toBe('?tab=setups');
    await heatmap();

    await router.navigate(-1);
    expect(await screen.findByRole('tab', { name: 'Overview', selected: true })).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
  });

  it('moves between the tabs with the arrow keys', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    const { router } = renderLearning();
    const overview = await screen.findByRole('tab', { name: 'Overview' });
    overview.focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Setups', selected: true })).toHaveFocus();
    expect(router.state.location.search).toBe('?tab=setups');
  });

  it('shows the error of the cells request and keeps the rest of the page', async () => {
    mountLearning();
    server.use(http.get('/api/cells', () => problemResponse({ title: 'The cells broke.' }, 500)));
    renderLearning('?tab=setups');

    expect(await screen.findAllByText('The cells broke.')).not.toHaveLength(0);
    expect(screen.getByRole('heading', { name: 'Changelog' })).toBeInTheDocument();
  });

  it('shows a loading state while the cells are read', async () => {
    mountLearning();
    server.use(http.get('/api/cells', () => new Promise<never>(() => undefined)));
    renderLearning('?tab=setups');

    expect(await screen.findByText('Loading the cells…')).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Setups by strategy and timeframe' })).not.toBeInTheDocument();
  });

  it('has no control that writes: only tabs, window choices and tiles that open a cell', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    renderLearning('?tab=setups');
    await heatmap();
    const names = screen.getAllByRole('button').map((b) => b.getAttribute('aria-label') ?? b.textContent);
    expect(names.join('|')).not.toMatch(/run|apply|approve|revert|retrain|delete|save|start/i);
  });
});
