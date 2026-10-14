import AccountBalanceWalletIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import CompareArrowsIcon from '@mui/icons-material/CompareArrowsOutlined';
import DonutSmallIcon from '@mui/icons-material/DonutSmallOutlined';
import PaidIcon from '@mui/icons-material/PaidOutlined';
import SavingsIcon from '@mui/icons-material/SavingsOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChartOutlined';
import ScaleIcon from '@mui/icons-material/ScaleOutlined';
import TodayIcon from '@mui/icons-material/TodayOutlined';
import TrendingDownIcon from '@mui/icons-material/TrendingDownOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUpOutlined';
import { Alert, Box, Button } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import type { HoldingsSummary, PortfolioHistory } from '@/shared/api';
import { formatDate, formatInr, formatIstDayMonth, formatNumber } from '@/shared/lib/format';
import { useWallClock } from '@/shared/lib/useWallClock';
import { LoadingState, StatTile } from '@/shared/ui';
import { alphaHint, benchmarkXirrHint } from '../lib/benchmark';
import { drawdownTileHint } from '../lib/drawdown';
import { problemText } from '../lib/errors';
import { noDayChangeReason, xirrOf } from '../lib/figures';
import { isEarlierDay } from '../lib/prices';
import { DayChangeAmount, Pnl, PnlPercent, Xirr } from './Figures';
import { Alpha, BenchmarkXirr, MaxDrawdown } from './HistoryFigures';

/** "1.2% · as of 10 Oct" style hint: the percentage, and the date of the session when the prices are from an earlier day. */
function DayChangeHint({ data, now }: { data: HoldingsSummary; now: number }) {
  if (data.dayChange === null) return <>No previous close yet</>;
  const newest = data.pricesAsOf.newest;
  return (
    <>
      <PnlPercent value={data.dayChangePct} />
      {newest !== null && isEarlierDay(newest, now) && ` · as of ${formatIstDayMonth(newest)}`}
    </>
  );
}

interface HistoryTiles {
  /** The value history of the chosen range: the max drawdown and the benchmark's name and reason. */
  query: UseQueryResult<PortfolioHistory>;
  /** The range chip, e.g. "1Y": the drawdown tile says which range it covers. */
  rangeLabel: string;
}

function Tiles({ data, history }: { data: HoldingsSummary; history: HistoryTiles }) {
  // The benchmark's name and reason are those of the history on screen, once it is the one asked for.
  const benchmark = history.query.isPlaceholderData ? undefined : history.query.data?.benchmark;
  const { returns } = data;
  const now = useWallClock(60_000);
  const xirr = xirrOf(returns);
  const unmapped = data.unmappedCount > 0 ? ` · ${formatNumber(data.unmappedCount)} not in master list` : '';
  return (
    <Box
      sx={(theme) => ({
        display: 'grid',
        gap: 2,
        mb: 1,
        // Ten tiles in even rows, never an orphan: one column on a phone, two up to a wide laptop, then two rows of five
        // (five need about 200 px each, so not before the content is ~1050 px wide).
        gridTemplateColumns: 'minmax(0, 1fr)',
        [theme.breakpoints.up('sm')]: { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' },
        // A label such as "Max drawdown (All)" wraps at this width: every tile reserves two lines, so the values line up.
        [theme.breakpoints.up(1350)]: {
          gridTemplateColumns: 'repeat(5, minmax(0, 1fr))',
          '--stat-tile-label-min': '3em',
        },
      })}
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
        label="Day change"
        value={<DayChangeAmount value={data.dayChange} reason={noDayChangeReason} focusable />}
        hint={<DayChangeHint data={data} now={now} />}
        icon={TodayIcon}
        hue="today"
      />
      <StatTile
        label="Unrealised P&L"
        value={<Pnl value={returns.pnl} focusable />}
        hint={<PnlPercent value={returns.pnlPct} />}
        icon={TrendingUpIcon}
        hue="green"
      />
      <StatTile
        label={returns.realisedEstimated ? 'Realised (est.)' : 'Realised'}
        value={<Pnl value={returns.realisedPnl} />}
        hint={returns.realisedEstimated ? 'sales priced at the last price' : 'from closed lots'}
        icon={PaidIcon}
        hue="amber"
      />
      <StatTile
        label="XIRR"
        value={<Xirr returns={returns} focusable />}
        hint={'short' in xirr ? xirr.short : 'annualised'}
        icon={ShowChartIcon}
        hue="violet"
      />
      <StatTile
        label="Benchmark XIRR"
        value={<BenchmarkXirr summary={data} benchmark={benchmark} />}
        hint={benchmarkXirrHint(data, benchmark)}
        icon={CompareArrowsIcon}
        hue="indigo"
      />
      <StatTile
        label="Alpha"
        value={<Alpha summary={data} benchmark={benchmark} />}
        hint={alphaHint(data, benchmark)}
        icon={ScaleIcon}
        hue="pink"
      />
      <StatTile
        label={`Max drawdown (${history.rangeLabel})`}
        value={<MaxDrawdown query={history.query} rangeLabel={history.rangeLabel} />}
        hint={drawdownTileHint(history.query)}
        icon={TrendingDownIcon}
        hue="rose"
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

/** The totals of the chosen account and bucket: invested, value, P&L, XIRR, benchmark XIRR, alpha, max drawdown, realised and the counts. */
export function SummaryTiles({ query, history }: { query: UseQueryResult<HoldingsSummary>; history: HistoryTiles }) {
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
  return <Tiles data={query.data} history={history} />;
}
