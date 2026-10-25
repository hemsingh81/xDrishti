import { screen, within } from '@testing-library/react';
import { buildAutonomy, buildLearningCheck, buildPlanCheck, mountLearning } from '@/test/learningFixtures';
import { renderLearning } from './testing';

describe('Autonomy', () => {
  it('shows Manual approval when OFF, the fixed no-order sentence, where it changes, and no control', async () => {
    mountLearning({ autonomy: buildAutonomy() });
    renderLearning('?tab=autonomy');

    expect((await screen.findAllByText(/Manual approval/)).length).toBeGreaterThan(0);
    expect(
      screen.getByText('No setting lets anything place an order; there is no order in this system yet.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/P8-19 decides/)).toHaveTextContent('A change needs a fresh password');
    const panel = screen.getByRole('tabpanel');
    expect(within(panel).queryAllByRole('switch')).toHaveLength(0);
    expect(within(panel).queryAllByRole('checkbox')).toHaveLength(0);
    expect(within(panel).queryAllByRole('button')).toHaveLength(0);
  });

  it('shows ON and the history of two changes', async () => {
    mountLearning({
      autonomy: buildAutonomy({
        id: 2,
        hash: 'abc123',
        autoApproveTickets: true,
        history: [
          {
            id: 2,
            hash: 'abc123',
            autoApproveTickets: true,
            promoteFromReserve: false,
            minPWin: 0.6,
            maxAutoPerDay: 3,
            actor: 'owner:me',
            reason: 'try it',
            changedAt: '2026-10-22T05:00:00Z',
          },
          {
            id: 1,
            hash: 'def456',
            autoApproveTickets: false,
            promoteFromReserve: false,
            minPWin: 0.6,
            maxAutoPerDay: 3,
            actor: 'owner:me',
            reason: 'start',
            changedAt: '2026-10-20T05:00:00Z',
          },
        ],
      }),
    });
    renderLearning('?tab=autonomy');

    expect(await screen.findByText('ON', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByText(/abc123/)).toBeInTheDocument();
    const table = screen.getByRole('table', { name: 'Autonomy change history' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(table).toHaveTextContent('try it');
  });
});

describe('Phase checks', () => {
  it('shows the P7 exit call with its caveat and a P6 list of NOT EVALUABLE criteria with limits', async () => {
    mountLearning({
      planCheck: buildPlanCheck({
        exitVerdict: 'MET',
        exitCriterion: 'MET (COUNT ONLY: ten sessions counted, nothing else judged)',
        criteria: [{ number: 1, name: 'Ten sessions', verdict: 'MET', detail: '10 of 10', caveat: 'count only' }],
      }),
      learningCheck: buildLearningCheck(),
    });
    renderLearning('?tab=checks');

    expect(await screen.findByText(/MET \(COUNT ONLY/)).toBeInTheDocument();
    const p7 = screen.getByRole('list', { name: 'P7 criteria' });
    expect(p7).toHaveTextContent('Caveat: count only');
    const p6 = await screen.findByRole('list', { name: 'P6 criteria' });
    expect(within(p6).getAllByText('NOT EVALUABLE')).toHaveLength(2);
    expect(screen.getByRole('list', { name: 'P6 limits' })).toHaveTextContent('stored records only');
    expect(screen.getByRole('list', { name: 'P7 limits' })).toBeInTheDocument();
    expect(screen.getByText(/Read in 12 ms/)).toBeInTheDocument();
  });

  it('renders server text as text', async () => {
    mountLearning({
      learningCheck: buildLearningCheck({
        criteria: [{ number: 1, name: 'x', verdict: 'NOT MET', detail: '<b>bold</b> detail', caveat: null }],
      }),
    });
    renderLearning('?tab=checks');

    expect(await screen.findByText(/<b>bold<\/b> detail/)).toBeInTheDocument();
    expect(document.querySelector('b')).toBeNull();
  });
});
