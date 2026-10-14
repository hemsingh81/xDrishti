import { http, HttpResponse, type RequestHandler } from 'msw';
import type { Schemas } from '@/shared/api';
import { buildSession } from './fixtures';
import { createStepUpGate, problemResponse } from './server';

type OwnerAccount = Schemas['AccountResponse'];
type AuditEntry = Schemas['AuditEventResponse'];

export function buildOwnerAccount(overrides: Partial<OwnerAccount> = {}): OwnerAccount {
  return {
    email: 'example@invalid.test',
    displayName: 'Test Owner',
    createdAt: '2026-09-01T09:00:00Z',
    passwordChangedAt: '2026-10-01T09:00:00Z',
    ...overrides,
  };
}

export function buildAuditEntry(overrides: Partial<AuditEntry> = {}): AuditEntry {
  return {
    at: '2026-10-14T09:30:00Z',
    event: 'LoginSucceeded',
    email: 'example@invalid.test',
    ip: '192.0.2.10',
    userAgent: 'Mozilla/5.0',
    ...overrides,
  };
}

export const validCurrentPassword = 'correct-password';

export interface AccountFakeOptions {
  account?: OwnerAccount;
  audit?: AuditEntry[];
}

export interface PasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/**
 * The My account endpoints of the API as one stateful stand-in (made-up data): the profile read/save behind the step-up
 * rule (password `correct-password`), the password change (current password `correct-password`) and the audit list.
 * Tests replace single answers with `server.use` where they need a refusal.
 */
export interface AccountFake {
  account: OwnerAccount;
  audit: AuditEntry[];
  gate: ReturnType<typeof createStepUpGate>;
  passwordRequests: PasswordRequest[];
  profileRequests: { displayName: string; email: string }[];
  handlers: RequestHandler[];
}

export function createAccountFake({
  account = buildOwnerAccount(),
  audit = [buildAuditEntry()],
}: AccountFakeOptions = {}) {
  const gate = createStepUpGate();
  const fake: AccountFake = {
    account,
    audit,
    gate,
    passwordRequests: [],
    profileRequests: [],
    handlers: [
      gate.handler,
      http.get('/api/auth/account', () => HttpResponse.json(fake.account)),
      http.get('/api/auth/audit', () => HttpResponse.json(fake.audit)),
      http.post('/api/auth/change-password', async ({ request }) => {
        const body = (await request.json()) as PasswordRequest;
        fake.passwordRequests.push(body);
        if (body.currentPassword !== validCurrentPassword) {
          return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
        }
        fake.account = { ...fake.account, passwordChangedAt: '2026-10-14T10:00:00Z' };
        return new HttpResponse(null, { status: 204 });
      }),
      http.put('/api/auth/profile', async ({ request }) => {
        const required = gate.require();
        if (required) {
          return required;
        }
        const body = (await request.json()) as { displayName: string; email: string };
        fake.profileRequests.push(body);
        fake.account = {
          ...fake.account,
          displayName: body.displayName.trim(),
          email: body.email.trim().toLowerCase(),
        };
        return HttpResponse.json(fake.account);
      }),
      // The session carries the name and e-mail the avatar shows: it follows the account.
      http.get('/api/auth/session', () =>
        HttpResponse.json(buildSession({ user: { email: fake.account.email, displayName: fake.account.displayName } })),
      ),
    ],
  };
  return fake;
}
