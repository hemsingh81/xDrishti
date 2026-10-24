import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { TodayPage } from '@/features/today';
import { pinClock } from '@/test/pinClock';
import {
  accountA,
  beforeCutoff,
  buildOutcome,
  buildPlan,
  buildReview,
  buildTicket,
  cleanTicket,
  planItems,
} from '@/test/planFixtures';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { PlansPage } from './components/PlansPage';

pinClock(beforeCutoff);

const wording = 'Approved for watching: no order is placed and none exists.';
const detailPlan = () => buildPlan({ items: planItems() });

interface Options {
  review?: ReturnType<typeof buildReview>;
  autoApprove?: boolean;
}

/** Mounts the screen over a published plan; returns the bodies the tests posted. */
function mount(opts: Options = {}) {
  const posted: Record<string, unknown> = {};
  server.use(
    http.get('/api/plans', () => HttpResponse.json([buildPlan()])),
    http.get('/api/plans/7', () => HttpResponse.json(detailPlan())),
    http.get('/api/plans/7/review', () => HttpResponse.json(opts.review ?? buildReview())),
    http.get('/api/plans/7/suggestions', () =>
      HttpResponse.json([
        {
          id: 5,
          kind: 'open_book_unavailable',
          subject: 'plan',
          text: 'Open book is unavailable.',
          createdAt: '2026-10-25T16:00:00Z',
          seen: false,
        },
      ]),
    ),
    http.get('/api/plans/7/grade', () =>
      HttpResponse.json({
        planId: 7,
        forDate: '2026-10-26',
        basis: 'simulated on plan prices',
        tickets: [],
        pending: 3,
        report: {},
      }),
    ),
    http.get('/api/plans/7/journal', () =>
      HttpResponse.json({
        planId: 7,
        forDate: '2026-10-26',
        fillBasis: 'realised fills, not simulated',
        planBasis: 'simulated on plan prices',
        entries: [],
        discretionary: [],
        ticketsWithoutFills: 0,
      }),
    ),
    http.get('/api/learning/digests', () =>
      HttpResponse.json([
        {
          id: 1,
          isoWeek: '2026-W43',
          weekStart: '2026-10-19',
          weekEnd: '2026-10-25',
          sections: [{ key: 'cells', title: 'Cells', lines: ['Cell orb is drifting.'] }],
          text: '',
          builtBy: 'rule:weekly',
          createdAt: null,
          repeated: false,
        },
      ]),
    ),
    http.get('/api/autonomy/settings', () =>
      HttpResponse.json({
        id: null,
        hash: null,
        autoApproveTickets: opts.autoApprove ?? false,
        promoteFromReserve: false,
        minPWin: 0.6,
        maxAutoPerDay: 3,
        notice: 'No order is placed in any setting.',
        history: [],
      }),
    ),
  );
  return posted;
}

function capture(path: string, body: object, status = 200) {
  const seen: { body?: unknown } = {};
  server.use(
    http.post(path, async ({ request }) => {
      seen.body = await request.json().catch(() => undefined);
      return status === 200 ? HttpResponse.json(body) : problemResponse(body, status);
    }),
  );
  return seen;
}

describe('Plan review screen', () => {
  it('says there is no plan yet', async () => {
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText(/No plan yet/)).toBeInTheDocument();
    expect(screen.getByText(/places no order/)).toBeInTheDocument();
  });

  it('shows an error when the plans cannot be read', async () => {
    server.use(http.get('/api/plans', () => problemResponse({ title: 'Plans broke.' }, 500)));
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText('Plans broke.')).toBeInTheDocument();
  });

  it('lists the plan with its state and basis flags as text', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    const list = await screen.findByRole('list', { name: 'Plans by date' });
    expect(within(list).getByText('State: Published')).toBeInTheDocument();
    expect(within(list).getByText('Provisional')).toBeInTheDocument();
    expect(within(list).getByText('Market gate unavailable')).toBeInTheDocument();
    expect(within(list).getByText('Open book unavailable')).toBeInTheDocument();
  });

  it('shows the groups, the evidence, the paper mode, the cutoff countdown and the lapse default', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    expect(await screen.findByRole('heading', { name: 'Primary (1)' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Reserve (1)' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Rejected (1)' })).toBeInTheDocument();
    expect(screen.getAllByText(/about 2\.1 years, replayed history, modelled fills, no hold-out/)).toHaveLength(3);
    expect(screen.getAllByText(/cell orb\|15m\|long, 142 trades/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/\(paper\)/).length).toBeGreaterThan(0);
    expect(screen.getByText(/liquidity_too_low/)).toBeInTheDocument();
    expect(screen.getByText(/40m 00s left/)).toBeInTheDocument();
    expect(screen.getAllByText(/Undecided tickets lapse at the cutoff/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Automatic approval: OFF/)).toBeInTheDocument();
    expect(screen.getAllByText(/data_quality/, { selector: 'strong' }).length).toBeGreaterThan(0);
  });

  it('shows an automatic approval that is ON as read only', async () => {
    mount({ autoApprove: true });
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText(/Automatic approval: ON \(set elsewhere/)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('disables Approve with the reason while a check needs confirmation', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(card).getByRole('button', { name: 'Approve' })).toBeDisabled();
    expect(within(card).getByText(/Approve is off: Checks need your confirmation: data_quality/)).toBeInTheDocument();
    expect(within(card).getByText(wording)).toBeInTheDocument();
  });

  it('confirms despite with the exact codes per account and the reason, and shows the ignored list', async () => {
    mount();
    const seen = capture(
      '/api/plans/7/items/11/approve',
      buildOutcome({
        overriddenCodes: ['data_quality'],
        ignored: ['account B: not sized for it'],
      }),
    );
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: /Confirm despite/ }));
    const dialog = await screen.findByRole('dialog', { name: /Confirm despite: AAA/ });
    expect(within(dialog).getByText(/data_quality/)).toBeInTheDocument();
    expect(within(dialog).getByText(wording)).toBeInTheDocument();
    const submit = within(dialog).getByRole('button', { name: 'Approve despite these checks' });
    expect(submit).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Data check is not wired yet');
    await userEvent.click(submit);
    await waitFor(() => {
      expect(seen.body).toEqual({
        reason: null,
        confirmations: [{ accountId: accountA, codes: ['data_quality'], reason: 'Data check is not wired yet' }],
      });
    });
    expect(await screen.findByText(/AAA: Approved for watching/)).toBeInTheDocument();
    expect(screen.getByText(/Approved despite: data_quality/)).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Ignored confirmations' })).toHaveTextContent(
      'account B: not sized for it',
    );
  });

  it('does not offer Confirm despite for a check that cannot be overridden', async () => {
    const t = buildTicket();
    t.accounts[0]?.checks.push({ code: 'account_gate', outcome: 'fail', standing: 'refused', detail: 'not paper' });
    mount({ review: buildReview({ tickets: [t] }) });
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(card).queryByRole('button', { name: /Confirm despite/ })).not.toBeInTheDocument();
  });

  it('approves a clean ticket from the server answer', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    const seen = capture('/api/plans/7/items/11/approve', buildOutcome());
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Approve' }));
    await waitFor(() => {
      expect(seen.body).toEqual({ reason: null, confirmations: [] });
    });
    expect(await screen.findByText(`AAA: ${wording}`)).toBeInTheDocument();
  });

  it('lowers the size with the quantities and the reason', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    const seen = capture('/api/plans/7/items/11/modify', buildOutcome({ state: 'modified', notice: '' }));
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Modify' }));
    const dialog = await screen.findByRole('dialog', { name: /Lower the size: AAA/ });
    expect(within(dialog).getByText(wording)).toBeInTheDocument();
    const qty = within(dialog).getByLabelText(/Quantity for/);
    await userEvent.clear(qty);
    await userEvent.type(qty, '12');
    expect(within(dialog).getByRole('button', { name: 'Lower the size' })).toBeDisabled();
    await userEvent.clear(qty);
    await userEvent.type(qty, '4');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Smaller risk');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Lower the size' }));
    await waitFor(() => {
      expect(seen.body).toEqual({ reason: 'Smaller risk', quantities: [{ accountId: accountA, quantity: 4 }] });
    });
    expect(await screen.findByText(/AAA: size lowered/)).toBeInTheDocument();
  });

  it('rejects a ticket with a reason', async () => {
    mount();
    const seen = capture('/api/plans/7/items/11/reject', buildOutcome({ state: 'rejected', notice: '' }));
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Reject' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reject AAA' });
    expect(within(dialog).getByText(wording)).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Not convinced');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reject ticket' }));
    await waitFor(() => {
      expect(seen.body).toEqual({ reason: 'Not convinced' });
    });
    expect(await screen.findByText(/AAA: rejected/)).toBeInTheDocument();
  });

  it('states how many tickets approve-all takes and that reserve is not included', async () => {
    const second = cleanTicket({ itemId: 14, symbol: 'DDD' });
    mount({
      review: buildReview({
        tickets: [cleanTicket(), second, buildTicket({ itemId: 12, group: 'reserve', symbol: 'BBB' })],
      }),
    });
    const seen = capture('/api/plans/7/approve-all', {
      planId: 7,
      approved: [11, 14],
      skipped: [{ itemId: 12, symbol: 'BBB', reason: 'reserve' }],
      notice: wording,
    });
    renderWithProviders(<PlansPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Approve all' }));
    const dialog = await screen.findByRole('dialog', { name: 'Approve all' });
    expect(within(dialog).getByText(/exactly 2 primary ticket\(s\)/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Reserve tickets are not included/)).toBeInTheDocument();
    expect(within(dialog).getByText(wording)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Approve 2 ticket(s)' }));
    expect(await screen.findByText(/Approved 2 ticket\(s\)/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('list', { name: 'Skipped tickets' })).toHaveTextContent('BBB: reserve');
    expect(seen.body).toBeUndefined();
  });

  it('keeps Approve all off when no ticket is clean', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    expect(await screen.findByRole('button', { name: 'Approve all' })).toBeDisabled();
    expect(screen.getByText(/0 primary ticket\(s\) have every check passed/)).toBeInTheDocument();
  });

  it('says plainly that the cutoff has passed (409 plans.closed)', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    capture('/api/plans/7/items/11/approve', { title: 'Closed', code: 'plans.closed' }, 409);
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Approve' }));
    expect(await screen.findByText(/The review cutoff has passed/)).toBeInTheDocument();
  });

  it('says a stale decision is already decided (409 plans.stale)', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    capture('/api/plans/7/items/11/approve', { title: 'Stale', code: 'plans.stale' }, 409);
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Approve' }));
    expect(await screen.findByText(/already decided or changed/)).toBeInTheDocument();
  });

  it.each([
    [401, 'auth.session_expired', /Your session has ended/],
    [403, 'auth.csrf_invalid', /The request was refused/],
  ])('explains a %i answer in words', async (status, code, message) => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    capture('/api/plans/7/items/11/approve', { title: 'No', code }, status);
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Approve' }));
    expect(await screen.findByText(message)).toBeInTheDocument();
  });

  it('closes the decisions after the cutoff but keeps the reason visible', async () => {
    mount({ review: buildReview({ open: false, tickets: [cleanTicket()] }) });
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(card).getByRole('button', { name: 'Approve' })).toBeDisabled();
    expect(within(card).getByText(/Decisions are closed/)).toBeInTheDocument();
  });

  it('marks a suggestion as seen', async () => {
    mount();
    const seen: { called: boolean } = { called: false };
    server.use(
      http.post('/api/plans/7/suggestions/5/seen', () => {
        seen.called = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderWithProviders(<PlansPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Mark suggestion 5 as seen' }));
    await waitFor(() => {
      expect(seen.called).toBe(true);
    });
  });

  it('labels the grade, the journal and lists the digests, read only', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText(/simulated on plan prices\. 3 ticket\(s\) not graded yet/)).toBeInTheDocument();
    expect(await screen.findByText(/realised fills, not simulated/)).toBeInTheDocument();
    expect(await screen.findByText(/2026-W43/)).toBeInTheDocument();
  });

  it('shows plain text from the server without interpreting it', async () => {
    const item = { ...planItems()[0], reasons: ['<img src=x onerror=alert(1)>'] } as ReturnType<
      typeof planItems
    >[number];
    server.use(
      http.get('/api/plans', () => HttpResponse.json([buildPlan()])),
      http.get('/api/plans/7', () => HttpResponse.json(buildPlan({ items: [item] }))),
      http.get('/api/plans/7/review', () => HttpResponse.json(buildReview())),
      http.get('/api/plans/7/suggestions', () => HttpResponse.json([])),
      http.get('/api/plans/7/grade', () =>
        HttpResponse.json({ planId: 7, forDate: '', basis: 'b', tickets: [], pending: 0, report: {} }),
      ),
      http.get('/api/plans/7/journal', () =>
        HttpResponse.json({
          planId: 7,
          forDate: '',
          fillBasis: 'f',
          planBasis: 'p',
          entries: [],
          discretionary: [],
          ticketsWithoutFills: 0,
        }),
      ),
      http.get('/api/learning/digests', () => HttpResponse.json([])),
      http.get('/api/autonomy/settings', () => HttpResponse.json({ autoApproveTickets: false })),
    );
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText(/<img src=x onerror=alert\(1\)>/)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });
});

describe('Today plan tile', () => {
  it('shows the state, the counts and the cutoff', async () => {
    server.use(http.get('/api/plans', () => HttpResponse.json([buildPlan()])));
    renderWithProviders(<TodayPage />);
    expect(await screen.findByText(/Published\. 1 primary, 1 reserve, 1 rejected/)).toBeInTheDocument();
    expect(screen.getByText(/Review cutoff .*09:10.* IST/)).toBeInTheDocument();
    expect(screen.getByText(/No order is placed/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Plan review' })).toHaveAttribute('href', '/plans');
  });

  it('says there is no plan yet', async () => {
    renderWithProviders(<TodayPage />);
    expect(await screen.findByText('No plan yet.')).toBeInTheDocument();
  });

  it('shows an error when the plans cannot be read', async () => {
    server.use(http.get('/api/plans', () => problemResponse({ title: 'Plans broke.' }, 500)));
    renderWithProviders(<TodayPage />);
    expect(await screen.findByText('Plans broke.')).toBeInTheDocument();
  });
});
