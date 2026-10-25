import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildDigest, buildModel, buildModelDetail, mountRecords } from '@/test/learningFixtures';
import { pinClock } from '@/test/pinClock';
import { renderLearning } from './testing';

pinClock('2026-10-25T05:00:00Z');

describe('Learning, Models tab', () => {
  it('says no model is stored and what to expect on the first view', async () => {
    mountRecords();
    renderLearning('?tab=models');
    expect(await screen.findByText(/No model stored yet; run `xd learn model`/)).toBeInTheDocument();
    expect(screen.getByText(/most checks will be NOT EVALUABLE/)).toBeInTheDocument();
  });

  it('shows an eligible challenger and a champion that cannot beat the baselines, with the reasons', async () => {
    mountRecords({
      models: [
        buildModel(),
        buildModel({
          id: 1,
          role: 'champion',
          verdict: 'CannotBeatBaselines',
          mayBeChampion: false,
          holdOutStart: null,
          reasons: ['beat both baselines in 2 of 6 folds, 5 needed'],
        }),
      ],
    });
    renderLearning('?tab=models');
    const list = within(await screen.findByRole('list', { name: 'Models, newest first' }));
    expect(list.getByText('Role: challenger')).toBeInTheDocument();
    expect(list.getByText('Role: champion')).toBeInTheDocument();
    expect(list.getByText('Eligible')).toBeInTheDocument();
    expect(list.getByText('Cannot beat baselines')).toBeInTheDocument();
    expect(list.getByText('no hold-out kept')).toBeInTheDocument();
    expect(list.getAllByText('2.50 percentage points')).toHaveLength(2);
    expect(list.getByText('yes')).toBeInTheDocument();
    expect(list.getByText('no')).toBeInTheDocument();
    expect(list.getByText('beat both baselines in 2 of 6 folds, 5 needed')).toBeInTheDocument();
  });

  it('opens a model: report, labelled calibration map, importance, designations and limits', async () => {
    mountRecords({
      models: [buildModel()],
      modelDetails: [buildModelDetail({ reportText: 'fold <b>1</b>' })],
    });
    const { router } = renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Open model #2' }));
    expect(router.state.location.search).toBe('?tab=models&model=2');

    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    expect((await dialog.findByRole('region', { name: 'Validation report with the fold table' })).textContent).toBe(
      'fold <b>1</b>',
    );
    expect(dialog.getByText(/the map from score to probability, not a reliability diagram/i)).toBeInTheDocument();
    expect(dialog.getByRole('table', { name: 'Calibration map' })).toBeInTheDocument();
    expect(dialog.getByText(/not exact, not SHAP/)).toBeInTheDocument();
    expect(dialog.getByText('atr_ratio')).toBeInTheDocument();
    expect(dialog.getByText('Registered.')).toBeInTheDocument();
    expect(dialog.getByText('Advisory: a model changes no plan.')).toBeInTheDocument();
    // Read only: the only button is the close button.
    expect(dialog.getAllByRole('button')).toHaveLength(1);
  });

  it('says so when the calibration list is empty', async () => {
    mountRecords({ modelDetails: [buildModelDetail({ calibration: [] })] });
    renderLearning('?tab=models&model=2');
    expect(await screen.findByText('No calibration table was stored.')).toBeInTheDocument();
  });

  it('filters by role', async () => {
    mountRecords({ models: [buildModel(), buildModel({ id: 1, role: 'champion' })] });
    const { router } = renderLearning('?tab=models');
    await screen.findByText('Model #1');
    await userEvent.click(screen.getByRole('button', { name: 'champion' }));
    expect(router.state.location.search).toContain('role=champion');
    await screen.findByText('Role: champion');
    expect(screen.queryByText('Model #2')).not.toBeInTheDocument();
  });
});

describe('Learning, Digest tab', () => {
  it('says the weekly service builds one when none is stored', async () => {
    mountRecords();
    renderLearning('?tab=digest');
    expect(
      await screen.findByText(/The weekly service builds one each week once it is switched on \(Services\)/),
    ).toBeInTheDocument();
  });

  it('lists the weeks and shows the newest digest as text, with the plan label and footer', async () => {
    mountRecords({
      digests: [
        buildDigest({
          sections: [
            { key: 'cells', title: 'Cells', lines: ['<b>2</b> new cells.'] },
            { key: 'plans', title: 'Plan outcomes (simulated on plan prices)', lines: ['3 graded.'] },
          ],
        }),
        buildDigest({
          id: 4,
          isoWeek: '2026-W42',
          weekStart: '2026-10-12',
          weekEnd: '2026-10-18',
          sections: [{ key: 'cells', title: 'Cells', lines: ['Older week.'] }],
        }),
      ],
    });
    const { router } = renderLearning('?tab=digest');
    const weeks = within(await screen.findByRole('list', { name: 'Stored digests, newest week first' }));
    expect(weeks.getByText(/2026-W43/)).toBeInTheDocument();
    expect(await screen.findByText('<b>2</b> new cells.')).toBeInTheDocument();
    expect(screen.getByText('Plan outcomes (simulated on plan prices)')).toBeInTheDocument();
    expect(screen.getByText('Every section says nothing was applied.')).toBeInTheDocument();

    await userEvent.click(weeks.getByRole('button', { name: /2026-W42/ }));
    expect(router.state.location.search).toBe('?tab=digest&week=2026-W42');
    expect(await screen.findByText('Older week.')).toBeInTheDocument();
  });

  it('says no digest is stored for a week the list lacks', async () => {
    mountRecords({ digests: [buildDigest()] });
    renderLearning('?tab=digest&week=2026-W01');
    expect(await screen.findByText('No digest stored for this week')).toBeInTheDocument();
  });
});

describe('Learning, markup in server text', () => {
  const markup = '<img src=x onerror=alert(1)>';

  it('draws model reasons, designation reasons and an empty designation list as text', async () => {
    mountRecords({
      models: [buildModel({ reasons: [markup] })],
      modelDetails: [
        buildModelDetail({
          designations: [
            { id: 1, role: 'champion', reason: markup, createdAt: '2026-10-19T04:30:00Z', createdBy: 'cli' },
          ],
        }),
      ],
    });
    renderLearning('?tab=models&model=2');
    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    expect(await dialog.findByText(markup)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    expect(screen.getByRole('list', { name: 'Reasons for model 2', hidden: true })).toHaveTextContent(markup);
  });

  it('says no designation is stored', async () => {
    mountRecords({ modelDetails: [buildModelDetail({ designations: [] })] });
    renderLearning('?tab=models&model=2');
    expect(await screen.findByText('No designation stored')).toBeInTheDocument();
  });

  it('gives each verdict its own icon and its word', async () => {
    mountRecords({
      models: [
        buildModel(),
        buildModel({ id: 1, verdict: 'CannotBeatBaselines' }),
        buildModel({ id: 3, verdict: 'Inconclusive' }),
      ],
    });
    renderLearning('?tab=models');
    const chip = async (word: string) => (await screen.findByText(word)).closest<HTMLElement>('.MuiChip-root')!;
    expect(within(await chip('Eligible')).getByTestId('CheckOutlinedIcon')).toBeInTheDocument();
    expect(within(await chip('Cannot beat baselines')).getByTestId('CancelOutlinedIcon')).toBeInTheDocument();
    expect(within(await chip('Inconclusive')).getByTestId('HelpOutlineOutlinedIcon')).toBeInTheDocument();
  });

  it('treats a malformed week in the address as no digest, asking the server nothing', async () => {
    mountRecords({ digests: [buildDigest()] });
    renderLearning('?tab=digest&week=../etc');
    expect(await screen.findByText('No digest stored for this week')).toBeInTheDocument();
  });
});
