import TimelineIcon from '@mui/icons-material/TimelineOutlined';
import { Alert, Box, Button, Link as MuiLink, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useUrlState } from '@/shared/analytics';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { ButtonTabs } from '@/shared/ui/ButtonTabs';
import { lightweightChartsNotice, PriceChart } from '@/shared/ui/chart';
import { tradeReplayQuery, type TradeReplay } from '../api/reportQueries';
import {
  largerTimeframe,
  replayBars,
  replayEvents,
  replayLines,
  replayTimeframes,
  type ReplayTimeframe,
} from '../lib/replayModel';
import { Panel } from './Panel';
import { ReplayEvents } from './ReplayEvents';
import { ReplayFacts } from './ReplayFacts';
import { ReplayFeatures } from './ReplayFeatures';

const whole = (text: string | null): number | undefined => {
  const n = text === null ? Number.NaN : Number(text);
  return Number.isInteger(n) && n >= 0 ? n : undefined;
};

const tabs = replayTimeframes.map((value) => ({
  value,
  label: value,
  id: `replay-tab-${value}`,
  panelId: 'replay-chart',
}));

function Sessions({
  name,
  value,
  max,
  onChange,
}: {
  name: string;
  value: number | undefined;
  max: number;
  onChange: (v: string | null) => void;
}) {
  return (
    <TextField
      select
      size="small"
      label={`Sessions ${name}`}
      value={value === undefined ? '' : String(value)}
      onChange={(event) => {
        onChange(event.target.value === '' ? null : event.target.value);
      }}
      sx={{ minWidth: 140 }}
    >
      <MenuItem value="">Default</MenuItem>
      {Array.from({ length: max + 1 }, (_, i) => (
        <MenuItem key={i} value={String(i)}>
          {String(i)}
        </MenuItem>
      ))}
    </TextField>
  );
}

function Body({ view, timeframe }: { view: TradeReplay; timeframe: ReplayTimeframe }) {
  const events = replayEvents(view);
  const { coherence } = view.chart;
  return (
    <>
      {coherence.state === 'entry_mismatch' && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {`The bars no longer match this stored trade: it was entered at ${String(coherence.entryStored)}, the bars now open at ${String(coherence.entryOnBars)}. ${coherence.detail}`}
        </Alert>
      )}
      <Panel title="Price and events">
        {coherence.state === 'no_bars' ? (
          <Alert severity="info">{`No bars are stored for this window, so no chart is drawn. ${coherence.detail} The events and the facts are below.`}</Alert>
        ) : (
          <PriceChart
            label={`${view.trade.strategyId} ${view.trade.side} ${view.trade.symbol}, ${timeframe}`}
            bars={replayBars(view)}
            lines={replayLines(view)}
            events={events}
            dayOnly={timeframe === '1D'}
          />
        )}
        <Box sx={{ mt: 2 }}>
          <ReplayEvents events={events} />
        </Box>
      </Panel>
      <ReplayFacts view={view} />
      <ReplayFeatures features={view.features} />
      <Panel title="Limits">
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {view.limits.map((limit) => (
            <Typography key={limit} component="li" variant="body2">
              {limit}
            </Typography>
          ))}
        </Box>
      </Panel>
    </>
  );
}

/**
 * One stored simulated trade on the bars it was simulated on (P8-11): entry, stop, target, every leg and the exit, beside its facts and
 * the decision-time features it has. Read only. Simulated trade, replayed history: not a fill the owner got.
 */
export function TradeReplayPage() {
  const { runKey = '', ordinal = '' } = useParams();
  const navigate = useNavigate();
  const url = useUrlState();
  const timeframe = url.choice('timeframe', replayTimeframes, '5m');
  const before = whole(url.text('before'));
  const after = whole(url.text('after'));
  const at = whole(ordinal);
  const max = timeframe === '1D' ? 60 : 3;
  const result = useQuery({
    ...tradeReplayQuery(runKey, at ?? 0, { timeframe, before, after, openAtEnd: url.text('openAtEnd') === 'true' }),
    enabled: at !== undefined,
  });
  const error = result.error instanceof ApiError ? result.error : null;
  const larger = largerTimeframe(timeframe);

  return (
    <>
      <PageHeader
        title="Trade replay"
        icon={TimelineIcon}
        hue="reports"
        description="Simulated trade, replayed history: the bars the trade was simulated on, not a fill you got."
        actions={
          <Button
            onClick={() => {
              void navigate(-1);
            }}
          >
            Back to the report
          </Button>
        }
      />
      <Stack direction="row" sx={{ mb: 2, gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <ButtonTabs
          label="Timeframe"
          tabs={tabs}
          value={timeframe}
          onChange={(value) => {
            url.update({ timeframe: value === '5m' ? null : value, before: null, after: null });
          }}
        />
        <Sessions
          name="before"
          value={before}
          max={max}
          onChange={(v) => {
            url.update({ before: v });
          }}
        />
        <Sessions
          name="after"
          value={after}
          max={max}
          onChange={(v) => {
            url.update({ after: v });
          }}
        />
      </Stack>
      <Box
        id="replay-chart"
        role="tabpanel"
        aria-labelledby={`replay-tab-${timeframe}`}
        sx={{ display: 'grid', gap: 2 }}
      >
        {at === undefined || error?.status === 404 ? (
          <Alert severity="warning">Trade not found. Open a trade from the list of a cell in the reports.</Alert>
        ) : result.isPending ? (
          <LoadingState label="Loading the trade and its bars…" />
        ) : error?.problem?.code === 'reports.window_too_large' ? (
          <Alert
            severity="warning"
            action={
              larger ? (
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    url.update({ timeframe: larger, before: null, after: null });
                  }}
                >
                  {`Use ${larger}`}
                </Button>
              ) : undefined
            }
          >
            {error.message}
          </Alert>
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
