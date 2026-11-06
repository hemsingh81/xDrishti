import { Box, Typography } from '@mui/material';
import { DataTable, formatCount, formatRange, formatRate, NonValue } from '@/shared/analytics';
import { Facts } from '@/shared/ui/Facts';
import type { DecayQuarter, DecaySeries, DecayWindow } from '../api/confidenceQueries';
import { seriesStateLabel } from '../lib/confidenceText';
import { figure, formatFine, formatFineR, formatFineSigned } from '../lib/figure';
import { Panel } from './Panel';

const below = (flag: boolean) => (flag ? 'Below the level' : 'At or above the level');

function WindowsTable({
  title,
  windows,
  pending,
}: {
  title: string;
  windows: readonly DecayWindow[];
  pending: number;
}) {
  return (
    <DataTable
      label={`${title}: completed windows against their alert level`}
      rows={windows}
      rowKey={(row) => String(row.index)}
      status={
        windows.length === 0
          ? {
              kind: 'empty',
              reason: `No completed window yet (${formatCount(pending)} trades are waiting for the first one).`,
            }
          : undefined
      }
      columns={[
        { kind: 'text', id: 'index', header: 'Window', render: (row) => `#${formatCount(row.index)}` },
        {
          kind: 'text',
          id: 'days',
          header: 'Days',
          render: (row) => formatRange({ from: row.firstDay, to: row.lastDay }),
        },
        { kind: 'text', id: 'trades', header: 'Trades', render: (row) => formatCount(row.trades), align: 'right' },
        {
          kind: 'metric',
          id: 'mean',
          header: 'Mean net R',
          metric: (row) => figure(row.meanR, 'not stated'),
          format: formatFineR,
        },
        {
          kind: 'metric',
          id: 'level',
          header: 'Alert level (R)',
          metric: (row) => figure(row.threshold, 'not stated'),
          format: formatFineSigned,
        },
        {
          kind: 'metric',
          id: 'deff',
          header: 'Design effect',
          metric: (row) => figure(row.designEffect, 'not stated'),
          format: formatFine,
        },
        { kind: 'text', id: 'below', header: 'Against the level', render: (row) => below(row.below) },
      ]}
    />
  );
}

function QuartersTable({
  title,
  quarters,
  share,
}: {
  title: string;
  quarters: readonly DecayQuarter[];
  share: number | null;
}) {
  return (
    <>
      <DataTable
        label={`${title}: mean net R by quarter`}
        rows={quarters}
        rowKey={(row) => row.label}
        status={quarters.length === 0 ? { kind: 'empty', reason: 'No quarter has a trade yet.' } : undefined}
        columns={[
          { kind: 'text', id: 'quarter', header: 'Quarter', render: (row) => row.label },
          { kind: 'text', id: 'trades', header: 'Trades', render: (row) => formatCount(row.trades), align: 'right' },
          {
            kind: 'metric',
            id: 'mean',
            header: 'Mean net R',
            metric: (row) => figure(row.meanR, 'not stated'),
            format: formatFineR,
          },
          {
            kind: 'text',
            id: 'weak',
            header: 'Against the floor',
            render: (row) => (row.weak ? 'Weak (below the floor)' : 'Not weak'),
          },
        ]}
      />
      <Typography variant="body2" sx={{ mt: 1 }}>
        Share of quarters below the floor:{' '}
        {share === null ? (
          <NonValue state={{ kind: 'unavailable', reason: 'no quarter to count' }} />
        ) : (
          formatRate(share)
        )}
      </Typography>
    </>
  );
}

interface DecaySeriesPanelProps {
  title: string;
  series: DecaySeries;
}

/** One series run through the decay monitor: its state, windows against their level, twice in a row, CUSUM and quarters. */
export function DecaySeriesPanel({ title, series }: DecaySeriesPanelProps) {
  const cusum = series.cusum;
  return (
    <Panel title={title}>
      <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
        <Facts
          items={[
            { label: 'State', value: <strong>{seriesStateLabel(series.state)}</strong> },
            { label: 'Basis', value: series.basis },
            {
              label: 'Trades',
              value: `${formatCount(series.trades)} (${formatCount(series.pendingTrades)} after the last completed window)`,
            },
            {
              label: 'Twice in a row',
              value: series.twiceInARow
                ? `Yes: the last two completed windows are both below their level${
                    series.firstTwiceAtWindow === null
                      ? ''
                      : ` (first reached at window #${formatCount(series.firstTwiceAtWindow)})`
                  }`
                : `No (${formatCount(series.streak)} completed window(s) below their level in a row at the end)`,
            },
            {
              label: 'CUSUM',
              value:
                cusum === null ? (
                  <NonValue state={{ kind: 'unavailable', reason: 'the series is not judged' }} />
                ) : (
                  `${cusum.alarm ? 'Alarm' : 'No alarm'}; ${formatCount(cusum.events)} event(s)${cusum.latest ? `. ${cusum.latest}` : ''}`
                ),
            },
          ]}
        />
        {series.note && <Typography variant="body2">{series.note}</Typography>}
        <WindowsTable title={title} windows={series.windows} pending={series.pendingTrades} />
        <QuartersTable title={title} quarters={series.quarters} share={series.weakQuarterShare} />
      </Box>
    </Panel>
  );
}
