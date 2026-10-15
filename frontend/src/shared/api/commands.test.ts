import { http, HttpResponse } from 'msw';
import { generatedCommandTargets } from '@/test/attentionFixtures';
import { problemResponse, server } from '@/test/server';
import { attentionCommands } from './attentionCommands.generated';
import { ApiError, configureApiAuth } from './client';
import { createCommandParser, parseCommand, runCommand } from './commands';

const account = '00000000-0000-4000-8000-000000000001';

describe('parseCommand', () => {
  it('reads "POST /api/path" of a known command', () => {
    expect(parseCommand(`POST /api/accounts/${account}/sync/broker-history`)).toEqual({
      method: 'POST',
      path: `/api/accounts/${account}/sync/broker-history`,
    });
    expect(parseCommand(`POST /api/accounts/${account}/holdings/sync`)).toEqual({
      method: 'POST',
      path: `/api/accounts/${account}/holdings/sync`,
    });
    expect(parseCommand('POST /api/services/holdings-quotes/run')).toEqual({
      method: 'POST',
      path: '/api/services/holdings-quotes/run',
    });
  });

  it('refuses anything else: a read, another origin, a path outside the API, a climb, a query, a blank', () => {
    for (const target of [
      'GET /api/accounts',
      'POST https://example.org/api/accounts',
      'POST //example.org/api/accounts',
      'POST /accounts/1',
      'POST /api/../admin',
      'POST /api/accounts?x=1',
      'POST',
      '/api/accounts',
      '',
      // Only POST, and only the known endpoints.
      'DELETE /api/price-alerts/abc',
      'PUT /api/services/holdings-quotes/run',
      'PATCH /api/services/holdings-quotes/run',
      `GET /api/accounts/${account}/holdings/sync`,
      `PUT /api/accounts/${account}/holdings/sync`,
      `POST /api/accounts/${account}/holdings/sync/extra`,
      `POST /api/accounts/${account}/holdings/preview`,
      'POST /api/accounts/not-a-guid/holdings/sync',
      `POST /api/accounts/${account}/holdings/sync?confirm=true`,
      `POST /api/accounts/${account}/sync/broker-history/extra`,
      'POST /api/accounts/not-a-guid/sync/broker-history',
      'POST /api/services//run',
      // Encoded or hidden climbs and separators, backslashes, doubled slashes, dot-segments, fragments.
      `POST /api/accounts/${account}/%2e%2e/sync/broker-history`,
      `POST /api/accounts/${account}%2fsync/broker-history`,
      'POST /api/services/%2E%2E/run',
      'POST /api/services/a%2fb/run',
      'POST /api/services/a\\b/run',
      '  POST /api/services/./run',
      `POST /api//accounts/${account}/sync/broker-history`,
      `POST /api/accounts/${account}/sync/broker-history#x`,
      'POST /api/services/holdings-quotes/run?x=1',
      'POST /api/services/holdings quotes/run',
    ]) {
      expect(parseCommand(target), target).toBeNull();
    }
  });
});

describe('the commands the API names (the generated list)', () => {
  it('are all accepted, as the POST they are: none is silently dropped', () => {
    const targets = generatedCommandTargets(account);
    expect(targets).toHaveLength(attentionCommands.length);
    expect(targets.length).toBeGreaterThan(0);
    for (const [n, target] of targets.entries()) {
      expect(attentionCommands[n]?.method).toBe('POST');
      expect(parseCommand(target), target).toEqual({ method: 'POST', path: target.replace(/^POST /, '') });
    }
  });

  it('are refused with any other method, however the rest of the target looks', () => {
    for (const method of ['GET', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'post']) {
      for (const target of generatedCommandTargets(account)) {
        const other = `${method} ${target.replace(/^\S+ /, '')}`;
        expect(parseCommand(other), other).toBeNull();
      }
    }
  });

  it('are refused with a bad id, a trailing path, a query or a fragment', () => {
    for (const target of generatedCommandTargets(account)) {
      for (const suffix of ['/extra', '?x=1', '#x', '%2f', '\\']) {
        expect(parseCommand(target + suffix), target + suffix).toBeNull();
      }
    }
    expect(parseCommand(generatedCommandTargets('not-a-guid')[0] ?? '')).toBeNull();
  });
});

describe('createCommandParser', () => {
  const pattern = '^/api/things/[0-9]{1,6}$';
  const parse = createCommandParser([
    { method: 'POST', pattern },
    { method: 'DELETE', pattern: '^/api/things/[0-9]{1,6}/old$' },
  ]);

  it('accepts a command with the method its entry names, not only POST', () => {
    expect(parse('DELETE /api/things/12/old')).toEqual({ method: 'DELETE', path: '/api/things/12/old' });
    expect(parse('POST /api/things/12')).toEqual({ method: 'POST', path: '/api/things/12' });
  });

  it("refuses a method that is not the entry's for that path, and a lower-case one", () => {
    expect(parse('POST /api/things/12/old')).toBeNull();
    expect(parse('DELETE /api/things/12')).toBeNull();
    expect(parse('delete /api/things/12/old')).toBeNull();
    expect(parse('GET /api/things/12')).toBeNull();
  });

  it('refuses everything when the list is empty', () => {
    expect(createCommandParser([])('POST /api/things/12')).toBeNull();
  });
});

describe('runCommand', () => {
  afterEach(() => {
    configureApiAuth(undefined);
  });

  it("calls the endpoint with the session cookie's CSRF token like any other change", async () => {
    configureApiAuth({ getCsrfToken: () => 'csrf-token', onUnauthorized: () => undefined });
    const seen: { method: string; csrf: string | null; path: string }[] = [];
    server.use(
      http.post('/api/accounts/:id/sync/broker-history', ({ request }) => {
        seen.push({
          method: request.method,
          csrf: request.headers.get('X-CSRF-Token'),
          path: new URL(request.url).pathname,
        });
        return HttpResponse.json({ ok: true }, { status: 202 });
      }),
    );

    await runCommand(`POST /api/accounts/${account}/sync/broker-history`);

    expect(seen).toEqual([
      { method: 'POST', csrf: 'csrf-token', path: `/api/accounts/${account}/sync/broker-history` },
    ]);
  });

  it('accepts an answer without a body', async () => {
    server.use(http.post('/api/services/:id/run', () => new HttpResponse(null, { status: 204 })));
    await expect(runCommand('POST /api/services/holdings-quotes/run')).resolves.toBeUndefined();
  });

  it('throws an ApiError with the problem when the API refuses', async () => {
    server.use(
      http.post('/api/accounts/:id/sync/broker-history', () =>
        problemResponse({ title: 'A sync is already running.', code: 'portfolio.sync_running' }, 409),
      ),
    );

    const error = await runCommand(`POST /api/accounts/${account}/sync/broker-history`).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).problem?.code).toBe('portfolio.sync_running');
  });

  it('posts a holdings sync without a body, so the safeguard can answer 409 confirmation_required', async () => {
    const seen: string[] = [];
    server.use(
      http.post('/api/accounts/:id/holdings/sync', async ({ request }) => {
        seen.push(await request.text());
        return problemResponse(
          { title: 'Confirm to continue.', code: 'portfolio.confirmation_required', status: 409 },
          409,
        );
      }),
    );

    const error = await runCommand(`POST /api/accounts/${account}/holdings/sync`).catch((e: unknown) => e);

    expect(seen).toEqual(['']);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).problem?.code).toBe('portfolio.confirmation_required');
  });

  it('sends nothing for a target that is not a command', async () => {
    await expect(runCommand('GET /api/accounts')).rejects.toThrow('This action is not available.');
    await expect(runCommand('DELETE /api/services/holdings-quotes/run')).rejects.toThrow(
      'This action is not available.',
    );
  });
});
