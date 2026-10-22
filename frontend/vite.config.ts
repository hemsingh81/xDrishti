/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The dev server proxies API calls to the containerised stack (xd-proxy on :8080),
// so the browser always talks to one origin — same as production.
const apiTarget = process.env.XD_API_URL ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
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
