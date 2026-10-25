import { screen, within } from '@testing-library/react';
import { buildEntry, buildLesson, buildModel, mountLearning } from '@/test/learningFixtures';
import { buildCounterfactual, buildJourney, journeyOf, liveReason, mountJourney } from '@/test/journeyFixtures';
import { pinClock } from '@/test/pinClock';
import { renderLearning } from '../testing';

pinClock('2026-10-25T05:00:00Z');

const open = async () => {
  renderLearning('?tab=journey');
  return screen.findByRole('heading', { name: 'Is the system getting better?' });
};

describe('Learning, Journey tab', () => {
  it('with nothing stored says so with the command, and still draws lessons, pins and plan weeks', async () => {
    mountJourney(
      buildJourney({
        planPrice: {
          state: 'ok',
          weeks: [
            { isoWeek: '2026-W42', start: '2026-10-12', graded: 7, meanR: 0.25, totalR: 1.75, state: 'ok' },
            { isoWeek: '2026-W43', start: '2026-10-19', graded: 3, meanR: null, totalR: 0.5, state: 'insufficient' },
          ],
          totalGraded: 10,
          basis: 'simulated on plan prices',
          note: null,
        },
      }),
      null,
      {
        lessons: [buildLesson({ subject: '<b>orb</b>' })],
        models: [buildModel()],
      },
      [
        buildEntry({
          id: 4,
          kind: 'designation',
          subjectKind: 'model',
          subject: 'model:2',
          toState: 'champion',
          at: '2026-10-22T04:30:00Z',
        }),
      ],
    );
    await open();

    expect(
      await screen.findByText(/scripts\/xd\.sh learn counterfactual --run RUNKEY:STYLE:EXIT … --store/),
    ).toBeInTheDocument();
    const bands = within(await screen.findByRole('list', { name: 'Lesson bands' }));
    expect(bands.getByText(/Lesson #1: <b>orb<\/b>/)).toBeInTheDocument();
    expect(bands.getByText(/today \(open\)/)).toBeInTheDocument();
    const pins = within(screen.getByRole('list', { name: 'Model pins' }));
    expect(pins.getByText(/Model #2 registered \(ffffffff\)/)).toBeInTheDocument();
    expect(pins.getByText(/Model #2 designated champion \(ffffffff\)/)).toBeInTheDocument();
    const weeks = within(screen.getByRole('table', { name: 'Plan-price grades by week' }));
    expect(weeks.getByText(/2026-W42/)).toBeInTheDocument();
    expect(weeks.getByText('+0.25 R')).toBeInTheDocument();
    expect(weeks.getByText(/n 3 of at least 5/)).toBeInTheDocument();
    expect(screen.getByText(/Basis: simulated on plan prices/)).toBeInTheDocument();
  });

  it('says no lessons and no model when none are stored', async () => {
    mountJourney(buildJourney());
    await open();

    expect(await screen.findAllByText(/No lessons recorded/)).not.toHaveLength(0);
    expect(screen.getAllByText(/No model stored/)).not.toHaveLength(0);
    expect(screen.getByText(/No plan has been graded yet/)).toBeInTheDocument();
  });

  it('is inconclusive on fewer than three judged windows and calls no gain an improvement', async () => {
    const report = buildCounterfactual({ outcome: 'RetrainingHelps', judgedWindows: 2 });
    mountJourney(journeyOf(report), report);
    await open();

    expect(await screen.findByText('Inconclusive: 2 judged windows, 3 needed')).toBeInTheDocument();
    expect(screen.queryByText(/improved|helped/i)).not.toBeInTheDocument();
    expect(document.querySelector('[data-state="value"]')).not.toBeNull();
  });

  it('reads no difference as an ordinary answer', async () => {
    const report = buildCounterfactual({ outcome: 'NoDifference', judgedWindows: 4 });
    mountJourney(journeyOf(report), report);
    await open();

    expect(await screen.findByText('No measurable difference on this data')).toBeInTheDocument();
  });

  it.each([
    ['RetrainingHelps', 4, 'Retraining helped out of sample, weak (4 windows)'],
    ['FrozenBetter', 5, 'The frozen model did better out of sample, weak (5 windows)'],
    ['RetrainingHelps', 6, 'Retraining helped out of sample'],
  ])('headlines %s on %i judged windows as "%s"', async (outcome, judged, text) => {
    const report = buildCounterfactual({ outcome, judgedWindows: judged });
    mountJourney(journeyOf(report), report);
    await open();

    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it('shows the windows: uplifts from the server, the unjudged window as a sentence not a bar of 0', async () => {
    const report = buildCounterfactual();
    mountJourney(journeyOf(report), report);
    await open();

    const bars = within(await screen.findByRole('list', { name: 'Take-R uplift of retraining by window' }));
    expect(bars.getByText('+2.40 R')).toBeInTheDocument();
    expect(bars.getByText('-1.50 R')).toBeInTheDocument();
    expect(bars.getByText(/Same model on both sides, not judged/)).toBeInTheDocument();
    expect(screen.getByText(/Cumulative take-R uplift:/).textContent).toContain('+0.90 R');
    const table = within(screen.getByRole('table', { name: 'Counterfactual windows' }));
    expect(table.getByText(/Window 1: .*\(not judged\)/)).toBeInTheDocument();
    expect(table.getAllByRole('row')).toHaveLength(4);
  });

  it('prints the evidence: basis, runs, range, n and the limits, and the unconfirmed sheet wording', async () => {
    const report = buildCounterfactual({ basis: 'costed_unconfirmed' });
    mountJourney(journeyOf(report), report);
    await open();

    const evidence = within(await screen.findByRole('region', { name: 'Journey evidence' }));
    expect(evidence.getByText(/costed on the stored sheet, not confirmed as yours/)).toBeInTheDocument();
    expect(evidence.getByText('n 170')).toBeInTheDocument();
    expect(evidence.getByText(/aaaaaaaaaaaa/)).toBeInTheDocument();
    expect(evidence.getByText('Replayed history, not a forecast.')).toBeInTheDocument();
  });

  it('keeps the live line not evaluable with its reason', async () => {
    const report = buildCounterfactual();
    mountJourney(journeyOf(report), report);
    await open();

    expect(await screen.findByText(liveReason)).toBeInTheDocument();
    expect(screen.getAllByText('Not evaluable').length).toBeGreaterThan(0);
  });

  it('is reachable as a tab of the Learning screen', async () => {
    mountLearning();
    mountJourney(buildJourney());
    renderLearning();

    expect(await screen.findByRole('tab', { name: 'Journey' })).toBeInTheDocument();
  });
});
