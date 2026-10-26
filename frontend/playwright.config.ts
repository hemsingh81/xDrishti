import { defineConfig } from '@playwright/test';

// P8-16: the manual browser runner (`npm run test:browser`). Not part of scripts/check.sh. It drives the installed Chrome
// (nothing is downloaded) over `vite preview` of the production build, which binds 127.0.0.1 only.
const port = 4173;

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  tsconfig: './tsconfig.e2e.json',
  timeout: 90_000,
  workers: 4,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    channel: 'chrome',
    headless: true,
    serviceWorkers: 'block',
  },
  webServer: {
    command: `npm run build && npx vite preview --host 127.0.0.1 --port ${port} --strictPort`,
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: true,
    timeout: 180_000,
  },
});
