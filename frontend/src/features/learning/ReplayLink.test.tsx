import { screen } from '@testing-library/react';
import { mountExperiments } from '@/test/experimentsFake';
import { mountRecords } from '@/test/learningFixtures';
import { renderLearning } from './testing';

describe('Learning, Experiments tab, the replay link', () => {
  it('draws a plain link to the replay screen only when the switch is on', async () => {
    mountRecords();
    mountExperiments({ on: true });
    renderLearning('?tab=experiments');

    const link = await screen.findByRole('link', { name: 'Replay one change' });
    expect(link).toHaveAttribute('href', '/owner-acts/experiment');
    expect(screen.queryByRole('button', { name: /replay/i })).toBeNull();
  });

  it('draws nothing when the switch is off', async () => {
    mountRecords();
    mountExperiments({ on: false });
    renderLearning('?tab=experiments');

    await screen.findByText(/No experiment stored yet/);
    expect(screen.queryByRole('link', { name: 'Replay one change' })).toBeNull();
  });
});
