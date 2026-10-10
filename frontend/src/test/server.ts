import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { buildSession, buildSystemStatus } from './fixtures';

/** Default API mocks (network level): a signed-in owner. Override per test with server.use(...). */
export const handlers = [
  http.get('/api/auth/session', () => HttpResponse.json(buildSession())),
  http.get('/api/system/status', () => HttpResponse.json(buildSystemStatus())),
];

/** problem+json response as the API sends it. */
export const problemResponse = (body: object, status: number, headers?: Record<string, string>) =>
  HttpResponse.json(body, { status, headers: { 'Content-Type': 'application/problem+json', ...headers } });

export const server = setupServer(...handlers);
