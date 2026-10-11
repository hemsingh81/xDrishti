import AccountBalanceWalletIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import PaymentsIcon from '@mui/icons-material/PaymentsOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChartOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUpOutlined';
import { Alert, Grid, Stack } from '@mui/material';
import type { AccountDetail } from '@/shared/api';
import { formatInr } from '@/shared/lib/format';
import { hueForLabel } from '@/shared/lib/iconHue';
import { Facts, StatTile, type Fact } from '@/shared/ui';
import { modeLabel } from '../lib/labels';
import { ApiKeyExpiry } from './ApiKeyExpiry';
import { ConnectionStatus } from './ConnectionStatus';
import { DisabledActions } from './DisabledActions';
import { RoleChips } from './RoleChips';
import { SecretsStatus } from './SecretsStatus';

const notReady = 'Available once the broker adapter and portfolio sync are built (P1-04, P2).';

const tiles = [
  { label: 'Holdings value', icon: AccountBalanceWalletIcon, hint: 'open positions: —' },
  { label: 'Unrealised', icon: TrendingUpIcon, hint: 'realised: —' },
  { label: 'Day change', icon: ShowChartIcon, hint: undefined },
  { label: 'Funds available', icon: PaymentsIcon, hint: 'used: —' },
];

function tradingSummary(account: AccountDetail): string {
  const t = account.trading;
  const capital = t.capitalAmount === null ? 'capital not set' : `capital ${formatInr(t.capitalAmount)}`;
  return [modeLabel(t.mode), `${t.riskProfileName ?? 'no'} profile`, capital, t.styles.join(' + ') || 'no style'].join(
    ' · ',
  );
}

function facts(account: AccountDetail): Fact[] {
  const list: Fact[] = [
    { label: 'Roles', value: <RoleChips roles={account.roles} /> },
    { label: 'Token', value: <ConnectionStatus connection={account.connection} /> },
    { label: 'Last sync', value: '—' },
  ];
  if (account.trading.enabled) {
    list.push({ label: 'Trading', value: tradingSummary(account) });
  }
  if (account.portfolio.enabled) {
    const p = account.portfolio;
    list.push({
      label: 'Portfolio',
      value: `default bucket ${p.defaultBucket} · ${p.includeInTotals ? 'included in totals' : 'excluded from totals'}`,
    });
  }
  if (account.data.enabled) {
    list.push({
      label: 'Data',
      value: `${account.data.primary ? 'primary' : 'standby'} · live-feed budget ${account.data.liveFeedBudget} · subscription unknown until the broker adapter`,
    });
  }
  list.push({
    label: 'Credentials',
    value: (
      <Stack spacing={0.5}>
        <SecretsStatus secrets={account.secrets} />
        <ApiKeyExpiry expiresOn={account.apiKeyExpiresOn} />
      </Stack>
    ),
  });
  if (account.notes) {
    list.push({ label: 'Notes', value: account.notes });
  }
  return list;
}

/** Account at a glance: money tiles (not available yet), token status, roles, settings summaries and stored secrets. */
export function OverviewTab({ account }: { account: AccountDetail }) {
  return (
    <Stack spacing={2.5}>
      <Grid container spacing={2}>
        {tiles.map((tile) => (
          <Grid key={tile.label} size={{ xs: 6, md: 3 }}>
            <StatTile label={tile.label} value="—" hint={tile.hint} icon={tile.icon} hue={hueForLabel(tile.label)} />
          </Grid>
        ))}
      </Grid>
      <Alert role="status" severity="info">
        {notReady}
      </Alert>
      <Facts items={facts(account)} />
      <DisabledActions
        actions={[{ label: 'Sync now', reason: 'Syncing needs the broker adapter and portfolio sync (P1-04, P2).' }]}
      />
    </Stack>
  );
}
