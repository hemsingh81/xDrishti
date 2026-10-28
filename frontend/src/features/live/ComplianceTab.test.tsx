import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';
import { saveProblem, validate } from './lib/complianceForm';
import { ApiError } from '@/shared/api';

pinClock('2026-10-24T06:30:00Z');

type Record = Schemas['ComplianceRecordView'];

const record = (over: Partial<Record> = {}): Record => ({
  id: 1,
  kind: 'sebi_recheck',
  outcome: 'rechecked_no_change',
  decidedOn: '2026-10-24',
  summary: 'Read the circulars; the threshold is unchanged.',
  sources: ['NSE/INVG/73992'],
  staticIpState: null,
  isp: null,
  monthlyCost: null,
  ipVersion: null,
  whitelistedAt: null,
  lockEndsOn: null,
  nextReviewOn: '2027-01-22',
  overdue: false,
  actor: 'owner:me@example.invalid',
  createdAt: '2026-10-24T06:00:00Z',
  ...over,
});

const view = (over: Partial<Schemas['ComplianceView']> = {}): Schemas['ComplianceView'] => ({
  items: [],
  latest: { static_ip: null, sebi_recheck: null, dhan_ip_read_policy: null },
  writesEnabled: true,
  ...over,
});

function mount(data: Schemas['ComplianceView'], onPost?: (body: unknown) => Response | undefined) {
  const posts: unknown[] = [];
  server.use(
    http.get('/api/live-trading/compliance', () => HttpResponse.json(data)),
    http.post('/api/live-trading/compliance', async ({ request }) => {
      const body = await request.json();
      posts.push(body);
      return onPost?.(body) ?? HttpResponse.json(record(), { status: 201 });
    }),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: '/live' });
  return posts;
}

describe('the Compliance tab', () => {
  it('shows every kind as empty with the runbook sentence, and the sentence that says what a record is', async () => {
    mount(view());

    expect(await screen.findByRole('heading', { level: 1, name: 'Live trading' })).toBeInTheDocument();
    expect(await screen.findAllByText('Nothing recorded yet. Start with the runbook, section 3.')).toHaveLength(3);
    expect(
      screen.getByText(
        /This record is your note of what you were told; the system checks nothing at Dhan or the ISP\./,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('docs/research/p11-static-ip-and-sebi-runbook.md')).toBeInTheDocument();
    expect(screen.getByText('No record in the history yet.')).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('shows the newest record of a kind as a card with the outcome in words, the dates and the overdue chip with its word', async () => {
    const overdue = record({ id: 3, outcome: 'rechecked_changed', nextReviewOn: '2026-10-20', overdue: true });
    const whitelisted = record({
      id: 2,
      kind: 'static_ip',
      outcome: 'whitelisted',
      staticIpState: 'whitelisted',
      whitelistedAt: '2026-10-20',
      lockEndsOn: '2026-10-27',
      isp: 'ACT Broadband',
    });
    mount(
      view({
        items: [overdue, whitelisted],
        latest: { static_ip: whitelisted, sebi_recheck: overdue, dhan_ip_read_policy: null },
      }),
    );

    const sebi = await screen.findByRole('region', { name: 'SEBI re-check' });
    expect(within(sebi).getByText('Re-checked, changed')).toBeInTheDocument();
    expect(within(sebi).getByText('Overdue')).toBeInTheDocument();
    expect(within(sebi).getByText(/Next review 20 Oct 2026 \(overdue\)/)).toBeInTheDocument();
    const ip = screen.getByRole('region', { name: 'Static IP' });
    expect(within(ip).getByText('Whitelisted at Dhan')).toBeInTheDocument();
    expect(within(ip).getByText(/lock on the IP ends 27 Oct 2026/)).toBeInTheDocument();
    expect(screen.getAllByText('Nothing recorded yet. Start with the runbook, section 3.')).toHaveLength(1);
    expect(screen.getByRole('table', { name: 'Compliance history' })).toBeInTheDocument();
  });

  it('shows text as text: markup in a note is not rendered', async () => {
    const r = record({ summary: '<img src=x onerror=alert(1)> note', sources: ['https://example.com/a'] });
    mount(view({ items: [r], latest: { static_ip: null, sebi_recheck: r, dhan_ip_read_policy: null } }));

    expect(await screen.findByText(/<img src=x onerror=alert\(1\)> note/)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('holds the New record button back while the deployment gate is closed and says why', async () => {
    mount(view({ writesEnabled: false }));

    expect(await screen.findByText(/Writing records is switched off in this deployment/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New record' })).toBeDisabled();
  });

  it('saves with the password, sends it with the record and clears it, and refreshes the list', async () => {
    const posts = mount(view());
    await userEvent.click(await screen.findByRole('button', { name: 'New record' }));
    const dialog = await screen.findByRole('dialog', { name: 'New compliance record' });
    await userEvent.type(
      within(dialog).getByRole('textbox', { name: /What you were told or read/ }),
      'Read the circulars',
    );
    await userEvent.type(within(dialog).getByRole('textbox', { name: /Sources/ }), 'NSE/INVG/73992');
    await userEvent.type(within(dialog).getByLabelText(/^Your password for this save/), 'correct-horse');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));

    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({
      kind: 'sebi_recheck',
      outcome: 'rechecked_no_change',
      decidedOn: '2026-10-24',
      nextReviewOn: '2027-01-22',
      summary: 'Read the circulars',
      sources: ['NSE/INVG/73992'],
      password: 'correct-horse',
      staticIpState: null,
    });
    expect(await within(dialog).findByText(/Saved\. The record is in the list/)).toBeInTheDocument();
    expect(within(dialog).queryByLabelText(/^Your password/)).toBeNull();
  });

  it('shows the dialog sentence, asks nothing it cannot ask without a password and shows the validation messages before sending', async () => {
    const posts = mount(view());
    await userEvent.click(await screen.findByRole('button', { name: 'New record' }));
    const dialog = await screen.findByRole('dialog', { name: 'New compliance record' });
    expect(within(dialog).getByText(/This record is your note of what you were told/)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));

    expect(await within(dialog).findByText('Write what you were told or read.')).toBeInTheDocument();
    expect(within(dialog).getByText('Type your password.')).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it('a wrong password is an answer in the dialog, the field is cleared and nothing signs the owner out', async () => {
    const posts = mount(view(), () =>
      HttpResponse.json({ title: 'x', code: 'auth.invalid_credentials' }, { status: 401 }),
    );
    await userEvent.click(await screen.findByRole('button', { name: 'New record' }));
    const dialog = await screen.findByRole('dialog', { name: 'New compliance record' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: /What you were told or read/ }), 'note');
    await userEvent.type(within(dialog).getByLabelText(/^Your password for this save/), 'wrong');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));

    expect(await within(dialog).findByText('The password was not accepted. Nothing was saved.')).toBeInTheDocument();
    expect(posts).toHaveLength(1);
    expect(within(dialog).getByLabelText(/^Your password for this save/)).toHaveValue('');
    expect(screen.getByRole('heading', { level: 1, name: 'Live trading', hidden: true })).toBeInTheDocument();
  });

  it('shows the server refusal of an address in the note, naming the field', async () => {
    mount(view(), () =>
      HttpResponse.json(
        {
          title: 'summary looks like an IP address; the record holds no address, client id or token',
          code: 'live.invalid',
        },
        { status: 422 },
      ),
    );
    await userEvent.click(await screen.findByRole('button', { name: 'New record' }));
    const dialog = await screen.findByRole('dialog', { name: 'New compliance record' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: /What you were told or read/ }), 'it said x');
    await userEvent.type(within(dialog).getByLabelText(/^Your password for this save/), 'pw');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));

    expect(
      await within(dialog).findByText(/summary looks like an IP address.*Nothing was saved\./),
    ).toBeInTheDocument();
  });

  it('a static IP record asks for the whitelisting date and sends the provider fields', async () => {
    const posts = mount(view());
    await userEvent.click(await screen.findByRole('button', { name: 'New record' }));
    const dialog = await screen.findByRole('dialog', { name: 'New compliance record' });
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Kind' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Static IP' }));
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'State of the static IP' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Whitelisted at Dhan' }));
    await userEvent.type(within(dialog).getByRole('textbox', { name: /What you were told or read/ }), 'set it');
    await userEvent.type(within(dialog).getByLabelText(/^Your password for this save/), 'pw');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Provider (ISP)' }), 'ACT Broadband');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Cost a month (Rs)' }), '350');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));
    expect(await within(dialog).findByText('Whitelisted needs the date you set it at Dhan.')).toBeInTheDocument();
    expect(posts).toHaveLength(0);

    const date = within(dialog).getByLabelText(/Whitelisted at Dhan on/);
    await userEvent.type(date, '2026-10-20');
    await userEvent.type(within(dialog).getByLabelText(/^Your password for this save/), 'pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save record' }));

    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({
      kind: 'static_ip',
      outcome: 'not_needed_for_manual',
      staticIpState: 'whitelisted',
      isp: 'ACT Broadband',
      monthlyCost: 350,
      whitelistedAt: '2026-10-20',
    });
    expect(JSON.stringify(posts[0])).not.toContain('lockEndsOn');
  });
});

describe('the dialog rules', () => {
  const base = {
    kind: 'sebi_recheck' as const,
    outcome: 'rechecked_no_change',
    decidedOn: '2026-10-24',
    summary: 'ok',
    sources: '',
    staticIpState: 'none',
    isp: '',
    monthlyCost: '',
    ipVersion: '',
    whitelistedAt: '',
    nextReviewOn: '2027-01-22',
  };

  it('accepts the runbook sources and refuses a source of another shape, a future decision and a bad review date', () => {
    expect(
      validate({ ...base, sources: 'NSE/INVG/73992\nhttps://dhanhq.co/docs/v2/authentication/' }, '2026-10-24'),
    ).toEqual({});
    expect(validate({ ...base, sources: 'http://plain.example' }, '2026-10-24').sources).toMatch(/https:\/\//);
    expect(
      validate({ ...base, sources: Array.from({ length: 11 }, () => 'NSE/INVG/73992').join('\n') }, '2026-10-24')
        .sources,
    ).toMatch(/At most 10/);
    expect(validate({ ...base, decidedOn: '2026-10-25' }, '2026-10-24').decidedOn).toMatch(/future/);
    expect(validate({ ...base, nextReviewOn: '2026-10-24' }, '2026-10-24').nextReviewOn).toMatch(/after the decision/);
    expect(validate({ ...base, nextReviewOn: '2027-12-31' }, '2026-10-24').nextReviewOn).toMatch(/400 days/);
    expect(validate({ ...base, summary: '  ' }, '2026-10-24').summary).toBeDefined();
  });

  it('words every status of a refused save and never claims it saved', () => {
    const err = (status: number, code?: string, extra: object = {}) =>
      new ApiError(status, { title: 't', status, ...(code ? { code } : {}), ...extra });
    expect(saveProblem(err(401, 'auth.invalid_credentials'))).toMatch(/password was not accepted/);
    expect(saveProblem(err(403, 'live.capability_off'))).toMatch(/Live__Writes__Enabled/);
    expect(saveProblem(err(422, 'live.invalid'))).toMatch(/Nothing was saved/);
    expect(saveProblem(err(423))).toMatch(/locked/);
    expect(saveProblem(err(429, undefined, { retryAfterSeconds: 30 }))).toMatch(/Try again in/);
    expect(saveProblem(new Error('x'))).toMatch(/Nothing was saved/);
  });
});
