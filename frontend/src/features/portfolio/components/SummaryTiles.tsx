import AccountBalanceWalletIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import DonutSmallIcon from '@mui/icons-material/DonutSmallOutlined';
import PaidIcon from '@mui/icons-material/PaidOutlined';
import SavingsIcon from '@mui/icons-material/SavingsOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChartOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUpOutlined';
import { Alert, Box, Button } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import type { HoldingsSummary } from '@/shared/api';
import { formatDate, formatInr, formatNumber } from '@/shared/lib/format';
import { LoadingState, StatTile } from '@/shared/ui';
import { problemText } from '../lib/errors';
import { xirrOf } from '../lib/figures';
import { Pnl, PnlPercent, Xirr } from './Figures';

function Tiles({ data }: { data: HoldingsSummary }) {
  const { returns } = data;
  const xirr = xirrOf(returns);
  const unmapped = data.unmappedCount > 0 ? ` · ${formatNumber(data.unmappedCount)} not in master list` : '';
  return (
    <Box
      sx={{ display: 'grid', gap: 2, mb: 1, gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 190px), 1fr))' }}
    >
      <StatTile label="Invested" value={formatInr(returns.invested)} icon={SavingsIcon} hue="portfolio" />
      <StatTile
        label="Current value"
        value={returns.value === null ? '—' : formatInr(returns.value)}
        hint={`as of ${formatDate(data.valuationDate)}`}
        icon={AccountBalanceWalletIcon}
        hue="sky"
      />
      <StatTile
        label="Unrealised P&L"
        value={<Pnl value={returns.pnl} focusable />}
        hint={<PnlPercent value={returns.pnlPct} />}
        icon={TrendingUpIcon}
        hue="green"
      />
      <StatTile
        label="XIRR"
        value={<Xirr returns={returns} focusable />}
        hint={'short' in xirr ? xirr.short : 'annualised'}
        icon={ShowChartIcon}
        hue="violet"
      />
      <StatTile
        label={returns.realisedEstimated ? 'Realised (est.)' : 'Realised'}
        value={<Pnl value={returns.realisedPnl} />}
        hint={returns.realisedEstimated ? 'sales priced at the last price' : 'from closed lots'}
        icon={PaidIcon}
        hue="amber"
      />
      <StatTile
        label="Holdings"
        value={formatNumber(data.openCount)}
        hint={`${formatNumber(data.closedCount)} closed${unmapped}`}
        icon={DonutSmallIcon}
        hue="teal"
      />
    </Box>
  );
}

/** The totals of the chosen account and bucket: invested, value, P&L, XIRR, realised and the counts. */
export function SummaryTiles({ query }: { query: UseQueryResult<HoldingsSummary> }) {
  if (query.isPending) return <LoadingState label="Loading totals" />;
  if (query.isError) {
    return (
      <Alert
        severity="error"
        sx={{ mb: 2 }}
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              void query.refetch();
            }}
          >
            Retry
          </Button>
        }
      >
        {problemText(query.error, 'The totals could not be loaded.')}
      </Alert>
    );
  }
  return <Tiles data={query.data} />;
}
