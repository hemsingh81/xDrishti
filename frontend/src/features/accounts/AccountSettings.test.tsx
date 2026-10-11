import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildBucket, buildBuckets, buildPortfolioSaved, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';
import { istDayIso } from '@/shared/lib/format';

const addDays = (iso: string, days: number) =>
  new Date(Date.parse(`${iso}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);

function renderTab(tab: string, accountId: string = ids.main) {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], {
    initialPath: `/accounts?account=${accountId}&tab=${tab}`,
  });
}

async function confirmPassword() {
  const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
  await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
}

describe('Connection tab', () => {
  it('shows the broker, the masked client id and which secrets are stored', async () => {
    renderTab('connection');

    const section = await screen.findByRole('region', { name: 'Broker app' });
    expect(within(section).getByText('Dhan')).toBeInTheDocument();
    expect(within(section).getByText('TEST••90')).toBeInTheDocument();
    expect(within(section).getByText('API key: set · updated 9 Oct 2026')).toBeInTheDocument();
    expect(within(section).getByText('API secret: set · updated 9 Oct 2026')).toBeInTheDocument();
    expect(within(section).getByText('API key valid until 9 Oct 2027')).toBeInTheDocument();
    expect(within(section).queryByText(/Expires in/)).not.toBeInTheDocument();
  });

  it('warns when the Dhan API key expires within 30 days, and when it has expired', async () => {
    let expiresOn = addDays(istDayIso(), 12);
    server.use(
      http.get('/api/accounts/:id', () => HttpResponse.json(buildAccountDetail({ apiKeyExpiresOn: expiresOn }))),
    );
    const { unmount } = renderTab('connection');

    expect(await screen.findByText('Expires in 12 days — generate a new key')).toBeInTheDocument();
    unmount();

    expiresOn = addDays(istDayIso(), -1);
    renderTab('connection');
    expect(await screen.findByText('Expired — generate a new key')).toBeInTheDocument();
    expect(screen.getByText(/^API key expired on/)).toBeInTheDocument();
  });

  it('shows the setup steps and redirect URL on request', async () => {
    renderTab('connection');

    const toggle = await screen.findByRole('button', { name: 'Set up the Dhan app' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('http://localhost:8080/api/brokers/dhan/callback')).not.toBeInTheDocument();
    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('http://localhost:8080/api/brokers/dhan/callback')).toBeInTheDocument();
    expect(screen.getByText(/Access DhanHQ APIs/)).toBeInTheDocument();
  });

  it('replaces only the typed secret after the password dialog and never pre-fills the fields', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/secrets', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail({ updatedAt: '2026-10-11T09:00:00Z' }));
      }),
    );
    renderTab('connection');

    await userEvent.click(await screen.findByRole('button', { name: 'Replace secrets' }));
    const dialog = await screen.findByRole('dialog', { name: 'Replace broker secrets' });
    const key = within(dialog).getByLabelText('New API key (app_id)');
    expect(key).toHaveValue('');
    expect(key).toHaveAttribute('type', 'password');
    expect(within(dialog).getByLabelText('New API secret (app_secret)')).toHaveValue('');
    expect(within(dialog).getByLabelText('API key generated on')).toHaveValue('2026-10-09');

    await userEvent.type(within(dialog).getByLabelText('New API secret (app_secret)'), 'fake-new-secret');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Replace secrets' }));
    await confirmPassword();

    expect(await screen.findByText('Broker secrets replaced.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Replace broker secrets' })).not.toBeInTheDocument();
    expect(bodies).toEqual([{ apiKey: null, apiSecret: 'fake-new-secret', apiKeyIssuedOn: null }]);
    expect(document.body).not.toHaveTextContent('fake-new-secret');
  });

  it('dates a new Dhan key today and needs something to replace', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/accounts/:id/secrets', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderTab('connection');
    await userEvent.click(await screen.findByRole('button', { name: 'Replace secrets' }));
    const dialog = await screen.findByRole('dialog', { name: 'Replace broker secrets' });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Replace secrets' }));
    expect(await within(dialog).findByText('Enter a new API key, a new API secret, or both.')).toBeInTheDocument();
    expect(bodies).toHaveLength(0);

    await userEvent.type(within(dialog).getByLabelText('New API key (app_id)'), 'fake-new-key');
    expect(within(dialog).getByLabelText('API key generated on')).toHaveValue(istDayIso());
    await userEvent.click(within(dialog).getByRole('button', { name: 'Replace secrets' }));

    await waitFor(() => {
      expect(bodies).toEqual([{ apiKey: 'fake-new-key', apiSecret: null, apiKeyIssuedOn: istDayIso() }]);
    });
  });

  it('shows a secret the server refused next to its field', async () => {
    server.use(
      http.put('/api/accounts/:id/secrets', () =>
        problemResponse(
          { title: 'Invalid', code: 'accounts.invalid', errors: { apiKey: ['The API key is too long.'] } },
          400,
        ),
      ),
    );
    renderTab('connection');
    await userEvent.click(await screen.findByRole('button', { name: 'Replace secrets' }));
    const dialog = await screen.findByRole('dialog', { name: 'Replace broker secrets' });

    await userEvent.type(within(dialog).getByLabelText('New API key (app_id)'), 'x');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Replace secrets' }));

    expect(await within(dialog).findByText('The API key is too long.')).toBeInTheDocument();
  });

  it('keeps the client id unless a new one is typed; the broker is not sent', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/connection', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail({ name: 'Renamed' }));
      }),
    );
    renderTab('connection');

    const clientId = await screen.findByLabelText('Client id');
    expect(clientId).toHaveValue('');
    expect(clientId).toHaveAccessibleDescription('Current: TEST••90. Leave blank to keep it.');
    const name = screen.getByLabelText(/^Display name/);
    await userEvent.clear(name);
    await userEvent.type(name, 'Renamed');
    await userEvent.click(screen.getByRole('button', { name: 'Save connection' }));
    await confirmPassword();

    expect(await screen.findByText('Connection details saved.')).toBeInTheDocument();
    expect(bodies).toEqual([{ name: 'Renamed', clientId: null, color: '#5b8def', notes: null }]);
  });

  it('sends a new client id only when one was typed', async () => {
    const bodies: { clientId: string | null }[] = [];
    server.use(
      http.put('/api/accounts/:id/connection', async ({ request }) => {
        bodies.push((await request.json()) as { clientId: string | null });
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderTab('connection');

    await userEvent.type(await screen.findByLabelText('Client id'), 'FRESH9999');
    await userEvent.click(screen.getByRole('button', { name: 'Save connection' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]?.clientId).toBe('FRESH9999');
  });

  it('removes an account after confirmation and the password dialog', async () => {
    const gate = createStepUpGate();
    const removed: string[] = [];
    server.use(
      gate.handler,
      http.delete('/api/accounts/:id', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        removed.push(String(params.id));
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { router } = renderTab('connection', ids.swing);

    await userEvent.click(await screen.findByRole('button', { name: 'Remove account' }));
    const confirm = await screen.findByRole('dialog', { name: 'Remove Test Dhan Swing?' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Remove account' }));
    await confirmPassword();

    expect(await screen.findByText('Test Dhan Swing was removed.')).toBeInTheDocument();
    expect(removed).toEqual([ids.swing]);
    expect(router.state.location.search).toBe('');
  });

  it('explains why the primary Data account cannot be removed', async () => {
    server.use(
      http.delete('/api/accounts/:id', () =>
        problemResponse(
          {
            title: 'The primary Data account cannot be removed. Make another account primary first.',
            code: 'accounts.conflict',
          },
          409,
        ),
      ),
    );
    renderTab('connection');

    await userEvent.click(await screen.findByRole('button', { name: 'Remove account' }));
    const confirm = await screen.findByRole('dialog', { name: 'Remove Test Dhan Main?' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Remove account' }));

    expect(await within(confirm).findByRole('alert')).toHaveTextContent('The primary Data account cannot be removed');
  });
});

describe('Portfolio tab', () => {
  const customBucket = buildBucket({ id: '00000000-0000-4000-8000-0000000000b2', name: 'MyLongTerm' });
  const otherDefault = buildBucket({
    id: '00000000-0000-4000-8000-0000000000b3',
    name: 'Test Dhan Swing · Portfolio',
    isAccountDefault: true,
    accountId: ids.swing,
    accountName: 'Test Dhan Swing',
  });

  it('pre-selects the account’s own bucket and offers custom buckets only', async () => {
    server.use(
      http.get('/api/portfolio-buckets', () => HttpResponse.json([...buildBuckets(), otherDefault, customBucket])),
    );
    renderTab('portfolio');

    const select = await screen.findByLabelText(/^Default bucket/);
    expect(select).toHaveDisplayValue('Test Dhan Main · Portfolio (default)');
    expect(
      within(select)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['Test Dhan Main · Portfolio (default)', 'MyLongTerm']);
    expect(screen.queryByRole('option', { name: 'Choose…' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/history/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Import trade/)).not.toBeInTheDocument();
    const firstImport = screen.getByRole('region', { name: 'First import' });
    expect(
      within(firstImport).getByText('Holdings are refreshed automatically; new ones are dated the day they appear.'),
    ).toBeInTheDocument();
    expect(within(firstImport).queryByRole('table')).not.toBeInTheDocument();
  });

  it('lists the buckets with the account’s own default first', async () => {
    server.use(
      http.get('/api/portfolio-buckets', () => HttpResponse.json([...buildBuckets(), otherDefault, customBucket])),
    );
    renderTab('portfolio');

    const list = await screen.findByRole('list', { name: 'Buckets' });
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((i) => i.textContent),
    ).toEqual(['Test Dhan Main · Portfolio (default for this account)', 'MyLongTerm']);
  });

  it('saves the own bucket as an empty string (null would keep the current one) without a date, and no password is asked', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderTab('portfolio');

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Include in portfolio totals' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        defaultBucket: '',
        includeInTotals: false,
        refreshMode: 'off',
        refreshDailyTimeIst: '16:00',
        firstImportDate: '2025-01-01',
      },
    ]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('lets the owner choose a custom bucket', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.get('/api/portfolio-buckets', () => HttpResponse.json([...buildBuckets(), customBucket])),
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderTab('portfolio');

    await userEvent.selectOptions(await screen.findByLabelText(/^Default bucket/), 'MyLongTerm');
    await userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        defaultBucket: 'MyLongTerm',
        includeInTotals: true,
        refreshMode: 'off',
        refreshDailyTimeIst: '16:00',
        firstImportDate: '2025-01-01',
      },
    ]);
  });

  it('offers only Off and Daily, shows the saved daily time and saves switching it off', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.get('/api/accounts/:id', () =>
        HttpResponse.json(
          buildAccountDetail({
            portfolio: { ...buildAccountDetail().portfolio, refreshMode: 'daily', refreshDailyTimeIst: '09:45' },
          }),
        ),
      ),
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderTab('portfolio');

    expect(await screen.findByRole('radio', { name: 'Daily at' })).toBeChecked();
    expect(screen.getByLabelText('Time of day (IST)')).toHaveValue('09:45');
    expect(screen.getAllByRole('radio').map((r) => r.getAttribute('value'))).toEqual(['off', 'daily']);
    expect(screen.queryByLabelText('Interval in minutes')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Off' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        defaultBucket: '',
        includeInTotals: true,
        refreshMode: 'off',
        refreshDailyTimeIst: '09:45',
        firstImportDate: '2025-01-01',
      },
    ]);
  });

  it('refuses a daily refresh without a time before asking the server', async () => {
    let puts = 0;
    server.use(
      http.put('/api/accounts/:id/portfolio', () => {
        puts += 1;
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderTab('portfolio');

    await userEvent.click(await screen.findByRole('radio', { name: 'Daily at' }));
    await userEvent.clear(screen.getByLabelText('Time of day (IST)'));
    await userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Enter a time as HH:MM, for example 16:00.')).toBeInTheDocument();
    expect(screen.getByLabelText('Time of day (IST)')).toBeInvalid();
    expect(puts).toBe(0);
  });

  it('shows the server’s message for an auto refresh field (the account tab uses the bare field names)', async () => {
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'accounts.invalid',
            errors: { refreshDailyTimeIst: ['Use HH:mm, for example 16:00.'] },
          },
          400,
        ),
      ),
    );
    renderTab('portfolio');

    await userEvent.click(await screen.findByRole('radio', { name: 'Daily at' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Use HH:mm, for example 16:00.')).toBeInTheDocument();
  });

  it('shows a refusal for a field the form has no place for', async () => {
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'accounts.invalid',
            errors: { includeInTotals: ['Cannot change right now.'] },
          },
          400,
        ),
      ),
    );
    renderTab('portfolio');

    await userEvent.click(await screen.findByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('includeInTotals: Cannot change right now.')).toBeInTheDocument();
  });

  it('shows a saved custom bucket as selected', async () => {
    server.use(
      http.get('/api/portfolio-buckets', () => HttpResponse.json([...buildBuckets(), customBucket])),
      http.get('/api/accounts/:id', () =>
        HttpResponse.json(
          buildAccountDetail({
            portfolio: {
              enabled: true,
              defaultBucket: 'MyLongTerm',
              autoBucketName: 'Test Dhan Main · Portfolio',
              includeInTotals: true,
              refreshMode: 'off',
              refreshDailyTimeIst: '16:00',
              firstImportDate: '2025-01-01',
              hasHoldings: true,
            },
          }),
        ),
      ),
    );
    renderTab('portfolio');

    expect(await screen.findByLabelText(/^Default bucket/)).toHaveDisplayValue('MyLongTerm');
  });

  it('shows the server message when it refuses another account’s bucket', async () => {
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: { defaultBucket: ['Another account’s default bucket cannot be used.'] },
          },
          400,
        ),
      ),
    );
    renderTab('portfolio');

    await userEvent.click(await screen.findByRole('button', { name: 'Save portfolio settings' }));

    expect(await screen.findByText('Another account’s default bucket cannot be used.')).toBeInTheDocument();
  });

  it('adds a bucket and offers it in the selector and the list', async () => {
    let buckets = buildBuckets();
    server.use(
      http.get('/api/portfolio-buckets', () => HttpResponse.json(buckets)),
      http.post('/api/portfolio-buckets', async ({ request }) => {
        const { name } = (await request.json()) as { name: string };
        const bucket = buildBucket({ name });
        buckets = [...buckets, bucket];
        return HttpResponse.json(bucket, { status: 201 });
      }),
    );
    renderTab('portfolio');

    await userEvent.type(await screen.findByLabelText('New bucket name'), 'Retirement{Enter}');

    expect(await screen.findByText('Bucket “Retirement” added.')).toBeInTheDocument();
    expect(await screen.findByRole('option', { name: 'Retirement' })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Buckets' })).getByText('Retirement')).toBeInTheDocument();
  });

  it('shows the server message for a bucket name it refuses', async () => {
    server.use(
      http.post('/api/portfolio-buckets', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: { name: ["Use 1 to 40 letters, digits, spaces, '_' or '-'."] },
          },
          400,
        ),
      ),
    );
    renderTab('portfolio');

    await userEvent.type(await screen.findByLabelText('New bucket name'), 'a/b');
    await userEvent.click(screen.getByRole('button', { name: 'Add bucket' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("Use 1 to 40 letters, digits, spaces, '_' or '-'.");
  });
});

describe('Data tab', () => {
  it('explains primary and shows the subscription as unknown', async () => {
    renderTab('data');

    expect(await screen.findByText(/all market-data services use this account/)).toBeInTheDocument();
    expect(screen.getByText('Unknown until the broker adapter exists (P1-04)')).toBeInTheDocument();
  });

  it('explains standby', async () => {
    renderTab('data', ids.swing);

    expect(await screen.findByText(/Standby — takes over if the primary fails/)).toBeInTheDocument();
  });

  it('checks the live-feed budget before saving, then saves it', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/accounts/:id/data', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderTab('data');

    const budget = await screen.findByLabelText('Live-feed budget (instruments)');
    await userEvent.clear(budget);
    await userEvent.type(budget, '6000');
    await userEvent.click(screen.getByRole('button', { name: 'Save data settings' }));
    expect(await screen.findByText('Use a whole number from 0 to 5,000.')).toBeInTheDocument();
    expect(bodies).toHaveLength(0);

    await userEvent.clear(budget);
    await userEvent.type(budget, '500');
    await userEvent.click(screen.getByRole('button', { name: 'Save data settings' }));
    expect(await screen.findByText('Data settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([{ liveFeedBudget: 500 }]);
  });
});
