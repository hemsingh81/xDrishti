import type { SystemStatus } from '@/shared/api';

/** Typed test data builders — tests override only what they care about. */
export function buildSystemStatus(overrides: Partial<SystemStatus> = {}): SystemStatus {
  return {
    application: {
      name: 'xd-api',
      version: '0.1.0',
      environment: 'Test',
      instance: 'test',
      startedAt: '2026-10-08T09:00:00Z',
      uptimeSeconds: 3_660,
    },
    database: {
      isConnected: true,
      serverVersion: '18.6',
      timescaleVersion: '2.30.2',
      latestMigration: '20261008153115_InitialPlatform',
      pendingMigrations: 0,
      latencyMs: 1.4,
      error: null,
    },
    services: [
      {
        name: 'xd-worker',
        instance: 'w1',
        version: '0.1.0',
        startedAt: '2026-10-08T09:00:00Z',
        lastSeenAt: new Date().toISOString(),
        isAlive: true,
      },
    ],
    isHealthy: true,
    checkedAt: '2026-10-08T10:01:00Z',
    ...overrides,
  };
}
