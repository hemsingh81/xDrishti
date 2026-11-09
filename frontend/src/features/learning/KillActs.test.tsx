import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildDecayReport, buildProposal, buildProposals, mountConfidence } from '@/test/confidenceFixtures';
import { buildCell, buildCellState, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

afterEach(() => {
  server.events.removeAllListeners();
});

const cellKey = 'orb|15m|BUY|intraday|fixed_r|*';

function mount(items = [buildProposal({ id: 8 })], auto = false, paused: string[] = [cellKey]) {
  mountLearning({
    cells: { all: [buildCell({ strategy: 'orb' })] },
    states: Object.fromEntries(paused.map((cell) => [cell, buildCellState({ cell, state: 'paused' })])),
  });
  mountConfidence({
    decay: buildDecayReport(),
    proposals: buildProposals({
      items,
      settings: { auto, settingId: null, stressMultiplier: 2, drawdownFactor: 2, settingsHash: 'h' },
    }),
  });
  const calls: { url: string; method: string; body: unknown }[] = [];
  server.events.on('request:start', async ({ request }) => {
    if (request.method !== 'GET') {
      calls.push({
        url: new URL(request.url).pathname,
        method: request.method,
        body: await request
          .clone()
          .json()
          .catch(() => null),
      });
    }
  });
  return calls;
}

async function fill(dialog: HTMLElement, password = 'correct-horse') {
  await userEvent.type(within(dialog).getByLabelText(/Reason/), 'looked at it');
  await userEvent.type(within(dialog).getByLabelText(/Password/), password);
}

const open = async (name: string) => {
  await userEvent.click(await screen.findByRole('button', { name }));
  return screen.findByRole('dialog');
};

describe('Kill proposal owner acts', () => {
  it('sends nothing on load or when a dialog is cancelled, and says what dismiss does not do', async () => {
    const calls = mount();
    renderLearning('?tab=decay');
    const dialog = await open('Dismiss proposal 8');
    expect(within(dialog).getByText(/It changes no cell and no lifecycle state/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(calls).toEqual([]);
  });

  it('dismisses with a reason and a password, shows the outcome and keeps no password', async () => {
    const calls = mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/dismiss', () =>
        HttpResponse.json({
          proposalId: 8,
          strategy: 'orb',
          rule: 'cost_stress',
          state: 'dismissed',
          alreadyDismissed: false,
          actor: 'owner:me',
          reason: 'looked at it',
        }),
      ),
    );
    const { client } = renderLearning('?tab=decay');
    const dialog = await open('Dismiss proposal 8');
    await fill(dialog);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Dismiss' }));
    expect(await screen.findByText('Proposal 8 dismissed. No cell was changed.')).toBeInTheDocument();
    expect(calls).toEqual([
      {
        url: '/api/confidence/kill-proposals/8/dismiss',
        method: 'POST',
        body: { reason: 'looked at it', password: 'correct-horse' },
      },
    ]);
    expect(document.body.innerHTML).not.toContain('correct-horse');
    expect(client.getMutationCache().getAll()).toHaveLength(0);
  });

  it('says so when it was already dismissed', async () => {
    mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/dismiss', () =>
        HttpResponse.json({
          proposalId: 8,
          strategy: 'orb',
          rule: 'cost_stress',
          state: 'dismissed',
          alreadyDismissed: true,
          actor: 'owner:first',
          reason: 'earlier',
        }),
      ),
    );
    renderLearning('?tab=decay');
    await fill(await open('Dismiss proposal 8'));
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(await screen.findByText(/Already dismissed by owner:first \(reason: earlier\)/)).toBeInTheDocument();
  });

  it.each<[number, string, string]>([
    [401, 'auth.invalid_credentials', 'The password is wrong.'],
    [403, 'auth.step_up_required', 'A fresh password is required.'],
    [423, 'auth.locked', 'Too many wrong passwords; locked.'],
    [429, 'auth.rate_limited', 'Slow down.'],
    [400, 'confidence.reason_invalid', 'The reason is not valid.'],
    [404, 'confidence.proposal_not_found', 'No such proposal.'],
    [409, 'confidence.proposal_applied', 'It was already applied.'],
    [409, 'confidence.decision_refused', 'The decision was refused.'],
  ])('shows the server words for a %i %s refusal of a dismissal and stays signed in', async (status, code, words) => {
    mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/dismiss', () => problemResponse({ title: words, code }, status)),
    );
    renderLearning('?tab=decay');
    await fill(await open('Dismiss proposal 8'), 'bad');
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(await screen.findByText(words)).toBeInTheDocument();
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    expect(document.body.innerHTML).not.toContain('bad"');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(await screen.findByRole('table', { name: 'Kill proposals, newest first' })).toBeInTheDocument();
  });

  it('applies after a plain confirmation without a password', async () => {
    const calls = mount([buildProposal({ id: 8, cells: [cellKey] })]);
    server.use(
      http.post('/api/confidence/kill-proposals/:id/apply', () =>
        HttpResponse.json({
          proposalId: 8,
          strategy: 'orb',
          rule: 'cost_stress',
          state: 'applied',
          alreadyApplied: false,
          pausedCells: [cellKey],
          skippedCells: [],
          actor: 'owner:me',
        }),
      ),
    );
    renderLearning('?tab=decay');
    const dialog = await open('Apply proposal 8');
    expect(within(dialog).queryByLabelText(/Password/)).toBeNull();
    expect(calls).toEqual([]);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Apply' }));
    expect(
      await screen.findByText(
        new RegExp(`Proposal 8 applied. Paused: ${cellKey.replaceAll('|', '\\|').replace('*', '\\*')}`),
      ),
    ).toBeInTheDocument();
    expect(calls).toEqual([{ url: '/api/confidence/kill-proposals/8/apply', method: 'POST', body: null }]);
  });

  it('shows the server words when the proposal is closed', async () => {
    mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/apply', () =>
        problemResponse({ title: 'The proposal is already closed.', code: 'confidence.proposal_closed' }, 409),
      ),
    );
    renderLearning('?tab=decay');
    await userEvent.click(within(await open('Apply proposal 8')).getByRole('button', { name: 'Apply' }));
    expect(await screen.findByText('The proposal is already closed.')).toBeInTheDocument();
  });

  it('re-enables an applied proposal cell with a reason and a password', async () => {
    const calls = mount([
      buildProposal({
        id: 7,
        state: 'applied',
        actor: 'owner:me',
        decidedAt: '2026-11-05T04:00:00Z',
        cells: [cellKey],
      }),
    ]);
    server.use(
      http.post('/api/learning/cells/:key/re-enable', () =>
        HttpResponse.json({
          cell: cellKey,
          changelogId: 5,
          from: 'paused',
          to: 'paper',
          warnings: ['The CF-11 gates are not enforcing.'],
        }),
      ),
    );
    renderLearning('?tab=decay');
    const dialog = await open(`Re-enable cell ${cellKey}`);
    await fill(dialog);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Re-enable' }));
    expect(
      await screen.findByText(/moved from paused to paper\. The CF-11 gates are not enforcing\./),
    ).toBeInTheDocument();
    expect(calls[0]).toMatchObject({ method: 'POST', body: { reason: 'looked at it', password: 'correct-horse' } });
  });

  it('shows a wrong password on a re-enable as an answer', async () => {
    mount([buildProposal({ id: 7, state: 'applied', cells: [cellKey] })]);
    server.use(
      http.post('/api/learning/cells/:key/re-enable', () =>
        problemResponse({ title: 'The password is wrong.', code: 'auth.invalid_credentials' }, 401),
      ),
    );
    renderLearning('?tab=decay');
    await fill(await open(`Re-enable cell ${cellKey}`));
    await userEvent.click(screen.getByRole('button', { name: 'Re-enable' }));
    expect(await screen.findByText('The password is wrong.')).toBeInTheDocument();
  });

  it('warns that turning automatic kill ON is serious, then sends the setting', async () => {
    const calls = mount();
    server.use(http.put('/api/confidence/kill-settings', () => HttpResponse.json({ id: 3, auto: true })));
    renderLearning('?tab=decay');
    const dialog = await open('Turn automatic kill ON');
    expect(within(dialog).getByText(/This is a serious owner act/)).toBeInTheDocument();
    expect(within(dialog).getByText(/never as your approval/)).toBeInTheDocument();
    await fill(dialog);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn ON' }));
    expect(await screen.findByText('Automatic kill is now ON.')).toBeInTheDocument();
    expect(calls).toEqual([
      {
        url: '/api/confidence/kill-settings',
        method: 'PUT',
        body: { auto: true, reason: 'looked at it', password: 'correct-horse' },
      },
    ]);
  });

  it('turns it OFF from ON with a fresh password and shows a refusal in the server words', async () => {
    mount([], true);
    server.use(
      http.put('/api/confidence/kill-settings', () =>
        problemResponse({ title: 'A fresh password is required.', code: 'auth.step_up_required' }, 403),
      ),
    );
    renderLearning('?tab=decay');
    const dialog = await open('Turn automatic kill OFF');
    expect(within(dialog).getByText(/returns to manual approval/)).toBeInTheDocument();
    await fill(dialog);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn OFF' }));
    expect(await screen.findByText('A fresh password is required.')).toBeInTheDocument();
  });

  it('shows a dismissed proposal with its reason and no act button', async () => {
    mount([
      buildProposal({
        id: 9,
        state: 'dismissed',
        actor: 'owner:me',
        decidedAt: '2026-11-05T04:00:00Z',
        dismissReason: 'noise',
      }),
    ]);
    renderLearning('?tab=decay');
    const table = await screen.findByRole('table', { name: 'Kill proposals, newest first' });
    expect(within(table).getByText(/Reason: noise/)).toBeInTheDocument();
    expect(within(table).getByText('Dismissed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Dismiss proposal 9/ })).toBeNull();
  });

  it('offers Re-enable for a cell paused under a dismissed proposal, and not for one that is no longer paused', async () => {
    const other = 'vwap|5m|BUY|intraday|fixed_r|*';
    mount(
      [
        buildProposal({
          id: 9,
          state: 'dismissed',
          actor: 'owner:me',
          decidedAt: '2026-11-05T04:00:00Z',
          cells: [cellKey],
        }),
        buildProposal({
          id: 7,
          state: 'applied',
          actor: 'owner:me',
          decidedAt: '2026-11-05T04:00:00Z',
          cells: [other],
        }),
      ],
      false,
      [cellKey],
    );
    renderLearning('?tab=decay');
    expect(await screen.findByRole('button', { name: `Re-enable cell ${cellKey}` })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: `Re-enable cell ${other}` })).toBeNull();
  });

  it('says no cell is paused when none is', async () => {
    mount([buildProposal({ id: 7, state: 'applied', cells: [cellKey] })], false, []);
    renderLearning('?tab=decay');
    expect(await screen.findByText('No cell is paused.')).toBeInTheDocument();
  });

  it('shows the server words when the proposal was dismissed during the apply, with the paused cells', async () => {
    mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/apply', () =>
        problemResponse(
          {
            title: `Dismissed meanwhile; these stay paused: ${cellKey}.`,
            code: 'confidence.proposal_dismissed_during_apply',
          },
          409,
        ),
      ),
    );
    renderLearning('?tab=decay');
    await userEvent.click(within(await open('Apply proposal 8')).getByRole('button', { name: 'Apply' }));
    expect(await screen.findByText(/Dismissed meanwhile; these stay paused/)).toBeInTheDocument();
  });

  it('keeps the reason after a refusal, drops the password, and shows the length limits', async () => {
    mount();
    server.use(
      http.post('/api/confidence/kill-proposals/:id/dismiss', () =>
        problemResponse({ title: 'The password is wrong.', code: 'auth.invalid_credentials' }, 401),
      ),
    );
    renderLearning('?tab=decay');
    const first = await open('Dismiss proposal 8');
    expect(within(first).getByText('0 of 400 characters')).toBeInTheDocument();
    await fill(first, 'bad');
    await userEvent.click(within(first).getByRole('button', { name: 'Dismiss' }));
    await screen.findByText('The password is wrong.');
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByLabelText(/Reason/)).toHaveValue('looked at it');
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
  });

  it('shows 300 for a re-enable and 500 for the automatic setting', async () => {
    mount([buildProposal({ id: 7, state: 'applied', cells: [cellKey] })]);
    renderLearning('?tab=decay');
    const re = await open(`Re-enable cell ${cellKey}`);
    expect(within(re).getByText('0 of 300 characters')).toBeInTheDocument();
    await userEvent.click(within(re).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(await open('Turn automatic kill ON')).getByText('0 of 500 characters')).toBeInTheDocument();
  });
});
