import { screen, within } from '@testing-library/react';
import { buildCell, buildEntry, mountLearning } from '@/test/learningFixtures';
import { renderLearning } from './testing';

const timeline = async () => within(await screen.findByRole('list', { name: 'Changelog, newest first' }));

describe('Changelog timeline', () => {
  it('lists the rows as the server sent them, newest first, with every field as text', async () => {
    mountLearning({
      cells: { all: [buildCell()] },
      changelog: [
        buildEntry({
          id: 3,
          kind: 'revert',
          fromState: 'candidate',
          toState: 'research',
          reason: 'Reverted: <script>alert(1)</script>',
          revertsId: 2,
          actor: 'owner',
          at: '2026-10-23T04:30:00Z',
        }),
        buildEntry({ id: 2, paperDays: 20, at: '2026-10-22T04:30:00Z', evidenceIds: ['experiment:4', 'run:9'] }),
        buildEntry({
          id: 1,
          subjectKind: 'model',
          subject: 'model:7',
          fromState: 'challenger',
          toState: 'champion',
          kind: 'designation',
        }),
      ],
    });
    renderLearning('?tab=setups');

    const list = await timeline();
    const items = list.getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent('candidate → research');
    expect(items[0]).toHaveTextContent('(revert, cell)');
    expect(items[0]).toHaveTextContent('23 Oct 2026, 10:00 IST · by owner');
    expect(items[0]).toHaveTextContent('Reverts row 2');
    expect(within(items[0]!).getByText('Reverted: <script>alert(1)</script>')).toBeInTheDocument();
    expect(items[1]).toHaveTextContent('Paper days stated: 20');
    expect(items[1]).toHaveTextContent('Evidence: experiment:4, run:9');
    expect(items[2]).toHaveTextContent('challenger → champion');
    expect(items[2]).toHaveTextContent('model:7');
    expect(
      screen.getByText(/Rows are never changed; a revert is a row that points at the one it reverts/),
    ).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });

  it('says there is nothing recorded yet, in words', async () => {
    mountLearning({ cells: { all: [buildCell()] } });
    renderLearning('?tab=setups');

    expect(await screen.findByText('No records yet')).toBeInTheDocument();
    expect(screen.getByText('No cell move or model designation has been recorded.')).toBeInTheDocument();
  });

  it('shows the newest 25 and counts the rest, and says when the read hit the server limit', async () => {
    const rows = Array.from({ length: 200 }, (_, i) => buildEntry({ id: 200 - i }));
    mountLearning({ cells: { all: [buildCell()] }, changelog: rows });
    renderLearning('?tab=setups');

    const list = await timeline();
    expect(list.getAllByRole('listitem')).toHaveLength(25);
    expect(screen.getByText(/Showing the newest 25 of 200 rows read/)).toBeInTheDocument();
    expect(screen.getByText(/The newest 200 rows were read; there may be more/)).toBeInTheDocument();
  });
});
