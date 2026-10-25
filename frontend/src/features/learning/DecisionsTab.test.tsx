import { screen, within } from '@testing-library/react';
import { buildDecisions, mountLearning } from '@/test/learningFixtures';
import { renderLearning } from './testing';

const stats = (name: string, over: Record<string, unknown> = {}) => ({
  name,
  tickets: 40,
  triggered: 35,
  notTriggered: 5,
  notGradable: 0,
  totalR: 12,
  meanR: 0.34,
  winRate: 0.55,
  note: null,
  ...over,
});

describe('Decisions', () => {
  it('prints the basis and an empty table without a digit when nothing is graded', async () => {
    mountLearning({ decisions: buildDecisions() });
    renderLearning('?tab=decisions');

    expect(await screen.findByText(/Basis: simulated on plan prices/)).toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'Groups' })).getByText('No records yet')).toBeInTheDocument();
    expect(screen.queryByText(/significant/i)).not.toBeInTheDocument();
  });

  it('shows a row with no triggered ticket as empty and a thin row as provisional with the server note', async () => {
    mountLearning({
      decisions: buildDecisions({
        plans: 3,
        groups: [
          stats('primary'),
          stats('reserve', { triggered: 0, meanR: null, winRate: null }),
          stats('rejected', {
            triggered: 3,
            meanR: 0.9,
            note: 'indicative only: 3 triggered ticket(s), fewer than 30',
          }),
        ],
      }),
    });
    renderLearning('?tab=decisions');

    const table = await screen.findByRole('table', { name: 'Groups' });
    const row = (name: string) => within(table).getByRole('rowheader', { name }).closest('tr') as HTMLElement;
    expect(row('primary')).toHaveTextContent('+0.34 R');
    expect(row('reserve')).toHaveTextContent('No records yet');
    expect(row('reserve')).not.toHaveTextContent('0.00');
    expect(row('rejected')).toHaveTextContent('Provisional');
    expect(row('rejected')).toHaveTextContent('indicative only: 3 triggered ticket(s), fewer than 30');
  });

  it('words the comparisons, shows a null difference as not evaluable and prints the server notes verbatim', async () => {
    mountLearning({
      decisions: buildDecisions({
        comparisons: [
          {
            question: 'Primary or reserve?',
            left: 'primary',
            right: 'reserve',
            meanDifference: 0.2,
            verdict: 'first_better',
            note: 'ok',
          },
          {
            question: 'Owner or rule?',
            left: 'approved',
            right: 'rejected',
            meanDifference: null,
            verdict: 'not_enough_data',
            note: 'few',
          },
        ],
        notes: ['TRUNCATED: more than 5000 tickets matched; the oldest are left out.'],
      }),
    });
    renderLearning('?tab=decisions');

    const list = await screen.findByRole('list', { name: 'Comparisons' });
    expect(list).toHaveTextContent('first better');
    expect(list).toHaveTextContent('not enough data');
    expect(within(list).getByText('Not evaluable')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Notes' })).toHaveTextContent('TRUNCATED: more than 5000');
  });
});
