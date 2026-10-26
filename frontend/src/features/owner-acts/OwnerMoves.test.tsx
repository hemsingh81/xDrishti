import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildAutonomy, buildEntry, buildHypothesis, buildLesson } from '@/test/learningFixtures';
import { mountOwnerMoves } from '@/test/ownerMovesFake';
import { renderRoutes } from '@/test/render';
import { MovePage } from './components/MovePage';
import { OwnerActsPage } from './components/OwnerActsPage';

function renderMove(path: string) {
  return renderRoutes([{ path: '/owner-acts/:kind/:id', element: <MovePage /> }], { initialPath: path });
}

const reason = () => screen.getByLabelText(/^Reason/);
const password = () => screen.getByLabelText<HTMLInputElement>(/^Your password/);
const submit = (name: RegExp | string) => screen.getByRole('button', { name });

async function fill(text = 'because I read the numbers', pass = 'correct-horse') {
  await userEvent.type(reason(), text);
  await userEvent.type(password(), pass);
}

describe('Lesson moves, capability off', () => {
  it('says it is switched off, links to the switches and draws no field to write with', async () => {
    mountOwnerMoves({ off: ['learning_moves_ui'] });
    renderMove('/owner-acts/lesson/1');

    expect(await screen.findByText(/switched off/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'the owner-act switches' })).toHaveAttribute('href', '/owner-acts');
    expect(screen.queryByLabelText(/password/i)).toBeNull();
    expect(screen.queryByRole('button', { name: /dismiss|link|close|move/i })).toBeNull();
  });

  it('an unknown address moves nothing', async () => {
    mountOwnerMoves();
    renderMove('/owner-acts/model/1');
    expect(await screen.findByText(/nothing to move/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });
});

describe('Lesson moves', () => {
  it('offers only the moves the server lists, and dismisses with a reason and the password only', async () => {
    const fake = mountOwnerMoves();
    const { client } = renderMove('/owner-acts/lesson/1');

    expect(await screen.findByRole('radio', { name: 'Dismiss' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Link a fix' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /close/i })).toBeNull();
    await userEvent.click(screen.getByRole('radio', { name: 'Dismiss' }));
    await fill();
    await userEvent.click(submit('Dismiss'));

    expect(await screen.findByText(/Lesson dismiss done/)).toBeInTheDocument();
    expect(fake.writes).toEqual([
      { route: 'lesson.dismiss', id: '1', body: { reason: 'because I read the numbers', password: 'correct-horse' } },
    ]);
    expect(password()).toHaveValue('');
    expect(document.body.innerHTML).not.toContain('correct-horse');
    expect(
      JSON.stringify(
        client
          .getMutationCache()
          .getAll()
          .map((m) => m.state),
      ),
    ).not.toContain('correct-horse');
    expect(client.getMutationCache().getAll()).toHaveLength(0);
  });

  it('links a hypothesis by its number', async () => {
    const fake = mountOwnerMoves();
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Link a fix' }));
    expect(submit('Link a fix')).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/^Number of the fix/), '3');
    await fill();
    await userEvent.click(submit('Link a fix'));

    await screen.findByText(/Lesson link done/);
    expect(fake.writes[0]?.body).toEqual({
      fixKind: 'hypothesis',
      fixId: 3,
      reason: 'because I read the numbers',
      password: 'correct-horse',
    });
  });

  it('closing a linked lesson takes no number: the body is the reason and the password', async () => {
    const fake = mountOwnerMoves({
      lesson: buildLesson({ id: 1, state: 'linked', nextStates: ['closed', 'dismissed'] }),
    });
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Close with the stored result' }));
    expect(screen.getByText(/You type no number/)).toBeInTheDocument();
    await fill();
    await userEvent.click(submit('Close with the stored result'));

    await screen.findByText(/Lesson close done/);
    expect(Object.keys(fake.writes[0]?.body ?? {}).sort()).toEqual(['password', 'reason']);
  });

  it('a final lesson has no move and no form', async () => {
    mountOwnerMoves({ lesson: buildLesson({ id: 1, state: 'closed', nextStates: [] }) });
    renderMove('/owner-acts/lesson/1');

    expect(await screen.findByText(/allows no move/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });

  it('a wrong password is an answer: the field is cleared and focused, nothing is signed out, nothing moved', async () => {
    const fake = mountOwnerMoves();
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Dismiss' }));
    await fill('a reason', 'wrong');
    await userEvent.click(submit('Dismiss'));

    expect(await screen.findByText(/The password was not accepted. Nothing was changed./)).toBeInTheDocument();
    expect(password()).toHaveValue('');
    await waitFor(() => {
      expect(password()).toHaveFocus();
    });
    expect(screen.getByLabelText(/^Reason/)).toHaveValue('a reason');
    expect(fake.writes).toHaveLength(1);
  });

  it('shows the domain’s sentence and code when a move is refused, as text', async () => {
    mountOwnerMoves({
      refuse: {
        status: 409,
        code: 'lessons.move_refused',
        title: 'A closed lesson cannot move: closed is <b>final</b>.',
      },
    });
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Dismiss' }));
    await fill();
    await userEvent.click(submit('Dismiss'));

    expect(
      await screen.findByText(
        'A closed lesson cannot move: closed is <b>final</b>. (lessons.move_refused) Nothing was changed.',
      ),
    ).toBeInTheDocument();
    expect(password()).toHaveValue('');
  });

  it('a switched-off answer from the server is said in words', async () => {
    mountOwnerMoves({ refuse: { status: 403, code: 'capabilities.off', title: 'The capability is off.' } });
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Dismiss' }));
    await fill();
    await userEvent.click(submit('Dismiss'));

    expect(
      await screen.findByText(/This act is switched off. The capability is off. \(capabilities.off\)/),
    ).toBeInTheDocument();
  });

  it('needs a reason and a password before the button is enabled', async () => {
    mountOwnerMoves();
    renderMove('/owner-acts/lesson/1');
    await userEvent.click(await screen.findByRole('radio', { name: 'Dismiss' }));
    expect(submit('Dismiss')).toBeDisabled();
    await userEvent.type(reason(), 'x');
    expect(submit('Dismiss')).toBeDisabled();
    await userEvent.type(password(), 'p');
    expect(submit('Dismiss')).toBeEnabled();
  });
});

describe('Hypothesis moves', () => {
  it('offers approve and reject only: running, judged and any other state are never drawn', async () => {
    const fake = mountOwnerMoves({
      hypothesis: buildHypothesis({ id: 3, nextStates: ['approved', 'rejected', 'running', 'judged'] }),
    });
    renderMove('/owner-acts/hypothesis/3');

    expect(await screen.findByRole('radio', { name: 'Approve' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Reject' })).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(2);
    await userEvent.click(screen.getByRole('radio', { name: 'Approve' }));
    await fill();
    await userEvent.click(submit('Approve'));

    await screen.findByText('Moved to approved.');
    expect(fake.writes[0]).toEqual({
      route: 'hypothesis.move',
      id: '3',
      body: { to: 'approved', reason: 'because I read the numbers', password: 'correct-horse' },
    });
  });

  it('accept says that nothing is applied', async () => {
    mountOwnerMoves({ hypothesis: buildHypothesis({ id: 3, state: 'judged', nextStates: ['accepted', 'rejected'] }) });
    renderMove('/owner-acts/hypothesis/3');
    await userEvent.click(await screen.findByRole('radio', { name: 'Accept' }));

    expect(screen.getByText(/Nothing is applied. Accepting records the verdict/)).toBeInTheDocument();
  });
});

describe('Revert', () => {
  it('says what it will change, and sends the reason and the password only', async () => {
    const fake = mountOwnerMoves({ entry: buildEntry({ id: 5, fromState: 'candidate', toState: 'paper' }) });
    renderMove('/owner-acts/revert/5');

    expect(await screen.findByText(/moved from/)).toHaveTextContent('moved from candidate to paper');
    expect(screen.getByText(/Plans that are already published are not changed/)).toBeInTheDocument();
    await fill();
    await userEvent.click(submit('Revert this move'));

    expect(await screen.findByText(/is back to candidate \(new row 9\)/)).toBeInTheDocument();
    expect(fake.writes[0]).toEqual({
      route: 'changelog.revert',
      id: '5',
      body: { reason: 'because I read the numbers', password: 'correct-horse' },
    });
  });

  it('a row that is not a transition has no form', async () => {
    mountOwnerMoves({ entry: buildEntry({ id: 5, kind: 'designation' }) });
    renderMove('/owner-acts/revert/5');

    expect(await screen.findByText(/Only a cell transition can be reverted/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });

  it('shows every reason when the server refuses', async () => {
    mountOwnerMoves({
      refuse: {
        status: 409,
        code: 'changelog.not_revertible',
        title: 'Row 5 cannot be reverted. It is not the latest row of the cell.',
      },
    });
    renderMove('/owner-acts/revert/5');
    await screen.findByText(/moved from/);
    await fill();
    await userEvent.click(submit('Revert this move'));

    expect(
      await screen.findByText(/It is not the latest row of the cell. \(changelog.not_revertible\)/),
    ).toBeInTheDocument();
  });
});

describe('Autonomy form', () => {
  const renderSwitches = () =>
    renderRoutes([{ path: '/owner-acts', element: <OwnerActsPage /> }], { initialPath: '/owner-acts' });
  const form = () => screen.getByRole('form', { name: 'Change the autonomy settings' });

  it('capability off: no form, no switch to write with', async () => {
    mountOwnerMoves({ off: ['autonomy_form'] });
    renderSwitches();

    expect(await screen.findByText(/autonomy form is switched off/)).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Change the autonomy settings' })).toBeNull();
    expect(screen.queryByRole('switch')).toBeNull();
  });

  it('nothing stored: both switches are OFF, and the order sentence is beside them', async () => {
    mountOwnerMoves();
    renderSwitches();

    await screen.findByRole('form', { name: 'Change the autonomy settings' });
    expect(screen.getByRole('switch', { name: /Automatic approval: OFF/ })).not.toBeChecked();
    expect(screen.getByRole('switch', { name: /Reserve promotion: OFF/ })).not.toBeChecked();
    expect(
      screen.getByText('No setting lets anything place an order; an automatic approval only makes a ticket Approved.'),
    ).toBeInTheDocument();
    expect(within(form()).getByRole('button', { name: 'Save as a new row' })).toBeDisabled();
  });

  it('turning it on is a new row: values, the row it was loaded at, the reason and the password', async () => {
    const fake = mountOwnerMoves();
    renderSwitches();
    await screen.findByRole('form', { name: 'Change the autonomy settings' });
    await userEvent.click(screen.getByRole('switch', { name: /Automatic approval/ }));
    await userEvent.type(within(form()).getByLabelText(/^Reason/), 'try the rule');
    await userEvent.type(within(form()).getByLabelText(/^Your password/), 'correct-horse');
    await userEvent.click(screen.getByRole('button', { name: 'Save as a new row' }));

    expect(await screen.findByText(/Saved as settings row 1/)).toBeInTheDocument();
    expect(fake.writes[0]?.body).toEqual({
      autoApproveTickets: true,
      promoteFromReserve: false,
      minPWin: 0.6,
      maxAutoPerDay: 3,
      expectedSettingsId: 0,
      reason: 'try the rule',
      password: 'correct-horse',
    });
    expect(within(form()).getByLabelText(/^Your password/)).toHaveValue('');
  });

  it('a value out of range keeps the button off', async () => {
    mountOwnerMoves();
    renderSwitches();
    await screen.findByRole('form', { name: 'Change the autonomy settings' });
    await userEvent.clear(screen.getByLabelText('Minimum win-rate bound'));
    await userEvent.type(screen.getByLabelText('Minimum win-rate bound'), '0.2');
    await userEvent.type(within(form()).getByLabelText(/^Reason/), 'x');
    await userEvent.type(within(form()).getByLabelText(/^Your password/), 'p');

    expect(screen.getByRole('button', { name: 'Save as a new row' })).toBeDisabled();
  });

  it('a stale form shows the server code and offers to read the settings again', async () => {
    const fake = mountOwnerMoves({
      autonomy: buildAutonomy({ id: 4 }),
      refuse: { status: 409, code: 'autonomy.stale', title: 'The settings were changed meanwhile; read them again.' },
    });
    renderSwitches();
    await screen.findByRole('form', { name: 'Change the autonomy settings' });
    await userEvent.type(within(form()).getByLabelText(/^Reason/), 'x');
    await userEvent.type(within(form()).getByLabelText(/^Your password/), 'p');
    await userEvent.click(screen.getByRole('button', { name: 'Save as a new row' }));

    expect(await screen.findByText(/\(autonomy.stale\)/)).toBeInTheDocument();
    expect(fake.writes[0]?.body.expectedSettingsId).toBe(4);
    fake.autonomy = buildAutonomy({ id: 5, maxAutoPerDay: 7 });
    await userEvent.click(await screen.findByRole('button', { name: 'Read the settings again' }));
    await waitFor(() => {
      expect(screen.getByLabelText('Daily cap of automatic approvals')).toHaveValue(7);
    });
  });

  it('re-using an old row only fills the fields', async () => {
    const fake = mountOwnerMoves({
      autonomy: buildAutonomy({
        id: 2,
        autoApproveTickets: false,
        maxAutoPerDay: 3,
        history: [
          {
            id: 1,
            hash: 'h',
            autoApproveTickets: true,
            promoteFromReserve: false,
            minPWin: 0.7,
            maxAutoPerDay: 5,
            actor: 'owner:x',
            reason: 'first',
            changedAt: '2026-10-20T04:30:00Z',
          },
        ],
      }),
    });
    renderSwitches();
    await screen.findByRole('form', { name: 'Change the autonomy settings' });
    await userEvent.click(screen.getByRole('button', { name: 'Use the values of row 1' }));

    expect(screen.getByLabelText('Daily cap of automatic approvals')).toHaveValue(5);
    expect(screen.getByRole('switch', { name: /Automatic approval: ON/ })).toBeChecked();
    expect(fake.writes).toHaveLength(0);
  });
});
