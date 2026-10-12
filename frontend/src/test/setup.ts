import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { installFakeIntersectionObserver } from './intersection';
import { server } from './server';

// Flows such as "save → password dialog → retry → toast" pass through several MUI transitions; under the parallel
// full suite they can take longer than the 1 s default, so async queries (findBy*, waitFor) wait up to 5 s.
configure({ asyncUtilTimeout: 5000 });

installFakeIntersectionObserver();

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
