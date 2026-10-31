import ShieldIcon from '@mui/icons-material/ShieldOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import { Alert, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { MetricValue } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatInrPrecise, formatPrice } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { positionsQuery } from '../api/viewsApi';
import { execMetric } from '../lib/metrics';
import { RetryError } from './RetryError';
import { StackedTable, type Col } from './StackedTable';

type Row = Schemas['PositionRow'];

/** Protection is green only when confirmed from the broker's order book; anything else has a word and a warning icon. */
function Protection({ value }: { value: string }) {
  const word =
    value === 'confirmed' ? 'Protected (confirmed)' : value === 'missing' ? 'Protection missing' : 'Protection unknown';
  const Icon = value === 'confirmed' ? ShieldIcon : value === 'missing' ? ErrorIcon : WarningIcon;
  const color = value === 'confirmed' ? 'success.main' : value === 'missing' ? 'error.main' : 'warning.main';
  return (
    <Stack component="span" direction="row" sx={{ gap: 0.5, alignItems: 'center', color }}>
      <Icon aria-hidden fontSize="small" />
      {word}
    </Stack>
  );
}

const recon: Readonly<Record<string, string>> = {
  matched: 'Matched',
  break: 'Break',
  not_run: 'Not run',
  unavailable: 'Not available',
};

const columns: readonly Col<Row>[] = [
  { id: 'symbol', header: 'Symbol', render: (r) => `${r.symbol} (${r.account})` },
  { id: 'qty', header: 'Net quantity', align: 'right', render: (r) => r.netQuantity },
  { id: 'avg', header: 'Average', align: 'right', render: (r) => formatPrice(r.averagePrice) },
  {
    id: 'last',
    header: 'Last price',
    align: 'right',
    render: (r) => <MetricValue state={execMetric(r.lastPrice)} format={formatPrice} />,
  },
  {
    id: 'pnl',
    header: 'Unrealised',
    align: 'right',
    render: (r) => <MetricValue state={execMetric(r.unrealised)} format={formatInrPrecise} tone="gain-loss" />,
  },
  {
    id: 'risk',
    header: 'Risk to stop',
    align: 'right',
    render: (r) => <MetricValue state={execMetric(r.riskToStop)} format={formatInrPrecise} />,
  },
  { id: 'prot', header: 'Protection', render: (r) => <Protection value={r.protection} /> },
  {
    id: 'src',
    header: 'Source',
    render: (r) => (r.source === 'foreign' ? 'Foreign (placed outside this system)' : 'Ours'),
  },
  { id: 'rec', header: 'Reconciliation', render: (r) => recon[r.reconciliation] ?? `Unknown: ${r.reconciliation}` },
];

/** Open positions with the protection state, source and reconciliation; a missing figure is a word, never zero. */
export function PositionsTab() {
  const { data, error, isPending, refetch } = useQuery(positionsQuery());
  return (
    <>
      {isPending && <LoadingState label="Loading positions" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data?.length === 0 && (
        <Alert severity="info" role="status">
          No open positions.
        </Alert>
      )}
      {data && data.length > 0 && (
        <StackedTable
          label="Positions"
          columns={columns}
          rows={data}
          rowKey={(r) => `${r.accountId}:${r.symbol}:${r.source}`}
        />
      )}
      <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', mt: 1 }}>
        Protection counts as confirmed only when the broker&apos;s order book, read recently, showed the stop.
      </Typography>
    </>
  );
}
