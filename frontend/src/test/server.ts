import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { buildSystemStatus } from './fixtures';

/** Default API mocks (network level). Override per test with server.use(...). */
export const handlers = [http.get('/api/system/status', () => HttpResponse.json(buildSystemStatus()))];

export const server = setupServer(...handlers);
