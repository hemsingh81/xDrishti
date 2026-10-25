import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildExperiment, buildHypothesis, mountRecords } from '@/test/learningFixtures';
import { pinClock } from '@/test/pinClock';
import { outcomeWords } from './lib/outcomeText';
import { renderLearning } from './testing';

pinClock('2026-10-25T05:00:00Z');

const table = async () => within(await screen.findByRole('table', { name: 'Experiments' }));

describe('Learning, Experiments tab', () => {
  it('says there is nothing stored, with the verbs', async () => {
    mountRecords();
    renderLearning('?tab=experiments');
    expect(await screen.findByText(/No experiment stored yet; run `xd learn experiment`/)).toBeInTheDocument();
    expect(await screen.findByText(/No hypothesis stored yet; run `xd learn hypothesis`/)).toBeInTheDocument();
  });

  it('words each outcome and gives Better no success fill', async () => {
    mountRecords({
      experiments: [
        buildExperiment({ id: 1, outcome: 'Better' }),
        buildExperiment({ id: 2, outcome: 'NotBetter' }),
        buildExperiment({ id: 3, outcome: 'Inconclusive' }),
        buildExperiment({ id: 4, outcome: 'CheaperAssumption' }),
        buildExperiment({ id: 5, outcome: 'Mystery' }),
      ],
    });
    renderLearning('?tab=experiments');
    const view = await table();
    for (const name of ['Better', 'Not better', 'Inconclusive', 'Cheaper assumption']) {
      expect(view.getByText(name)).toBeInTheDocument();
    }
    expect(view.getByText('Not a gain: a cheaper cost assumption cannot win.')).toBeInTheDocument();
    // An outcome the map lacks is shown as sent, with a note, never hidden.
    expect(view.getByText('Mystery')).toBeInTheDocument();
    expect(view.getByText(/Unknown outcome/)).toBeInTheDocument();
    const better = view.getByText('Better').closest('.MuiChip-root');
    expect(better).not.toHaveClass('MuiChip-colorSuccess');
    expect(better).not.toHaveClass('MuiChip-filled');
  });

  it('knows every outcome the server names', () => {
    expect(Object.keys(outcomeWords).sort()).toEqual(['Better', 'CheaperAssumption', 'Inconclusive', 'NotBetter']);
  });

  it('prints the figures with their n and the server reasons verbatim', async () => {
    const cap = 'only 3 used folds, 6 needed to call it better';
    const noHoldOut = 'no hold-out was kept, no change can be called better';
    mountRecords({
      experiments: [
        buildExperiment({
          usedFolds: 3,
          foldsBetter: 3,
          foldsNeeded: 6,
          outcome: 'Inconclusive',
          holdOutStart: null,
          reasons: [cap, noHoldOut],
          pooledCandidateMeanR: null,
        }),
      ],
    });
    renderLearning('?tab=experiments');
    const view = await table();
    expect(view.getByText('3 of 3 used folds better; 6 needed')).toBeInTheDocument();
    expect(view.getByText('+0.03 R after 4 trials in 30 days')).toBeInTheDocument();
    expect(view.getByText('-0.10 R (n 120)')).toBeInTheDocument();
    expect(view.getByText('Not available')).toBeInTheDocument();
    expect(view.getByText('no hold-out kept')).toBeInTheDocument();
    expect(view.getByText('comparison read in 1.2 s; the replay is not timed')).toBeInTheDocument();
    expect(view.getByText('21 Oct 2026, 10:00 IST by cli')).toBeInTheDocument();
    const reasons = within(view.getByRole('list', { name: 'Reasons for experiment 7' }));
    expect(reasons.getByText(cap)).toBeInTheDocument();
    expect(reasons.getByText(noHoldOut)).toBeInTheDocument();
  });

  it('opens an experiment from the address: report and parameters as text; Back closes', async () => {
    mountRecords({
      experiments: [buildExperiment({ reportText: 'Report <i>raw</i>\nline two', parameters: 'p=<b>1</b>' })],
    });
    const { router } = renderLearning('?tab=experiments');
    await userEvent.click(await screen.findByRole('button', { name: 'Experiment #7' }));
    expect(router.state.location.search).toBe('?tab=experiments&experiment=7');

    const dialog = within(await screen.findByRole('dialog', { name: 'Experiment #7' }));
    const report = await dialog.findByRole('region', { name: 'Experiment report' });
    expect(report.textContent).toBe('Report <i>raw</i>\nline two');
    expect(dialog.getByRole('region', { name: 'Rules used' }).textContent).toBe('p=<b>1</b>');

    await router.navigate(-1);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});

describe('Learning, Hypotheses section', () => {
  it('shows each state with its links, may-move-to text and the server note', async () => {
    mountRecords({
      hypotheses: [
        buildHypothesis(),
        buildHypothesis({ id: 4, state: 'judged', source: 'grid', cellKey: null, experimentId: 7, nextStates: [] }),
      ],
    });
    renderLearning('?tab=experiments');
    const list = within(await screen.findByRole('list', { name: 'Hypotheses, newest first' }));
    expect(list.getByText('State: proposed')).toBeInTheDocument();
    expect(list.getByText('State: judged')).toBeInTheDocument();
    expect(list.getByText('Source: grid')).toBeInTheDocument();
    expect(list.getByText(/approved, rejected\. This screen cannot make the move/)).toBeInTheDocument();
    expect(list.getByRole('link', { name: 'Experiment #7' })).toHaveAttribute(
      'href',
      expect.stringContaining('?tab=experiments&experiment=7'),
    );
    expect(list.getByText('not judged yet')).toBeInTheDocument();
    expect(screen.getAllByText(/Accepting it changes nothing/).length).toBeGreaterThan(0);
  });

  it('filters by state and opens the drawer with its history', async () => {
    mountRecords({ hypotheses: [buildHypothesis(), buildHypothesis({ id: 4, state: 'rejected' })] });
    const { router } = renderLearning('?tab=experiments');
    await screen.findByText('Hypothesis #4');
    await userEvent.click(screen.getByRole('button', { name: 'rejected' }));
    expect(router.state.location.search).toContain('state=rejected');
    await userEvent.click(await screen.findByRole('button', { name: 'Open hypothesis #4' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Hypothesis #4' }));
    expect(await dialog.findByRole('list', { name: 'Steps of this hypothesis' })).toBeInTheDocument();
    expect(dialog.getByText(/May move to: approved, rejected/)).toBeInTheDocument();
  });
});

describe('Learning, markup in experiment and hypothesis text', () => {
  const markup = '<b onmouseover=alert(1)>x</b>';

  it('draws experiment reasons as text', async () => {
    mountRecords({ experiments: [buildExperiment({ reasons: [markup] })] });
    renderLearning('?tab=experiments');
    const reasons = within(await screen.findByRole('list', { name: 'Reasons for experiment 7' }));
    expect(reasons.getByText(markup)).toBeInTheDocument();
  });

  it('draws the hypothesis rationale and the server note as text', async () => {
    mountRecords({ hypotheses: [buildHypothesis({ rationale: markup, note: markup })] });
    renderLearning('?tab=experiments');
    expect(await screen.findAllByText(markup)).toHaveLength(2);
    expect(document.querySelector('b')).toBeNull();
  });

  it('shows a hypothesis in every state', async () => {
    const states = ['proposed', 'approved', 'running', 'judged', 'accepted', 'rejected', 'expired'];
    mountRecords({ hypotheses: states.map((state, index) => buildHypothesis({ id: index + 1, state })) });
    renderLearning('?tab=experiments');
    const list = within(await screen.findByRole('list', { name: 'Hypotheses, newest first' }));
    for (const state of states) expect(list.getByText(`State: ${state}`)).toBeInTheDocument();
  });
});
