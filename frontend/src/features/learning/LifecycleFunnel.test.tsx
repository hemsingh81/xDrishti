import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { buildCell, buildCellState, buildEntry, cellKeyOf, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

const strategies = ['a', 'b', 'c', 'd'] as const;
const keyOf = (strategy: string) =>
  cellKeyOf({ strategy, timeframe: '15m', side: 'BUY', style: 'intraday', exit: 'fixed_r' });
const cells = () => strategies.map((strategy) => buildCell({ strategy }));

/** The number beside a stage of the funnel. */
const countOf = async (stage: string) => {
  const list = await screen.findByRole('list', { name: 'Cells by lifecycle state' });
  const item = within(list)
    .getAllByRole('listitem')
    .find((li) => within(li).queryByText(stage));
  if (!item) throw new Error(`No stage named ${stage}`);
  return item.lastElementChild?.textContent;
};

describe('Cell lifecycle funnel', () => {
  it('puts every cell in research while the changelog has no row for it', async () => {
    mountLearning({ cells: { all: cells() } });
    renderLearning('?tab=setups');

    expect(await countOf('Research')).toBe('4');
    for (const stage of ['Candidate', 'Paper', 'Active', 'Paused', 'Retired']) {
      expect(await countOf(stage)).toBe('0');
    }
    expect(screen.queryByText('State not read')).not.toBeInTheDocument();
    expect(screen.getByText(/Every cell is in research until a changelog row says otherwise/)).toBeInTheDocument();
    expect(screen.getByText(/moves are made with the CLI, not on this screen/)).toBeInTheDocument();
  });

  it('counts a cell where the server replayed its changelog rows', async () => {
    mountLearning({
      cells: { all: cells() },
      changelog: [
        buildEntry({ id: 3, subject: keyOf('b'), fromState: 'candidate', toState: 'paper' }),
        buildEntry({ id: 2, subject: keyOf('a'), fromState: 'candidate', toState: 'paper' }),
        buildEntry({ id: 1, subject: keyOf('a'), fromState: 'research', toState: 'candidate' }),
      ],
      states: {
        [keyOf('a')]: buildCellState({ cell: keyOf('a'), state: 'paper', rows: 2 }),
        [keyOf('b')]: buildCellState({ cell: keyOf('b'), state: 'paused', rows: 3 }),
      },
    });
    renderLearning('?tab=setups');

    expect(await countOf('Research')).toBe('2');
    expect(await countOf('Paper')).toBe('1');
    expect(await countOf('Paused')).toBe('1');
    expect(await countOf('Active')).toBe('0');
    expect(screen.queryByText(/do not add up/)).not.toBeInTheDocument();
  });

  it('counts a cell whose state could not be read apart, never in a stage', async () => {
    mountLearning({
      cells: { all: cells() },
      changelog: [buildEntry({ id: 1, subject: keyOf('a') }), buildEntry({ id: 2, subject: keyOf('b') })],
      states: { [keyOf('b')]: buildCellState({ cell: keyOf('b'), state: 'candidate', rows: 1 }) },
    });
    server.use(
      http.get('/api/changelog/cell-state', ({ request }) =>
        new URL(request.url).searchParams.get('cell') === keyOf('a')
          ? problemResponse({ title: 'The state broke.' }, 500)
          : undefined,
      ),
    );
    renderLearning('?tab=setups');

    expect(await countOf('State not read')).toBe('1');
    expect(await countOf('Research')).toBe('2');
    expect(await countOf('Candidate')).toBe('1');
    expect(screen.getByText(/The state of 1 cell with changelog rows was not read/)).toBeInTheDocument();
  });

  it('reads at most thirty states and says how many were left', async () => {
    const many = Array.from({ length: 32 }, (_, i) => buildCell({ strategy: `s${String(i).padStart(2, '0')}` }));
    mountLearning({
      cells: { all: many },
      changelog: many.map((c, i) => buildEntry({ id: i + 1, subject: c.cellKey })),
    });
    renderLearning('?tab=setups');

    expect(await countOf('State not read')).toBe('2');
    expect(await countOf('Research')).toBe('30');
    expect(screen.getByText(/at most 30 are read/)).toBeInTheDocument();
  });

  it('flags a cell whose rows do not add up, in words', async () => {
    mountLearning({
      cells: { all: cells() },
      changelog: [buildEntry({ id: 1, subject: keyOf('a') })],
      states: {
        [keyOf('a')]: buildCellState({ cell: keyOf('a'), problems: ['Row 1 starts from paper, not research.'] }),
      },
    });
    renderLearning('?tab=setups');

    expect(await screen.findByText(/Warning: the changelog rows of 1 cell\(s\) do not add up/)).toBeInTheDocument();
  });

  it('shows its evidence: the cells counted, the snapshot and the changelog read', async () => {
    mountLearning({ cells: { all: cells() } });
    renderLearning('?tab=setups');

    const evidence = await screen.findByRole('region', { name: 'Lifecycle evidence' });
    expect(evidence).toHaveTextContent('n 4');
    expect(evidence).toHaveTextContent('4 cell rows of the newest snapshot (window all)');
  });

  it('shows the error of a failed changelog read and no counts', async () => {
    mountLearning({ cells: { all: cells() } });
    server.use(http.get('/api/changelog', () => problemResponse({ title: 'The changelog broke.' }, 500)));
    renderLearning('?tab=setups');

    expect((await screen.findAllByText('The changelog broke.')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('list', { name: 'Cells by lifecycle state' })).not.toBeInTheDocument();
  });
});
