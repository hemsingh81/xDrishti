import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildModel, buildModelDetail, mountRecords } from '@/test/learningFixtures';
import { buildHoldingInfluence, buildInfluence, buildRollback, buildSettings } from '@/test/mlFixtures';
import { pinClock } from '@/test/pinClock';
import { server } from '@/test/server';
import { renderLearning } from './testing';

pinClock('2026-10-25T05:00:00Z');

afterEach(() => {
  server.events.removeAllListeners();
});

const models = () => ({ models: [buildModel()], modelDetails: [buildModelDetail()] });

describe('Models drawer, calibration and influence', () => {
  it("shows today's refusal: no term is a pass, the hold-out is kept, nothing is invented", async () => {
    mountRecords(models());
    renderLearning('?tab=models&model=2');
    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));

    const terms = within(await dialog.findByRole('list', { name: 'Influence predicate terms' }));
    expect(terms.getAllByText('Unavailable (not a pass)')).toHaveLength(3);
    expect(terms.getByText('Fails (not a pass)')).toBeInTheDocument();
    expect(terms.getByText('Hold-out: beats the best no-ML baseline')).toBeInTheDocument();
    expect(terms.getByText('ml.influence.enabled is OFF')).toBeInTheDocument();
    expect(
      dialog.getByText('This model may not influence a decision. Anything but "Holds" is not a pass.'),
    ).toBeInTheDocument();

    // A reason that is in no term (the champion flag) is listed once, apart.
    expect(dialog.getByText('the model is not the champion (not_champion)')).toBeInTheDocument();
    expect(dialog.getByText('no (role: challenger)')).toBeInTheDocument();
    expect(dialog.getByText(/Kept: not looked at yet; family v3/)).toBeInTheDocument();
    expect(dialog.getByText('hold-out probability at least 0.95; 30 candidates over 4 weeks')).toBeInTheDocument();
    expect(dialog.getByText('The hold-out, forward and calibration figures have no source yet.')).toBeInTheDocument();

    // Figures without a source say so and show no digit.
    const odds = dialog.getByText('Odds of beating the best no-ML baseline').nextElementSibling;
    expect(odds).toHaveTextContent('Not available');
    expect(odds).not.toHaveTextContent(/\d/);
    expect(dialog.getByText('Interval of the gain over the baseline').nextElementSibling).toHaveTextContent(
      'Not available',
    );

    // Calibration: the stored error is shown, the rest says why it is missing.
    expect(dialog.getByText('Pooled calibration error').nextElementSibling).toHaveTextContent('2.50 percentage points');
    expect(dialog.getByText('Calibration kind').nextElementSibling).toHaveTextContent(
      /calibration_kind has no column yet/,
    );
    expect(dialog.getByText('Ladder rung').nextElementSibling).toHaveTextContent('Not available');

    // Three switches OFF and the automatic hold-out score ON (ADR 0282), read only here.
    const switches = within(dialog.getByRole('list', { name: 'ML switches' }));
    expect(switches.getAllByText('OFF')).toHaveLength(3);
    expect(dialog.getAllByRole('button')).toHaveLength(2); // close, and the owner's hold-out score
  });

  it('shows a model whose terms hold, with small figures at four decimals', async () => {
    mountRecords({
      ...models(),
      ml: {
        influence: buildHoldingInfluence(),
        settings: buildSettings({ influenceEnabled: true, autoHoldOutScore: false }),
      },
    });
    renderLearning('?tab=models&model=2');
    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    const terms = within(await dialog.findByRole('list', { name: 'Influence predicate terms' }));
    expect(terms.getAllByText('Holds')).toHaveLength(4);
    expect(
      dialog.getByText('Every term holds and this model is the champion: it may influence a decision.'),
    ).toBeInTheDocument();
    expect(dialog.getByText('Odds of beating the best no-ML baseline').nextElementSibling).toHaveTextContent('0.9712');
    expect(dialog.getByText('Effect of the take rule').nextElementSibling).toHaveTextContent('+0.0045 R');
    expect(dialog.getByText('Hold-out rows').nextElementSibling).toHaveTextContent('240');
    expect(dialog.getByText('yes')).toBeInTheDocument();
    expect(within(dialog.getByRole('list', { name: 'ML switches' })).getByText('ON')).toBeInTheDocument();
  });

  it('says so when the influence check cannot be read', async () => {
    mountRecords(models());
    server.use(
      http.get('/api/learning/models/:id/influence', () => HttpResponse.json({ title: 'Down' }, { status: 500 })),
    );
    renderLearning('?tab=models&model=2');
    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    expect(await dialog.findByText('Down')).toBeInTheDocument();
    // The rest of the drawer still shows.
    expect(dialog.getByRole('table', { name: 'Calibration map' })).toBeInTheDocument();
  });
});

describe('Models tab, ML switches and rollback', () => {
  it('only reads on load: every request is a GET and every switch is OFF', async () => {
    const methods: string[] = [];
    server.events.on('request:start', ({ request }) => {
      methods.push(request.method);
    });
    mountRecords({ ...models(), ml: { proposals: [buildRollback()] } });
    renderLearning('?tab=models');
    const panel = within(await screen.findByRole('region', { name: 'ML switches' }));
    expect(await panel.findAllByText('OFF')).toHaveLength(3);
    await screen.findByRole('table', { name: /Rollback proposals/ });
    expect(new Set(methods)).toEqual(new Set(['GET']));
  });

  it('says no rollback has been proposed, and that automatic rollback is OFF', async () => {
    mountRecords(models());
    renderLearning('?tab=models');
    const panel = within(await screen.findByRole('region', { name: 'Rollback' }));
    expect(await panel.findByText(/No rollback has been proposed/)).toBeInTheDocument();
    expect(panel.getByText('Automatic rollback')).toBeInTheDocument();
    expect(panel.getByText(/OFF \(default, manual approval\)/)).toBeInTheDocument();
    expect(panel.queryByRole('button')).toBeNull();
    expect(panel.getByText(/you name the next one|until you name the next one/)).toBeInTheDocument();
  });

  it('lists an open and an applied proposal; only the open one can be applied', async () => {
    mountRecords({
      ...models(),
      ml: {
        proposals: [
          buildRollback(),
          buildRollback({
            id: 6,
            previousQualifies: true,
            trigger: 'band',
            cells: ['orb:NIFTY'],
            state: 'applied',
            actor: 'auto',
            decidedAt: '2026-10-24T11:00:00Z',
          }),
        ],
      },
    });
    renderLearning('?tab=models');
    const table = within(await screen.findByRole('table', { name: /Rollback proposals/ }));
    const rows = table.getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent('Calibration detector');
    expect(rows[1]).toHaveTextContent('Open (proposed)');
    expect(rows[1]).toHaveTextContent('0.12');
    expect(rows[1]).toHaveTextContent('Model #1, did not qualify when proposed (information only)');
    expect(rows[2]).toHaveTextContent('Rolling band detector');
    expect(rows[2]).toHaveTextContent('Model #1, qualified when proposed (information only)');
    expect(rows[2]).toHaveTextContent('Applied');
    expect(rows[2]).toHaveTextContent('auto');
    expect(rows[2]).toHaveTextContent('orb:NIFTY');
    expect(table.getAllByRole('button')).toHaveLength(1);
    expect(table.getByRole('button', { name: 'Apply rollback #7' })).toBeInTheDocument();
  });

  it('applies a rollback with the session only: no password, says nothing is re-promoted, and the row turns applied', async () => {
    const { ml } = mountRecords({ ...models(), ml: { proposals: [buildRollback()] } });
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Apply rollback #7' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Apply rollback #7' }));
    expect(dialog.queryByLabelText(/password/i)).toBeNull();
    expect(dialog.getByText(/never re-promotes the previous champion/)).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Yes, apply rollback #7' }));

    expect(
      await dialog.findByText(
        /Proposal #7 is applied\. Model #2 moved from champion to challenger\. There is no champion until you name one\./,
      ),
    ).toBeInTheDocument();
    expect(ml.applied).toEqual([7]);
    await userEvent.click(dialog.getByRole('button', { name: 'Done' }));
    const table = within(await screen.findByRole('table', { name: /Rollback proposals/ }));
    await waitFor(() => {
      expect(table.queryByRole('button', { name: 'Apply rollback #7' })).toBeNull();
    });
    expect(table.getByText('owner:me', { exact: false })).toBeInTheDocument();
  });

  it.each([
    [
      409,
      'learning.rollback_closed',
      'The proposal is closed.',
      /Refused: The proposal is closed\. \(learning\.rollback_closed\) Nothing was changed\./,
    ],
    [
      403,
      'auth.forbidden',
      'Not the owner.',
      /You are not allowed to do this\. Nothing was changed\. Not the owner\. \(auth\.forbidden\)/,
    ],
    [
      404,
      'learning.rollback_not_found',
      'No such proposal.',
      /It was not found; it may be gone\. Nothing was changed\. No such proposal\. \(learning\.rollback_not_found\)/,
    ],
  ])(
    'shows a %i refusal with its code, keeps the dialog open and leaves the row unchanged',
    async (status, code, title, text) => {
      const { ml } = mountRecords({
        ...models(),
        ml: { proposals: [buildRollback()], refuse: { status, code, title } },
      });
      renderLearning('?tab=models');
      await userEvent.click(await screen.findByRole('button', { name: 'Apply rollback #7' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Apply rollback #7' }));
      await userEvent.click(dialog.getByRole('button', { name: 'Yes, apply rollback #7' }));
      const alert = await dialog.findByRole('alert');
      expect(alert).toHaveTextContent(text);
      expect(alert).toHaveTextContent(code);
      expect(dialog.getByRole('button', { name: 'Yes, apply rollback #7' })).toBeEnabled();
      expect(ml.applied).toEqual([]);

      await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
      const table = within(await screen.findByRole('table', { name: /Rollback proposals/ }));
      const row = table.getByRole('row', { name: /Open \(proposed\)/ });
      expect(row).toHaveTextContent('Not decided');
      expect(within(row).getByRole('button', { name: 'Apply rollback #7' })).toBeInTheDocument();
    },
  );

  it('says the session ended, not a wrong password, when a session-only apply is answered 401', async () => {
    mountRecords({
      ...models(),
      ml: { proposals: [buildRollback()], refuse: { status: 401, code: 'auth.session_expired', title: 'Sign in.' } },
    });
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Apply rollback #7' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Apply rollback #7' }));
    await userEvent.click(dialog.getByRole('button', { name: 'Yes, apply rollback #7' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent('Your session has ended. Nothing was changed.');
    expect(dialog.queryByText(/password was not accepted/)).toBeNull();
  });

  it('says the session ended, not a wrong password, when turning a switch OFF is answered 401', async () => {
    mountRecords({
      ...models(),
      ml: {
        settings: buildSettings({ autoRollback: true }),
        refuse: { status: 401, code: 'auth.session_expired', title: 'Sign in.' },
      },
    });
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off automatic rollback' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn automatic rollback OFF' }));
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'enough');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn automatic rollback OFF' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent('Your session has ended. Nothing was changed.');
    expect(dialog.queryByText(/password was not accepted/)).toBeNull();
  });

  it('turns influence ON only with a reason and a password, sends only that switch, and clears the password', async () => {
    const { ml } = mountRecords(models());
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on model influence' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn model influence ON' }));
    const submit = dialog.getByRole('button', { name: 'Turn model influence ON' });

    await userEvent.type(dialog.getByLabelText(/^Reason/), 'trial');
    expect(submit).toBeDisabled();
    await userEvent.type(dialog.getByLabelText(/^Your password for this change/), 'correct-horse');
    expect(submit).toBeEnabled();
    await userEvent.click(submit);

    expect(await dialog.findByText(/Saved: ml\.influence\.enabled is now ON/)).toBeInTheDocument();
    expect(ml.switchBodies).toEqual([
      {
        influenceEnabled: true,
        autoPromote: null,
        autoRollback: null,
        autoHoldOutScore: null,
        reason: 'trial',
        password: 'correct-horse',
      },
    ]);
    expect(dialog.queryByLabelText(/^Your password for this change/)).toBeNull();
    await userEvent.click(dialog.getByRole('button', { name: 'Done' }));
    const panel = within(screen.getByRole('region', { name: 'ML switches' }));
    expect(await panel.findByRole('button', { name: 'Turn off model influence' })).toBeInTheDocument();
  });

  it('asks a password for automatic promotion ON too', async () => {
    const { ml } = mountRecords(models());
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on automatic promotion' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn automatic promotion ON' }));
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'go');
    expect(dialog.getByRole('button', { name: 'Turn automatic promotion ON' })).toBeDisabled();
    await userEvent.type(dialog.getByLabelText(/^Your password for this change/), 'pw');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn automatic promotion ON' }));
    await dialog.findByText(/Saved: ml\.auto_promote is now ON/);
    expect(ml.switchBodies[0]).toMatchObject({ autoPromote: true, password: 'pw' });
  });

  it('turns automatic rollback ON without a password field and sends none', async () => {
    const { ml } = mountRecords(models());
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on automatic rollback' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn automatic rollback ON' }));
    expect(dialog.queryByLabelText(/password/i)).toBeNull();
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'less reliance');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn automatic rollback ON' }));
    await dialog.findByText(/Saved: ml\.auto_rollback is now ON/);
    expect(ml.switchBodies).toEqual([
      {
        influenceEnabled: null,
        autoPromote: null,
        autoRollback: true,
        autoHoldOutScore: null,
        reason: 'less reliance',
        password: null,
      },
    ]);
  });

  it('turns an ON switch OFF with a reason only', async () => {
    const { ml } = mountRecords({ ...models(), ml: { settings: buildSettings({ influenceEnabled: true }) } });
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off model influence' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn model influence OFF' }));
    expect(dialog.queryByLabelText(/password/i)).toBeNull();
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'enough');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn model influence OFF' }));
    await dialog.findByText(/Saved: ml\.influence\.enabled is now OFF/);
    expect(ml.switchBodies[0]).toEqual({
      influenceEnabled: false,
      autoPromote: null,
      autoRollback: null,
      autoHoldOutScore: null,
      reason: 'enough',
      password: null,
    });
  });

  it('asks a password for any change of the automatic hold-out score, OFF included, and sends only that switch', async () => {
    const { ml } = mountRecords(models());
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off automatic hold-out score' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn automatic hold-out score OFF' }));
    const submit = dialog.getByRole('button', { name: 'Turn automatic hold-out score OFF' });

    await userEvent.type(dialog.getByLabelText(/^Reason/), 'I will choose myself');
    expect(submit).toBeDisabled();
    await userEvent.type(dialog.getByLabelText(/^Your password for this change/), 'pw');
    await userEvent.click(submit);

    await dialog.findByText(/Saved: ml\.holdout\.auto_score is now OFF/);
    expect(ml.switchBodies).toEqual([
      {
        influenceEnabled: null,
        autoPromote: null,
        autoRollback: null,
        autoHoldOutScore: false,
        reason: 'I will choose myself',
        password: 'pw',
      },
    ]);
  });

  it('scores a kept model on the hold-out only behind a password and then reads the figures again', async () => {
    const { ml } = mountRecords(models());
    renderLearning('?tab=models&model=2');
    const drawer = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    await userEvent.click(await drawer.findByRole('button', { name: 'Score on hold-out' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Score model #2 on the hold-out' }));
    const submit = dialog.getByRole('button', { name: 'Score on hold-out' });

    expect(submit).toBeDisabled();
    await userEvent.type(dialog.getByLabelText(/^Your password for this act/), 'pw');
    await userEvent.click(submit);

    expect(
      await dialog.findByText(/Scored on 140 hold-out candidates of the block from 2026-10-31/),
    ).toBeInTheDocument();
    expect(ml.scoreBodies).toEqual([{ id: 2, password: 'pw' }]);
    expect(dialog.queryByLabelText(/^Your password for this act/)).toBeNull();
  });

  it('says a refused look as the server sent it and offers no second try as a pass', async () => {
    const { ml } = mountRecords(models());
    ml.refuse = { status: 409, code: 'learning.holdout_reused', title: 'The family already looked.' };
    renderLearning('?tab=models&model=2');
    const drawer = within(await screen.findByRole('dialog', { name: 'Model #2' }));
    await userEvent.click(await drawer.findByRole('button', { name: 'Score on hold-out' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Score model #2 on the hold-out' }));
    await userEvent.type(dialog.getByLabelText(/^Your password for this act/), 'pw');
    await userEvent.click(dialog.getByRole('button', { name: 'Score on hold-out' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(/learning\.holdout_reused/);
    expect(dialog.getByLabelText(/^Your password for this act/)).toHaveValue('');
  });

  it('offers no score button while the block fills or the family has looked (the server says canScore false)', async () => {
    mountRecords({
      ...models(),
      ml: { influence: buildInfluence({ holdOut: { ...buildInfluence().holdOut, canScore: false } }) },
    });
    renderLearning('?tab=models&model=2');
    const drawer = within(await screen.findByRole('dialog', { name: 'Model #2' }));

    expect(await drawer.findByText(/Kept: not looked at yet/)).toBeInTheDocument();
    expect(drawer.queryByRole('button', { name: 'Score on hold-out' })).toBeNull();
  });

  it('shows the stored hold-out figures and the interval of a scored model, and no score button', async () => {
    mountRecords({
      ...models(),
      ml: {
        influence: buildHoldingInfluence(),
        settings: buildSettings(),
      },
    });
    renderLearning('?tab=models&model=2');
    const dialog = within(await screen.findByRole('dialog', { name: 'Model #2' }));

    expect(await dialog.findByText('Interval of the gain over the baseline')).toBeInTheDocument();
    expect(dialog.getByText('Interval of the gain over the baseline').nextElementSibling).toHaveTextContent(
      '+0.001 to +0.03',
    );
    expect(dialog.queryByRole('button', { name: 'Score on hold-out' })).toBeNull();
  });

  it('says a wrong password was not accepted, clears it and does not end the session', async () => {
    const { ml } = mountRecords(models());
    const { router } = renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on model influence' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn model influence ON' }));
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'trial');
    await userEvent.type(dialog.getByLabelText(/^Your password for this change/), 'wrong');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn model influence ON' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent('The password was not accepted. Nothing was changed.');
    await waitFor(() => {
      expect(dialog.getByLabelText(/^Your password for this change/)).toHaveValue('');
    });
    expect(ml.settings.influenceEnabled).toBe(false);
    expect(router.state.location.pathname).toBe('/learning');
  });

  it('shows the server refusal of a switch change with its code', async () => {
    mountRecords({
      ...models(),
      ml: {
        refuse: { status: 400, code: 'learning.ml_settings_invalid', title: 'Give a reason of 1 to 500 characters.' },
      },
    });
    renderLearning('?tab=models');
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on automatic rollback' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Turn automatic rollback ON' }));
    await userEvent.type(dialog.getByLabelText(/^Reason/), 'x');
    await userEvent.click(dialog.getByRole('button', { name: 'Turn automatic rollback ON' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'Give a reason of 1 to 500 characters. (learning.ml_settings_invalid) Nothing was changed.',
    );
  });

  it('shows the error state when the rollback read fails, in both panels', async () => {
    mountRecords(models());
    server.use(
      http.get('/api/learning/rollback/proposals', () => HttpResponse.json({ title: 'Down' }, { status: 500 })),
    );
    renderLearning('?tab=models');
    expect(
      await within(await screen.findByRole('region', { name: 'Rollback' })).findByText('Down'),
    ).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'ML switches' })).getByText('Down')).toBeInTheDocument();
  });
});
