import TimelineIcon from '@mui/icons-material/TimelineOutlined';
import { Alert, Box, Button, Card, CardContent, Link as MuiLink, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router';
import { ApiError } from '@/shared/api';
import { formatDate, formatInr, formatNumber, formatPrice } from '@/shared/lib/format';
import { ErrorState, Facts, LoadingState, PageHeader } from '@/shared/ui';
import { ButtonTabs } from '@/shared/ui/ButtonTabs';
import { lightweightChartsNotice, PriceChart } from '@/shared/ui/chart';
import { replayTradeChartQuery, type ReplayTradeChart } from '../../api/replayApi';
import { tradeBars, tradeEvents, tradeLines, tradeTimeframes, type TradeTimeframe } from '../../lib/tradeChartModel';
import { HindsightBanner } from './HindsightBanner';

const tabs = tradeTimeframes.map((value) => ({
  value,
  label: value,
  id: `replay-trade-tab-${value}`,
  panelId: 'replay-trade-chart',
}));

const money = (v: number | null) => (v === null ? 'n/a' : formatInr(v));

function Body({ view, timeframe }: { view: ReplayTradeChart; timeframe: TradeTimeframe }) {
  const { coherence } = view.chart;
  return (
    <>
      <HindsightBanner run={view} />
      {coherence.state === 'entry_mismatch' && (
        <Alert severity="warning">
          {`The bars do not match this stored trade: it was entered at ${String(coherence.entryStored)}, the bars open at ${String(coherence.entryOnBars)}. ${coherence.detail}`}
        </Alert>
      )}
      <Card component="section" aria-labelledby="replay-trade-chart-title">
        <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
          <Typography id="replay-trade-chart-title" variant="h2" component="h2">
            Price, entry, stop and exit
          </Typography>
          {coherence.state === 'no_bars' ? (
            <Alert severity="info">{`No bars are stored for this window, so no chart is drawn. ${coherence.detail}`}</Alert>
          ) : (
            <PriceChart
              label={`${view.side} ${view.symbol}, ${timeframe}`}
              bars={tradeBars(view)}
              lines={tradeLines(view)}
              events={tradeEvents(view)}
              dayOnly={timeframe === '1D'}
            />
          )}
          {view.exitPriceDerived && view.exitPrice !== null && (
            <Typography variant="body2" color="text.secondary">
              The exit price is derived, not a stored fill: the entry plus the gross result divided by the quantity and
              the side.
            </Typography>
          )}
        </CardContent>
      </Card>
      <Card component="section" aria-labelledby="replay-trade-facts-title">
        <CardContent>
          <Typography id="replay-trade-facts-title" variant="h2" component="h2" sx={{ mb: 1 }}>
            The trade
          </Typography>
          <Facts
            items={[
              { label: 'Symbol', value: view.symbol },
              { label: 'Side', value: view.side },
              { label: 'Quantity', value: formatNumber(view.qty) },
              { label: 'Entry', value: `${formatDate(view.entrySession)} at ${formatPrice(view.entryPrice)}` },
              { label: 'Stop', value: formatPrice(view.stopPrice) },
              {
                label: view.exitPriceDerived ? 'Exit (price derived)' : 'Exit',
                value: view.exitSession
                  ? `${formatDate(view.exitSession)}${view.exitPrice === null ? '' : ` at ${formatPrice(view.exitPrice)}`}`
                  : 'Open',
              },
              { label: 'Gross result', value: money(view.grossPnl) },
              { label: 'Net result', value: view.netPnl === null ? view.costsLabel : money(view.netPnl) },
            ]}
          />
        </CardContent>
      </Card>
      {view.limits.length > 0 && (
        <Card component="section" aria-labelledby="replay-trade-limits-title">
          <CardContent>
            <Typography id="replay-trade-limits-title" variant="h2" component="h2" sx={{ mb: 1 }}>
              Limits
            </Typography>
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {view.limits.map((limit) => (
                <Typography key={limit} component="li" variant="body2">
                  {limit}
                </Typography>
              ))}
            </Box>
          </CardContent>
        </Card>
      )}
    </>
  );
}

/**
 * One trade of a system replay on its bars (SR-02). The bars stop at the hold-out clamp and are not adjusted for later corporate
 * actions: the limits say so. Read only; a replayed trade, not a fill the owner got.
 */
export function ReplayTradeChartPage() {
  const { runId = '', tradeNo = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const asked = params.get('timeframe');
  const timeframe = tradeTimeframes.find((t) => t === asked) ?? '5m';
  const no = Number(tradeNo);
  const valid = Number.isInteger(no) && no >= 0;
  const result = useQuery({ ...replayTradeChartQuery(runId, no, timeframe), enabled: valid });
  const error = result.error instanceof ApiError ? result.error : null;
  return (
    <>
      <PageHeader
        title="Replay trade"
        icon={TimelineIcon}
        hue="reports"
        description="A trade of the system replay on its bars: research on replayed history, not a fill you got."
        actions={
          <Button component={Link} to={`/simulation/replay?run=${runId}`}>
            Back to the replay
          </Button>
        }
      />
      <Box sx={{ mb: 2 }}>
        <ButtonTabs
          label="Timeframe"
          tabs={tabs}
          value={timeframe}
          onChange={(value) => {
            setParams(value === '5m' ? {} : { timeframe: value }, { replace: true });
          }}
        />
      </Box>
      <Box
        id="replay-trade-chart"
        role="tabpanel"
        aria-labelledby={`replay-trade-tab-${timeframe}`}
        sx={{ display: 'grid', gap: 2, minWidth: 0 }}
      >
        {!valid || error?.status === 404 ? (
          <Alert severity="warning">Trade not found. Open a trade from the trades table of a replay.</Alert>
        ) : result.isPending ? (
          <LoadingState label="Loading the trade and its bars…" />
        ) : result.isError ? (
          <ErrorState error={result.error} />
        ) : (
          <Body view={result.data} timeframe={timeframe} />
        )}
      </Box>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 3 }}>
        {lightweightChartsNotice}{' '}
        <MuiLink href="https://www.tradingview.com/" target="_blank" rel="noopener noreferrer">
          tradingview.com
        </MuiLink>
      </Typography>
    </>
  );
}
