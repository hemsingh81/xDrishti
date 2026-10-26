/// <reference types="vitest/config" />
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The dev server proxies API calls to the containerised stack (xd-proxy on :8080),
// so the browser always talks to one origin — same as production.
const apiTarget = process.env.XD_API_URL ?? 'http://localhost:8080';

const swFiles = [
  'offline.html',
  'manifest.webmanifest',
  'icons/icon-192.png',
  'icons/icon-512.png',
  'icons/maskable-512.png',
  'icons/apple-touch-icon.png',
];

// P8-21: stamps public/sw.js (copied unhashed to dist/) with a hash of the files it caches, so a changed file means a new cache name.
const stampServiceWorker = {
  name: 'xd-stamp-sw',
  apply: 'build' as const,
  closeBundle() {
    const dist = fileURLToPath(new URL('./dist/', import.meta.url));
    const hash = createHash('sha256');
    swFiles.forEach((file) => hash.update(readFileSync(dist + file)));
    const source = readFileSync(`${dist}sw.js`, 'utf8');
    writeFileSync(`${dist}sw.js`, source.replace("'__XD_BUILD__'", `'${hash.digest('hex').slice(0, 10)}'`));
  },
};

export default defineConfig({
  plugins: [react(), stampServiceWorker],
  resolve: {
    alias: [
      { find: '@', replacement: fileURLToPath(new URL('./src', import.meta.url)) },
      // monaco-yaml imports `monaco-editor/esm/vs/...`, which the package's `exports` map does not serve; map it to the files.
      {
        find: /^monaco-editor\/esm\/vs\/(.*)$/,
        replacement: `${fileURLToPath(new URL('./node_modules/monaco-editor/esm/vs', import.meta.url))}/$1`,
      },
    ],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/health': { target: apiTarget, changeOrigin: true },
      // The SignalR hub (WebSocket). xd-api refuses any origin but its own host, so the dev server presents the target as the origin.
      '/hubs': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
        configure: (proxy) => {
          const asTarget = (request: { setHeader: (name: string, value: string) => void }) => {
            request.setHeader('origin', apiTarget);
          };
          proxy.on('proxyReq', asTarget);
          proxy.on('proxyReqWs', asTarget);
        },
      },
    },
  },
  build: {
    target: 'es2023',
    sourcemap: true,
    chunkSizeWarningLimit: 600,
  },
  test: {
    globals: true,
    // The browser runner (e2e/, Playwright) is manual and has its own config.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Long dialog flows (many keystrokes) can pass 5 s when the whole suite runs in parallel on a busy machine.
    testTimeout: 10_000,
    restoreMocks: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/shared/api/schema.d.ts', 'src/main.tsx'],
    },
  },
});
