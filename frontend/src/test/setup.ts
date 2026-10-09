import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { server } from './server';

// jsdom resolves relative fetch URLs against this origin.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' }); // any unmocked request fails the test
});
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => {
  server.close();
});
