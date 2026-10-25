import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildExperiment, buildLesson, lessonNote, mountRecords } from '@/test/learningFixtures';
import { pinClock } from '@/test/pinClock';
import { renderLearning } from './testing';

pinClock('2026-10-25T05:00:00Z');

describe('Learning, Lessons tab', () => {
  it('says there is no lesson yet, with the verb', async () => {
    mountRecords();
    renderLearning('?tab=lessons');
    expect(await screen.findByText(/No lesson stored yet; run `xd learn lesson`/)).toBeInTheDocument();
  });

  it('shows an open lesson with no fix tried, its note, and the times in India time', async () => {
    mountRecords({ lessons: [buildLesson({ problem: 'Drift <b>seen</b>' })] });
    renderLearning('?tab=lessons');

    const list = within(await screen.findByRole('list', { name: 'Lessons, newest first' }));
    expect(list.getByText('Rolling band')).toBeInTheDocument();
    expect(list.getByText('State: open')).toBeInTheDocument();
    // Server text is text, not markup.
    expect(list.getByText('Drift <b>seen</b>')).toBeInTheDocument();
    expect(list.getByText('No fix tried yet')).toBeInTheDocument();
    expect(list.getByText('Not closed')).toBeInTheDocument();
    expect(list.getByText('20 Oct 2026, 10:00 IST')).toBeInTheDocument();
    expect(screen.getByText(lessonNote)).toBeInTheDocument();
    expect(list.getByRole('link', { name: /orb\|15m/ })).toHaveAttribute(
      'href',
      expect.stringContaining('tab=setups&cell='),
    );
  });

  it('filters by state through the address', async () => {
    mountRecords({ lessons: [buildLesson(), buildLesson({ id: 2, state: 'closed', result: 'improved' })] });
    const { router } = renderLearning('?tab=lessons');
    await screen.findByText('Lesson #2');

    await userEvent.click(screen.getByRole('button', { name: 'closed' }));
    expect(router.state.location.search).toContain('state=closed');
    expect(await screen.findByText('Lesson #2')).toBeInTheDocument();
    expect(screen.queryByText('Lesson #1')).not.toBeInTheDocument();
  });

  it('opens a closed lesson: before and after with their n, from the experiment; Back closes it', async () => {
    mountRecords({
      lessons: [
        buildLesson({
          id: 4,
          state: 'closed',
          hypothesisRef: 3,
          experimentId: 7,
          result: 'improved',
          experimentOutcome: 'NotBetter',
          beforeMeanR: -0.1,
          afterMeanR: 0.05,
          beforeTotalR: -12,
          afterTotalR: 5.9,
        }),
      ],
      experiments: [buildExperiment()],
    });
    const { router } = renderLearning('?tab=lessons');
    await userEvent.click(await screen.findByRole('button', { name: 'Open lesson #4' }));
    expect(router.state.location.search).toBe('?tab=lessons&lesson=4');

    const dialog = within(await screen.findByRole('dialog', { name: 'Lesson #4' }));
    expect(await dialog.findByText('-0.10 R (n 120)')).toBeInTheDocument();
    expect(dialog.getByText('+0.05 R (n 118)')).toBeInTheDocument();
    expect(dialog.getByText('-12.00 R')).toBeInTheDocument();
    expect(dialog.getByText('Not better')).toBeInTheDocument();
    expect(dialog.getByText(/May move to: linked, dismissed/)).toBeInTheDocument();
    expect(dialog.getByText(/cannot make the move/)).toBeInTheDocument();
    expect(dialog.getByRole('list', { name: 'Steps of this lesson' })).toBeInTheDocument();
    // Read only: the only button is the close button.
    expect(dialog.getAllByRole('button')).toHaveLength(1);

    await router.navigate(-1);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('shows no change as an ordinary result: neutral, with no warning', async () => {
    mountRecords({
      lessons: [buildLesson({ id: 5, state: 'closed', result: 'no_measurable_change', experimentId: 7 })],
      experiments: [buildExperiment()],
    });
    renderLearning('?tab=lessons&lesson=5');
    const dialog = within(await screen.findByRole('dialog', { name: 'Lesson #5' }));
    expect(await dialog.findByText('No measurable change')).toBeInTheDocument();
    expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows unavailable, not a crash, when the linked experiment cannot be read', async () => {
    mountRecords({
      lessons: [buildLesson({ id: 6, state: 'closed', result: 'improved', experimentId: 99, beforeMeanR: -0.1 })],
    });
    renderLearning('?tab=lessons&lesson=6');
    const dialog = within(await screen.findByRole('dialog', { name: 'Lesson #6' }));
    expect(await dialog.findByText('Not available')).toBeInTheDocument();
    expect(dialog.getByText(/Experiment #99 could not be read/)).toBeInTheDocument();
    expect(dialog.queryByText(/-0\.10/)).not.toBeInTheDocument();
  });

  it('shows no before and after block for a lesson without a result', async () => {
    mountRecords({ lessons: [buildLesson({ id: 8 })] });
    renderLearning('?tab=lessons&lesson=8');
    const dialog = within(await screen.findByRole('dialog', { name: 'Lesson #8' }));
    expect(await dialog.findByText('Not closed')).toBeInTheDocument();
    expect(dialog.queryByText(/Pooled mean R/)).not.toBeInTheDocument();
  });
});
