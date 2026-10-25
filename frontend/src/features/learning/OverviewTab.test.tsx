import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildOverview, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

const tile = async (name: string) => {
  const label = await screen.findByText(name, { selector: 'p' });
  const card = label.closest('.MuiCard-root');
  if (!(card instanceof HTMLElement)) throw new Error(`No tile named ${name}`);
  return within(card);
};

describe('Learning overview', () => {
  it('says what is empty in words and never draws a zero for a missing thing', async () => {
    mountLearning();
    renderLearning();

    expect(await screen.findByRole('heading', { level: 1, name: 'Learning' })).toBeInTheDocument();
    for (const name of [
      'Cell rows (three windows per cell)',
      'Models',
      'Experiments',
      'Hypotheses',
      'Lessons',
      'Changelog rows',
    ]) {
      const t = await tile(name);
      expect(t.getByText('No records yet')).toBeInTheDocument();
      expect(t.queryByText(/\d/)).not.toBeInTheDocument();
    }
    expect(screen.getAllByText(/No snapshot yet; run `xd learn cells`/).length).toBeGreaterThan(0);
    expect(screen.getByText('No models stored yet')).toBeInTheDocument();
    // The empty table says so too, with the server's limits printed as the evidence.
    expect(screen.getByRole('region', { name: 'Overview evidence' })).toHaveTextContent(/nothing is recomputed here/);
    expect(screen.getByText(/no combined health score/)).toBeInTheDocument();
  });

  it('shows a thin store: one model, a truncated count as provisional, the splits and the newest verdicts', async () => {
    const experiment = {
      id: 5,
      label: 'stop_atr',
      verdict: 'Inconclusive',
      detail: 'better in 2 of 3 folds (4 needed); 6 trials',
      at: '2026-10-22T05:00:00Z',
    };
    mountLearning({
      overview: buildOverview({
        cellRows: 12,
        cellsAsOf: '2026-10-20',
        models: {
          counted: 1,
          truncated: false,
          split: [{ name: 'challenger', count: 1 }],
          newest: {
            ...experiment,
            id: 2,
            label: 'challenger',
            verdict: 'NotBetter',
            detail: 'beat both baselines in 1 of 3 folds',
            at: '2026-10-23T05:00:00Z',
          },
          champion: null,
        },
        experiments: {
          counted: 200,
          truncated: true,
          split: [
            { name: 'Better', count: 20 },
            { name: 'Inconclusive', count: 180 },
          ],
          newest: experiment,
          champion: null,
        },
        changelog: {
          counted: 1,
          truncated: false,
          split: [{ name: 'transition', count: 1 }],
          newest: {
            id: 9,
            label: 'transition',
            verdict: 'candidate',
            detail: 'orb|15m|BUY|intraday|fixed_r|*',
            at: '2026-10-21T04:30:00Z',
          },
          champion: null,
        },
      }),
    });
    renderLearning();

    const cells = await tile('Cell rows (three windows per cell)');
    expect(cells.getByText('12')).toBeInTheDocument();
    expect(cells.getByText('Snapshot as of 20 Oct 2026; all, 250d and 60d rows are counted')).toBeInTheDocument();
    expect(screen.queryByText(/No snapshot yet/)).not.toBeInTheDocument();

    const experiments = await tile('Experiments');
    expect(experiments.getByText('200')).toBeInTheDocument();
    expect(experiments.getByText('Provisional')).toBeInTheDocument();
    expect(experiments.getByText(/there may be more/)).toBeInTheDocument();
    expect(experiments.getByText('Better 20, Inconclusive 180')).toBeInTheDocument();

    expect((await tile('Models')).getByText('challenger 1')).toBeInTheDocument();
    expect((await tile('Lessons')).getByText('No records yet')).toBeInTheDocument();

    const table = screen.getByRole('table', { name: 'Newest records' });
    expect(within(table).getByRole('rowheader', { name: 'Models, newest' })).toBeInTheDocument();
    expect(within(table).getByText('better in 2 of 3 folds (4 needed); 6 trials')).toBeInTheDocument();
    expect(within(table).getByText('Inconclusive')).toBeInTheDocument();
    // A moment is shown in India time.
    expect(within(table).getByText('22 Oct 2026, 10:30 IST')).toBeInTheDocument();
    expect(within(table).getByText('23 Oct 2026, 10:30 IST')).toBeInTheDocument();
  });

  it('names the champion of a full store and draws server text as text', async () => {
    const champion = {
      id: 1,
      label: 'champion',
      verdict: 'Eligible',
      detail: '<img src=x onerror=alert(1)> beat both baselines in 5 of 6 folds',
      at: '2026-10-01T05:00:00Z',
    };
    mountLearning({
      overview: buildOverview({
        cellRows: 300,
        cellsAsOf: '2026-10-20',
        models: {
          counted: 3,
          truncated: false,
          split: [
            { name: 'champion', count: 1 },
            { name: 'challenger', count: 2 },
          ],
          newest: champion,
          champion,
        },
      }),
    });
    renderLearning();

    const table = await screen.findByRole('table', { name: 'Newest records' });
    expect(within(table).getByRole('rowheader', { name: 'Models, champion' })).toBeInTheDocument();
    expect(within(table).getAllByText(/<img src=x onerror=alert\(1\)>/)).toHaveLength(2);
    expect(table.querySelector('img')).toBeNull();
    expect((await tile('Models')).getByText('champion 1, challenger 2')).toBeInTheDocument();
  });

  it('shows the error of a failed request and keeps the page', async () => {
    server.use(http.get('/api/learning/overview', () => problemResponse({ title: 'The overview broke.' }, 500)));
    renderLearning();

    expect(await screen.findByText('The overview broke.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Learning' })).toBeInTheDocument();
  });

  it('shows a loading state while the overview is read', async () => {
    server.use(http.get('/api/learning/overview', () => new Promise<never>(() => undefined)));
    renderLearning();

    expect(await screen.findByLabelText('Loading the overview…')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('opens Setups from the overview', async () => {
    mountLearning();
    const { router } = renderLearning();

    await userEvent.click(await screen.findByRole('button', { name: 'Open Setups' }));
    expect(router.state.location.search).toBe('?tab=setups');
    expect(await screen.findByRole('heading', { name: 'Setups by timeframe' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Setups', selected: true })).toBeInTheDocument();
  });

  it('names the planned tabs in one line from the registry and does not render them as tabs', async () => {
    mountLearning();
    renderLearning();

    expect(await screen.findByText(/Not built yet: Journey \(P8-12\), Smart rules \(P8-13\)/)).toBeInTheDocument();
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      'Overview',
      'Setups',
      'Lessons',
      'Experiments',
      'Models',
      'Digest',
    ]);
  });

  it('serves a hand-edited address: an unknown tab falls back to the overview', async () => {
    server.use(http.get('/api/learning/overview', () => HttpResponse.json(buildOverview())));
    renderLearning('?tab=bogus');

    expect(await screen.findByRole('tab', { name: 'Overview', selected: true })).toBeInTheDocument();
  });
});
