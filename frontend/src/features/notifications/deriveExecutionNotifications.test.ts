import type { ExecutionNotice } from '@/features/execution';
import { pinClock } from '@/test/pinClock';
import { attentionKinds } from './lib/deriveNotifications';
import { deriveExecutionNotifications } from './lib/deriveExecutionNotifications';

pinClock('2026-10-30T04:00:00Z');

const notice = (over: Partial<ExecutionNotice> = {}): ExecutionNotice => ({
  id: 'exec:ticket:t1',
  kind: 'exec_confirm_waiting',
  severity: 'warning',
  text: 'Buy 1 INFY waits for you until 09:35',
  title: 'Order waiting',
  at: '2026-10-30T04:00:00Z',
  actions: [{ label: 'Open', kind: 'route', target: '/execution' }],
  since: '2026-10-30T04:00:00Z',
  resolved: false,
  accountId: null,
  ...over,
});

const table: [string, string, 'error' | 'warn' | 'info'][] = [
  ['exec_confirm_waiting', 'warning', 'warn'],
  ['exec_auto_action', 'info', 'info'],
  ['exec_order_rejected', 'error', 'error'],
  ['exec_protection_missing', 'error', 'error'],
  ['exec_halted', 'error', 'error'],
  ['exec_kill_switch', 'error', 'error'],
  ['exec_demoted', 'error', 'error'],
  ['exec_recon_break', 'error', 'error'],
  ['exec_not_ready', 'warning', 'warn'],
  ['exec_ip_invalid', 'error', 'error'],
  ['exec_cap_near', 'warning', 'warn'],
  ['exec_attended_pause', 'warning', 'warn'],
  ['exec_day_summary', 'info', 'info'],
];

describe('deriveExecutionNotifications', () => {
  it.each(table)('derives %s (%s) as %s', (kind, severity, expected) => {
    const [n] = deriveExecutionNotifications([notice({ kind, severity, id: `exec:x:${kind}` })]);
    expect(n).toMatchObject({ kind, severity: expected, id: `exec:x:${kind}`, resolved: false });
    expect(n?.at).toBe(Date.parse('2026-10-30T04:00:00Z'));
  });

  it('keeps the id with its cause: a changed situation is a new item', () => {
    const ids = deriveExecutionNotifications([notice({ id: 'exec:halt:3' }), notice({ id: 'exec:halt:4' })]).map(
      (n) => n.id,
    );
    expect(ids).toEqual(['exec:halt:3', 'exec:halt:4']);
  });

  it('keeps a resolved item resolved (listed, never counted) such as an automatic action', () => {
    const [n] = deriveExecutionNotifications([notice({ kind: 'exec_auto_action', severity: 'info', resolved: true })]);
    expect(n?.resolved).toBe(true);
  });

  it('keeps only routes inside the app, at most two, and never a command', () => {
    const [n] = deriveExecutionNotifications([
      notice({
        actions: [
          { label: 'Elsewhere', kind: 'route', target: '//evil.example' },
          { label: 'Run', kind: 'command', target: 'POST /api/execution/halts' },
          { label: 'Open', kind: 'route', target: '/execution?tab=orders' },
          { label: 'Also', kind: 'route', target: '/execution?tab=modes' },
          { label: 'Third', kind: 'route', target: '/execution' },
        ],
      }),
    ]);
    expect(n?.actions.map((a) => a.label)).toEqual(['Open', 'Also']);
    expect(n?.href).toBe('/execution?tab=orders');
  });

  it('falls back to the screen when the notice names no place, and drops an unknown kind', () => {
    const out = deriveExecutionNotifications([
      notice({ actions: [], kind: 'exec_recon_break', severity: 'error' }),
      notice({ id: 'exec:y', kind: 'exec_from_the_future' }),
    ]);
    expect(out).toHaveLength(1);
    expect(out[0]?.href).toBe('/execution?tab=positions');
  });

  it('pops up only the error kinds, once (attention kinds); the rest wait in the bell', () => {
    for (const [kind, , sev] of table) expect(attentionKinds.has(kind as never)).toBe(sev === 'error');
  });
});
