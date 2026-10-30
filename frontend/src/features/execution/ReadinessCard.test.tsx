import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './index';

const account = (over: Record<string, unknown> = {}) => ({
  accountId: '11111111-1111-4111-8111-111111111111',
  name: 'Test Dhan Main',
  state: 'ready',
  checks: [
    { code: 'connected', outcome: 'pass', text: 'Dhan is connected', ageSeconds: 5 },
    { code: 'token_live', outcome: 'fail', text: 'The token was refused', ageSeconds: 5 },
    { code: 'static_ip_ok', outcome: 'unavailable', text: 'The IP could not be read', ageSeconds: null },
  ],
  checkedAt: '2026-10-30T03:40:00Z',
  tokenExpiresAt: '2026-10-30T11:30:00Z',
  readyAt: null,
  ipModifyAfter: '2026-11-06',
  attestation: null,
  lastRecheck: null,
  dayStates: [],
  sendsPerSecondMax: null,
  ...over,
});

const readiness = (accounts: unknown[]) =>
  http.get('/api/execution/readiness', () => HttpResponse.json({ date: '2026-10-30', accounts }));

describe('Execution readiness', () => {
  beforeEach(() => {
    server.use(http.get('/api/plans', () => HttpResponse.json([{ id: 1, state: 'approved' }])));
  });

  it('shows the state as a word with the checks, token end and IP date', async () => {
    server.use(readiness([account()]));
    renderWithProviders(<ExecutionPage />);

    expect(await screen.findByRole('heading', { name: 'Execution: Ready' })).toBeInTheDocument();
    const checks = within(screen.getByRole('list', { name: 'Checks' }));
    expect(checks.getByText('Dhan is connected')).toBeInTheDocument();
    expect(checks.getByText('Pass')).toBeInTheDocument();
    expect(checks.getByText('Fail')).toBeInTheDocument();
    expect(checks.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.getByText(/Token ends:/)).toBeInTheDocument();
    expect(screen.getByText(/IP can be changed from/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Connect Dhan' })).toHaveAttribute(
      'href',
      '/accounts?account=11111111-1111-4111-8111-111111111111&tab=connection',
    );
  });

  it('says not ready and unavailable in words', async () => {
    server.use(
      readiness([
        account({ state: 'not_ready' }),
        account({ accountId: '22222222-2222-4222-8222-222222222222', name: 'Other', state: 'unavailable', checks: [] }),
      ]),
    );
    renderWithProviders(<ExecutionPage />);

    expect(await screen.findByRole('heading', { name: 'Execution: Not ready' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Execution: Unavailable' })).toBeInTheDocument();
    expect(screen.getByText(/^No checks recorded/)).toBeInTheDocument();
  });

  it('shows the morning checklist with Unlock today not yet available', async () => {
    server.use(readiness([account()]));
    renderWithProviders(<ExecutionPage />);

    const list = within(await screen.findByRole('region', { name: 'Morning checklist' }));
    expect(list.getByText('1. Connect Dhan')).toBeInTheDocument();
    expect(list.getByText('Not yet available (P12-08)')).toBeInTheDocument();
    expect(await list.findByText('3. Plan approved')).toBeInTheDocument();
    expect(list.getByText('4. Feed connected')).toBeInTheDocument();
  });

  it('asks for a check now', async () => {
    let asked = 0;
    server.use(
      readiness([account()]),
      http.post('/api/execution/readiness/checks', () => {
        asked += 1;
        return HttpResponse.json({ commandId: '33333333-3333-4333-8333-333333333333' });
      }),
    );
    renderWithProviders(<ExecutionPage />);

    await userEvent.click(await screen.findByRole('button', { name: 'Check now' }));
    expect(await screen.findByText(/Check requested/)).toBeInTheDocument();
    expect(asked).toBe(1);
  });

  it('shows loading, then an error when the read fails', async () => {
    server.use(http.get('/api/execution/readiness', () => HttpResponse.json({ title: 'boom' }, { status: 500 })));
    renderWithProviders(<ExecutionPage />);

    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('rejects an invalid address and sends nothing', async () => {
    let sent = 0;
    server.use(
      readiness([account()]),
      http.post('/api/execution/ip-attestations', () => {
        sent += 1;
        return HttpResponse.json({});
      }),
    );
    renderWithProviders(<ExecutionPage />);

    await userEvent.type(await screen.findByLabelText(/Static IP address/), '999.1.1');
    await userEvent.click(screen.getByRole('button', { name: 'Record attestation' }));
    expect(await screen.findByText('Enter a valid IPv4 or IPv6 address.')).toBeInTheDocument();
    expect(sent).toBe(0);
  });

  it('records an attestation and shows the failure of a refused one', async () => {
    let body: unknown;
    server.use(
      readiness([account()]),
      http.post('/api/execution/ip-attestations', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ id: 1 });
      }),
    );
    renderWithProviders(<ExecutionPage />);

    await userEvent.type(await screen.findByLabelText(/Static IP address/), '203.0.113.7');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Orders Allowed is true at Dhan' }));
    await userEvent.click(screen.getByRole('button', { name: 'Record attestation' }));
    expect(await screen.findByText('Attestation recorded.')).toBeInTheDocument();
    expect(body).toMatchObject({ ip: '203.0.113.7', ordersAllowed: true });

    server.use(
      http.post('/api/execution/ip-attestations', () =>
        HttpResponse.json({ title: 'Refused', code: 'execution.values_invalid' }, { status: 422 }),
      ),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Record attestation' }));
    expect(await screen.findAllByRole('alert')).not.toHaveLength(0);
  });

  it('lists the five runbook steps', async () => {
    server.use(readiness([account()]));
    renderWithProviders(<ExecutionPage />);
    const steps = within(await screen.findByRole('region', { name: 'IP runbook' }));
    expect(steps.getAllByRole('listitem')).toHaveLength(5);
  });
});
