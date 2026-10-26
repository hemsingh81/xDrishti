import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { createHubFake } from '@/test/hubFake';
import { buildExperiment } from '@/test/learningFixtures';
import { renderRoutes } from '@/test/render';
import { runKeyA } from '@/test/reportFixtures';
import { server } from '@/test/server';
import {
  buildJob,
  buildPreview,
  closingSentence,
  jobId,
  mountExperiments,
  noTrial,
  type ExperimentsFake,
} from '@/test/experimentsFake';
import { ExperimentPage } from './components/ExperimentPage';

function renderPage(options: Parameters<typeof mountExperiments>[0] = {}, path = '/owner-acts/experiment') {
  const fake = mountExperiments(options);
  const view = renderRoutes([{ path: '/owner-acts/experiment', element: <ExperimentPage /> }], { initialPath: path });
  return { ...view, fake };
}

async function pickKnob(name: RegExp) {
  await userEvent.click(await screen.findByRole('combobox', { name: /Knob to change/ }));
  await userEvent.click(await screen.findByRole('option', { name }));
}

/** Types the key, then fills the whole form; the box is ticked only when asked. */
async function fillForm({ password = 'correct-horse', tick = true, value = '3' } = {}) {
  await userEvent.type(await screen.findByLabelText('Baseline run key'), runKeyA);
  await pickKnob(/FIXED_RR\.rr/);
  await userEvent.type(screen.getByLabelText(/^New value/), value);
  await userEvent.type(screen.getByLabelText(/^Reason for this replay/), 'Try a wider target');
  if (tick) await userEvent.click(screen.getByRole('checkbox', { name: /counts as a look/ }));
  await userEvent.type(screen.getByLabelText(/^Your password/), password);
}

const at = (path: string) => `/owner-acts/experiment${path}`;
const start = () => screen.getByRole('button', { name: 'Start replay' });

describe('Replay one change, capability and preview', () => {
  it('capability OFF: says so in words, links to the switches and draws no start control', async () => {
    renderPage({ on: false });

    expect(await screen.findByText(/switched off/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'the owner-act switches' })).toHaveAttribute('href', '/owner-acts');
    expect(screen.queryByRole('button', { name: 'Start replay' })).toBeNull();
    expect(screen.queryByLabelText('Baseline run key')).toBeNull();
    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });

  it('ON: lists the stored runs (candidates labelled) and shows the preview for the picked key', async () => {
    renderPage();
    await userEvent.click(await screen.findByRole('combobox', { name: 'Stored run' }));
    expect(await screen.findByRole('option', { name: /experiment candidate/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('option', { name: /^orb, MyChoice.*gross$/ }));

    const facts = await screen.findByRole('group', { name: 'What this replay would mean' });
    expect(within(facts).getByText('Trial number 4')).toBeInTheDocument();
    expect(within(facts).getByText('0.032 R per trade')).toBeInTheDocument();
    expect(within(facts).getByText('3 available, 6 needed to call it better')).toBeInTheDocument();
    expect(within(facts).getByText('2 of 3')).toBeInTheDocument();
    expect(within(facts).getByText('No replay time measured yet.')).toBeInTheDocument();
    expect(screen.getByText(/the best this can be is Inconclusive/)).toBeInTheDocument();
    expect(screen.getByText(/A run is a look: the knob/)).toBeInTheDocument();
  });

  it('a baseline that cannot be rebuilt shows the reason and no form', async () => {
    renderPage({
      preview: buildPreview({ rebuildable: false, notRebuildableReason: 'It is a multi-exit comparison.' }),
    });
    await userEvent.type(await screen.findByLabelText('Baseline run key'), runKeyA);

    expect(await screen.findByText(/It is a multi-exit comparison\./)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start replay' })).toBeNull();
  });

  it('needs the box ticked while the best outcome is Inconclusive, and shows none otherwise', async () => {
    renderPage();
    await fillForm({ tick: false });
    expect(start()).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox', { name: /can at best be Inconclusive/ }));
    expect(start()).toBeEnabled();
  });

  it('draws no box when the data could support Better', async () => {
    renderPage({
      preview: buildPreview({ bestPossibleOutcome: 'Better', bestPossibleSentence: 'Enough folds exist.' }),
    });
    await fillForm({ tick: false });
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(start()).toBeEnabled();
  });

  it('shows an error when the preview cannot be read', async () => {
    renderPage();
    server.use(
      http.get('/api/experiments/preview', () => HttpResponse.json({ title: 'Run not found.' }, { status: 404 })),
    );
    await userEvent.type(await screen.findByLabelText('Baseline run key'), runKeyA);
    expect(await screen.findByText('Run not found.')).toBeInTheDocument();
  });
});

describe('Replay one change, starting', () => {
  it('sends exactly the documented keys, never an owner, clears the password and follows the job', async () => {
    const { fake, router } = renderPage();
    await fillForm();
    await userEvent.click(start());

    await waitFor(() => {
      expect(router.state.location.search).toBe(`?job=${jobId}`);
    });
    expect(fake.starts).toEqual([
      {
        baselineRunKey: runKeyA,
        knob: { kind: 'exit', name: 'FIXED_RR.rr', candidateValue: 3 },
        reason: 'Try a wider target',
        password: 'correct-horse',
      },
    ]);
    expect(await screen.findByRole('progressbar', { name: 'Replay progress' })).toBeInTheDocument();
    expect(screen.getByLabelText(/^Your password/)).toHaveValue('');
  });

  it('a wrong password is said in words, clears the field and does not sign out', async () => {
    const { router } = renderPage();
    await fillForm({ password: 'wrong' });
    await userEvent.click(start());

    expect(await screen.findByRole('alert')).toHaveTextContent('The password was not accepted. Nothing was started.');
    expect(screen.getByLabelText(/^Your password/)).toHaveValue('');
    expect(router.state.location.pathname).toBe('/owner-acts/experiment');
    expect(start()).toBeDisabled();
  });

  it('403: the act is switched off', async () => {
    renderPage({ refuse: { status: 403, code: 'capabilities.off', title: 'Replays are switched off.' } });
    await fillForm();
    await userEvent.click(start());
    expect(await screen.findByRole('alert')).toHaveTextContent('Replays are switched off.');
  });

  it('409: names the active job and links to it', async () => {
    renderPage({
      refuse: {
        status: 409,
        code: 'experiments.job_active',
        title: `Job ${jobId} is already running.`,
      },
    });
    await fillForm();
    await userEvent.click(start());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(jobId);
    expect(within(alert).getByRole('link', { name: 'Follow the active job' })).toHaveAttribute(
      'href',
      `/owner-acts/experiment?job=${jobId}`,
    );
  });

  it('429: says the day limit and when to retry', async () => {
    renderPage({
      refuse: {
        status: 429,
        code: 'experiments.daily_limit',
        title: 'Three replays a day.',
        extra: { retryAfterSeconds: 7200 },
      },
    });
    await fillForm();
    await userEvent.click(start());
    expect(await screen.findByRole('alert')).toHaveTextContent('Three replays a day. Try again in 2h 0m.');
  });

  it('422 and 423 show the server text', async () => {
    renderPage({ refuse: { status: 422, code: 'experiments.knob_not_allowed', title: 'That knob is not allowed.' } });
    await fillForm();
    await userEvent.click(start());
    expect(await screen.findByRole('alert')).toHaveTextContent('That knob is not allowed.');
  });
});

describe('Replay one change, the job', () => {
  it('follows the hub: progress is written in and the end event reads the job again', async () => {
    const hub = createHubFake();
    hub.install();
    const { fake } = renderPage({}, at(`?job=${jobId}`));
    const bar = await screen.findByRole('progressbar', { name: 'Replay progress' });
    expect(bar).toBeInTheDocument();
    expect(screen.getByText('3 of 12')).toBeInTheDocument();
    await waitFor(() => {
      expect(hub.alive).toBe(true);
    });

    hub.experimentProgress({ jobId, state: 'running', stage: 'store', done: 9, total: 12, message: null });
    expect(await screen.findByText('9 of 12')).toBeInTheDocument();
    expect(screen.getByText(/stage store/)).toBeInTheDocument();

    fake.job = buildJob({
      state: 'failed',
      stage: null,
      errorText: 'The candidate has entries the baseline lacks.',
      limits: [noTrial, closingSentence],
    });
    hub.experimentFinished({ jobId, state: 'failed' });
    expect(await screen.findByText('The candidate has entries the baseline lacks.')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: 'Replay progress' })).toBeNull();
  });

  it('an older progress event does not revive a finished job', async () => {
    const hub = createHubFake();
    hub.install();
    renderPage(
      { job: buildJob({ state: 'cancelled', stage: null, errorText: 'Cancelled.', limits: [noTrial] }) },
      at(`?job=${jobId}`),
    );
    expect(await screen.findByText('Cancelled.')).toBeInTheDocument();
    await waitFor(() => {
      expect(hub.alive).toBe(true);
    });
    hub.experimentProgress({ jobId, state: 'running', stage: 'replay', done: 1, total: 12, message: null });
    expect(screen.queryByRole('progressbar', { name: 'Replay progress' })).toBeNull();
  });

  it('polls every 2 s while the hub is away and stops when the job ends', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const fake = mountExperiments();
      renderRoutes([{ path: '/owner-acts/experiment', element: <ExperimentPage /> }], {
        initialPath: at(`?job=${jobId}`),
      });
      expect(await screen.findByText('3 of 12')).toBeInTheDocument();

      fake.job = buildJob({ done: 6 });
      await act(() => vi.advanceTimersByTimeAsync(2100));
      expect(await screen.findByText('6 of 12')).toBeInTheDocument();

      fake.job = buildJob({ state: 'failed', stage: null, errorText: 'Gave up.', limits: [noTrial, closingSentence] });
      await act(() => vi.advanceTimersByTimeAsync(2100));
      expect(await screen.findByText('Gave up.')).toBeInTheDocument();
      const reads = fake.jobReads;
      await act(() => vi.advanceTimersByTimeAsync(10_000));
      expect(fake.jobReads).toBe(reads);
    } finally {
      vi.useRealTimers();
    }
  });

  it('Cancel stops the job without a password and shows the card', async () => {
    const { fake } = renderPage({}, at(`?job=${jobId}`));
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    expect(await screen.findByText('Cancelled by the owner.')).toBeInTheDocument();
    expect(fake.cancels).toBe(1);
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull();
    expect(screen.getByText(noTrial)).toBeInTheDocument();
  });
});

describe('Replay one change, the result card', () => {
  const done = (over: Partial<ExperimentsFake['job']> = {}) =>
    buildJob({
      state: 'succeeded',
      stage: null,
      done: 12,
      candidateRunKey: 'd'.repeat(64),
      experimentId: 9,
      replayMs: 61_000,
      instruments: 12,
      timing: {
        text: 'Replay 1m 1s for 12 instruments over 2024-04-01 to 2026-04-30, measured on this machine; other heavy jobs were running: no. 10-minute target: not comparable: 12 instruments.',
        targetStatus: 'not_comparable',
      },
      limits: ['Replay time is for this machine and this moment.', closingSentence],
      ...over,
    });

  it.each([
    ['Better', 'Better', 6, 6],
    ['NotBetter', 'Not better', 2, 6],
    ['Inconclusive', 'Inconclusive', 3, 3],
  ])(
    'succeeded as %s prints the judgement, timing and limits and ends with the closing sentence',
    async (outcome, word, better, used) => {
      renderPage(
        {
          job: done(),
          judgement: buildExperiment({
            id: 9,
            outcome,
            foldsBetter: better,
            usedFolds: used,
            foldsNeeded: 6,
            trials: 4,
            requiredImprovementR: 0.032,
            baselineTotalR: -12,
            candidateTotalR: 5.9,
            reasons: ['only 3 used folds'],
          }),
        },
        at(`?job=${jobId}`),
      );

      const card = await screen.findByRole('article', { name: 'Result' });
      expect(await within(card).findByText(word)).toBeInTheDocument();
      expect(within(card).getByText(`${better} of ${used} used (6 needed)`)).toBeInTheDocument();
      expect(within(card).getByText('0.032 R per trade')).toBeInTheDocument();
      expect(within(card).getByText(/Baseline -12, candidate 5\.9/)).toBeInTheDocument();
      expect(within(card).getByText('4 in the last 30 days, this one included')).toBeInTheDocument();
      expect(within(card).getByText(/Replay 1m 1s for 12 instruments/)).toBeInTheDocument();
      expect(within(card).getByText('only 3 used folds')).toBeInTheDocument();
      expect(within(card).getByText('Replay time is for this machine and this moment.')).toBeInTheDocument();
      expect(within(card).getByRole('link', { name: 'Open experiment #9 in Learning' })).toHaveAttribute(
        'href',
        '/learning?tab=experiments&experiment=9',
      );
      expect(card.lastElementChild).toHaveTextContent(closingSentence);
    },
  );

  it.each(['failed', 'cancelled', 'interrupted'])(
    '%s prints the error, judges nothing and ends with the closing sentence',
    async (state) => {
      renderPage(
        { job: buildJob({ state, stage: null, errorText: 'Stopped here.', limits: [noTrial, closingSentence] }) },
        at(`?job=${jobId}`),
      );
      const card = await screen.findByRole('article', { name: 'Result' });
      expect(within(card).getByText('Stopped here.')).toBeInTheDocument();
      expect(within(card).getByText(noTrial)).toBeInTheDocument();
      expect(within(card).queryByText(/Folds better/)).toBeNull();
      expect(card.lastElementChild).toHaveTextContent(closingSentence);
      expect(screen.getAllByText(closingSentence)).toHaveLength(1);
    },
  );

  it('renders markup in server text as text', async () => {
    const markup = '<img src=x onerror=alert(1)><b>bold</b>';
    renderPage(
      {
        job: buildJob({
          state: 'failed',
          stage: null,
          errorText: markup,
          limits: [`<script>x</script>`, closingSentence],
        }),
      },
      at(`?job=${jobId}`),
    );
    expect(await screen.findByText(markup)).toBeInTheDocument();
    expect(screen.getByText('<script>x</script>')).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('script')).toBeNull();
  });
});

describe('Replay one change never applies anything', () => {
  const banned = /apply|activate|promote|publish/i;

  it('has no such control, label or accessible name on any state of the screen', async () => {
    renderPage(
      { job: buildJob({ state: 'succeeded', stage: null, experimentId: 9, limits: [closingSentence] }) },
      `/owner-acts/experiment?job=${jobId}`,
    );
    await userEvent.type(await screen.findByLabelText('Baseline run key'), runKeyA);
    await screen.findByRole('group', { name: 'What this replay would mean' });
    await screen.findByRole('article', { name: 'Result' });

    for (const role of ['button', 'link', 'checkbox', 'combobox', 'textbox', 'form', 'group']) {
      for (const element of screen.queryAllByRole(role)) {
        expect(element).not.toHaveAccessibleName(banned);
      }
    }
    expect(document.querySelectorAll('[aria-label]').length).toBeGreaterThan(0);
    document.querySelectorAll('[aria-label],[title],label').forEach((element) => {
      expect(element.getAttribute('aria-label') ?? element.getAttribute('title') ?? element.textContent).not.toMatch(
        banned,
      );
    });
  });

  it('has no such word in the owner-acts sources outside this slice that the replay screen wrote', () => {
    const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
      query: '?raw',
      import: 'default',
      eager: true,
    });
    const experimentFiles = Object.entries(sources).filter(([name]) =>
      /experiment|Experiment|Job|Result|RunPicker|Preview|problemText/.test(name),
    );
    expect(experimentFiles.length).toBeGreaterThanOrEqual(8);
    for (const [name, text] of experimentFiles) {
      // The closing sentence is the one place that names "Applying", as a statement that nothing is applied.
      const without = text
        .replace(/Nothing was changed\. Applying a change is your own separate act\./g, '')
        .replace(/\/apply\|activate\|promote\|publish\//g, '');
      expect({ name, hit: banned.exec(without)?.[0] }).toEqual({ name, hit: undefined });
    }
  });
});
