import { attentionCommands, type PortfolioNotice } from '@/shared/api';
import { buildPortfolioNotice } from './actionCenterFixtures';
import { holdingIds, ids } from './fixtures';

/** The attention items of ADR 0027 as the API sends them (invented accounts and holdings; ids carry their situation). */
const base = (overrides: Partial<PortfolioNotice>): PortfolioNotice =>
  buildPortfolioNotice({ accountId: ids.main, resolved: false, ...overrides });

export const buildReconnectNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:reconnect:${ids.main}:expired:20261014T0930`,
    kind: 'reconnect_dhan',
    severity: 'error',
    title: 'Reconnect Dhan',
    text: 'The Dhan login of Test Dhan Main has expired. Sign in at Dhan again; what was missed is read by itself afterwards.',
    at: '2026-10-14T09:30:00Z',
    since: '2026-10-14T09:30:00Z',
    actions: [{ label: 'Reconnect Dhan', kind: 'route', target: `/accounts?account=${ids.main}&tab=connection` }],
    ...overrides,
  });

export const buildSyncFailingNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:sync:${ids.main}:trades:unavailable`,
    kind: 'sync_failing',
    severity: 'error',
    title: 'Trades are not being read',
    text: 'The trades of Test Dhan Main have not been updated for 2 days: Dhan could not be reached.',
    at: '2026-10-14T05:00:00Z',
    since: '2026-10-14T05:00:00Z',
    actions: [
      { label: 'Retry now', kind: 'command', target: `POST /api/accounts/${ids.main}/sync/broker-history` },
      { label: 'Open', kind: 'route', target: `/accounts?account=${ids.main}&tab=portfolio` },
    ],
    ...overrides,
  });

export const buildReviewNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:review:${ids.main}:3f2a9c`,
    kind: 'holdings_need_review',
    severity: 'warning',
    title: 'Holdings need your review',
    text: '1 holding of Test Dhan Main has lots with a date you chose, so the lots were not rebuilt from the trades (INFY).',
    at: '2026-10-14T11:00:00Z',
    since: '2026-10-14T11:00:00Z',
    actions: [
      {
        label: 'Review',
        kind: 'route',
        target: `/portfolio?holding=${holdingIds.infy}&section=trades&rebuild=preview`,
      },
    ],
    ...overrides,
  });

export const buildDividendsNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:dividends:${ids.main}:0a1b2c3d`,
    kind: 'dividends_pending',
    severity: 'info',
    title: 'Possible dividends in the ledger',
    text: '2 credits in the ledger of Test Dhan Main look like dividends. Check them and add the ones that are.',
    at: '2026-10-14T12:00:00Z',
    since: '2026-10-14T12:00:00Z',
    actions: [{ label: 'Review', kind: 'route', target: `/portfolio?cash=suggestions&account=${ids.main}` }],
    ...overrides,
  });

export const buildRowsDroppedNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:dropped:${ids.main}:trades:1a2b3c4d`,
    kind: 'rows_dropped',
    severity: 'warning',
    title: 'Some trades could not be read',
    text: 'The latest trades read of Test Dhan Main dropped 3 rows: 2 with an unknown type, 1 with no date. Open the sync line to see where they were.',
    at: '2026-10-14T13:00:00Z',
    since: '2026-10-14T13:00:00Z',
    actions: [{ label: 'Open', kind: 'route', target: '/portfolio?focus=sync' }],
    ...overrides,
  });

export const buildUnmatchedNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  base({
    id: `attention:unmatched:${ids.main}:1-0`,
    kind: 'securities_unmatched',
    severity: 'warning',
    title: 'Securities with no match in the instrument list',
    text: '1 holding of Test Dhan Main has no match in the instrument master, so there is no price or sector for it.',
    at: '2026-10-14T10:00:00Z',
    since: '2026-10-14T10:00:00Z',
    actions: [{ label: 'Open', kind: 'route', target: `/portfolio?unmatched=1&account=${ids.main}` }],
    ...overrides,
  });

/** The same item once its cause is gone: kept 24 hours for the drawer's Resolved group. */
export const resolved = (notice: PortfolioNotice): PortfolioNotice => ({ ...notice, resolved: true });

/**
 * Every command target the API can name in a notice, made from the generated list (`attentionCommands`, ADR 0030): each
 * template's placeholders are filled (`{account}` with a guid, anything else with a slug), so a new command on the server is
 * covered the moment the list is regenerated. Nothing here is copied by hand.
 */
export const generatedCommandTargets = (accountId: string): string[] =>
  attentionCommands.map(
    (command) =>
      `${command.method} ${command.template.replace(/\{(\w+)\}/g, (_, name: string) => (name === 'account' ? accountId : `${name}-1`))}`,
  );
