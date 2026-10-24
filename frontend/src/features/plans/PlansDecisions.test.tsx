import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { sessionQuery } from '@/features/auth';
import { TodayPage } from '@/features/today';
import { pinClock } from '@/test/pinClock';
import {
  beforeCutoff,
  buildItem,
  buildOutcome,
  buildPlan,
  buildReview,
  buildTicket,
  cleanTicket,
} from '@/test/planFixtures';
import { capture, mount, wording } from '@/test/planScreen';
import { renderRoutes, renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { PlansPage } from './components/PlansPage';
import { basisFlags } from './lib/planText';

pinClock(beforeCutoff);

const riskBasis =
  'Proposed, provisional: no order exists. Risk multiplier 0.5 (market gate / equity throttle). Open book unavailable: no primary tickets.';

describe('basis flags', () => {
  it('matches the backend markers exactly', () => {
    expect(basisFlags({ basis: riskBasis, provisional: false })).toEqual({
      provisional: false,
      gateUnavailable: false,
      openBookUnavailable: true,
    });
    expect(basisFlags({ basis: 'x market gate unavailable (vix not known)', provisional: true })).toEqual({
      provisional: true,
      gateUnavailable: true,
      openBookUnavailable: false,
    });
  });

  it('shows no gate chip for a risk-multiplier basis, and always shows the basis line', async () => {
    mount();
    server.use(http.get('/api/plans', () => HttpResponse.json([buildPlan({ basis: riskBasis })])));
    renderWithProviders(<PlansPage />);
    const list = await screen.findByRole('list', { name: 'Plans by date' });
    expect(within(list).getByText('Open book unavailable')).toBeInTheDocument();
    expect(within(list).queryByText('Market gate unavailable')).not.toBeInTheDocument();
    expect(within(list).getByText(/Basis: .*Risk multiplier 0\.5/)).toBeInTheDocument();
  });

  it('shows the basis line on the Today tile', async () => {
    server.use(http.get('/api/plans', () => HttpResponse.json([buildPlan({ basis: riskBasis })])));
    renderWithProviders(<TodayPage />);
    expect(await screen.findByText(/Basis: .*Open book unavailable/)).toBeInTheDocument();
  });
});

describe('decisions', () => {
  it('sends the CSRF token with a plan decision', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    let csrf: string | null = null;
    server.use(
      http.post('/api/plans/7/items/11/approve', ({ request }) => {
        csrf = request.headers.get('X-CSRF-Token');
        return HttpResponse.json(buildOutcome());
      }),
    );
    const { client } = renderWithProviders(<PlansPage />);
    await client.query(sessionQuery);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: 'Approve' }));
    await screen.findByText(`AAA: ${wording}`);
    expect(csrf).toBe('csrf-token-1');
  });

  it('posts once on a double click and disables Approve while pending', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket()] }) });
    let posts = 0;
    server.use(
      http.post('/api/plans/7/items/11/approve', async () => {
        posts += 1;
        await delay(150);
        return HttpResponse.json(buildOutcome());
      }),
    );
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    const approve = within(card).getByRole('button', { name: 'Approve' });
    await userEvent.dblClick(approve);
    await waitFor(() => {
      expect(approve).toBeDisabled();
    });
    await screen.findByText(`AAA: ${wording}`);
    expect(posts).toBe(1);
  });

  it('explains why Approve is off: no checklist, already decided, refused check', async () => {
    const refused = buildTicket({ itemId: 14, symbol: 'DDD' });
    refused.accounts[0]?.checks.push({ code: 'account_gate', outcome: 'fail', standing: 'refused', detail: 'x' });
    mount({
      review: buildReview({
        tickets: [
          buildTicket({ checklistRecorded: false, canApprove: false, accounts: [] }),
          buildTicket({ itemId: 12, symbol: 'BBB', state: 'approved' }),
          refused,
        ],
      }),
    });
    server.use(
      http.get('/api/plans/7', () =>
        HttpResponse.json(
          buildPlan({
            items: [planItemsFor(11, 'AAA'), planItemsFor(12, 'BBB'), planItemsFor(14, 'DDD')],
          }),
        ),
      ),
    );
    renderWithProviders(<PlansPage />);
    const aaa = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(aaa).getByText(/No readable checklist was stored/)).toBeInTheDocument();
    const bbb = screen.getByRole('article', { name: 'BBB BUY primary' });
    expect(within(bbb).getByText(/Approve is off: Already approved/)).toBeInTheDocument();
    const ddd = screen.getByRole('article', { name: 'DDD BUY primary' });
    expect(within(ddd).getByText(/account_gate: this cannot be overridden/)).toBeInTheDocument();
    expect(within(ddd).queryByRole('button', { name: /Confirm despite/ })).not.toBeInTheDocument();
  });

  it('still takes a rejection after the cutoff while the plan is not closed', async () => {
    mount({ review: buildReview({ open: false, state: 'inreview', tickets: [cleanTicket()] }) });
    server.use(
      http.get('/api/plans/7', () =>
        HttpResponse.json(buildPlan({ state: 'inreview', items: [planItemsFor(11, 'AAA')] })),
      ),
    );
    const seen = capture('/api/plans/7/items/11/reject', buildOutcome({ state: 'rejected', notice: '' }));
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(card).getByRole('button', { name: 'Approve' })).toBeDisabled();
    await userEvent.click(within(card).getByRole('button', { name: 'Reject' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reject AAA' });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Too late to like it');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reject ticket' }));
    await waitFor(() => {
      expect(seen.body).toEqual({ reason: 'Too late to like it' });
    });
    expect(await screen.findByText(/AAA: rejected/)).toBeInTheDocument();
  });

  it('keeps the dialog and the typed reason after an error, and says what happened in it', async () => {
    mount();
    capture('/api/plans/7/items/11/approve', { title: 'Stale', code: 'plans.stale' }, 409);
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    await userEvent.click(within(card).getByRole('button', { name: /Confirm despite/ }));
    const dialog = await screen.findByRole('dialog', { name: /Confirm despite: AAA/ });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'My typed reason');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Approve despite these checks' }));
    expect(await within(dialog).findByText(/already decided or changed/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Reason/)).toHaveValue('My typed reason');
  });

  it('says so when approve-all is refused as stale or closed, and when the server approved fewer', async () => {
    mount({ review: buildReview({ tickets: [cleanTicket(), cleanTicket({ itemId: 14, symbol: 'DDD' })] }) });
    capture('/api/plans/7/approve-all', { title: 'Closed', code: 'plans.closed' }, 409);
    renderWithProviders(<PlansPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Approve all' }));
    const dialog = await screen.findByRole('dialog', { name: 'Approve all' });
    expect(within(dialog).getByText(/the server decides which are approved/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Approve 2 ticket(s)' }));
    expect(await within(dialog).findByText(/The review cutoff has passed/)).toBeInTheDocument();

    capture('/api/plans/7/approve-all', {
      planId: 7,
      approved: [11],
      skipped: [{ itemId: 14, symbol: 'DDD', reason: 'plans.stale: already decided' }],
      notice: wording,
    });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Approve 2 ticket(s)' }));
    expect(await screen.findByText(/Approved 1 ticket\(s\)/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('list', { name: 'Skipped tickets' })).toHaveTextContent('DDD: plans.stale');
    });
  });

  it('lists a check as not passed when its outcome is not a pass', async () => {
    const t = cleanTicket();
    t.accounts[0]?.checks.push({ code: 'liquidity2', outcome: 'unavailable', standing: 'passed', detail: 'no data' });
    mount({ review: buildReview({ tickets: [t] }) });
    renderWithProviders(<PlansPage />);
    const card = await screen.findByRole('article', { name: 'AAA BUY primary' });
    expect(within(card).queryByText(/checks passed/)).not.toBeInTheDocument();
    expect(within(card).getByText('liquidity2')).toBeInTheDocument();
  });

  it('says plan not found for an unknown plan id instead of showing another plan', async () => {
    mount();
    renderRoutes([{ path: '/', element: <PlansPage /> }], { initialPath: '/?plan=999' });
    expect(await screen.findByText(/Plan not found: 999/)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /Plan for/ })).not.toBeInTheDocument();
  });
});

const planItemsFor = (id: number, symbol: string) => buildItem({ id, symbol });
