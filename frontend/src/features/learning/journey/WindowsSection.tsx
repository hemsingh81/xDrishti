import { Box, Typography } from '@mui/material';
import {
  BarList,
  DataTable,
  formatCount,
  formatR,
  formatRatio,
  formatSigned,
  metricOf,
  MetricValue,
} from '@/shared/analytics';
import type { Column, MetricState } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { Counterfactual, CounterfactualWindow } from '../api/learningQueries';
import type { Headline } from './headline';

const fixed4 = (value: number): string => value.toFixed(4);

/** An uplift is a number only for a judged window; otherwise the sentence the server sent, never a bar of 0. */
function upliftState(w: CounterfactualWindow): MetricState {
  return w.takeRUplift === null
    ? { kind: 'not_evaluable', reason: w.note ?? 'Same model on both sides, not judged.' }
    : metricOf(w.takeRUplift);
}

const windowName = (w: CounterfactualWindow): string => `Window ${w.index}`;

const columns: readonly Column<CounterfactualWindow>[] = [
  {
    kind: 'text',
    id: 'window',
    header: 'Window',
    render: (w) =>
      `${windowName(w)}: ${formatDate(w.start)} to ${formatDate(w.end)}${w.informative ? '' : ' (not judged)'}`,
  },
  { kind: 'metric', id: 'rows', header: 'Trades', metric: (w) => metricOf(w.rows), format: formatCount },
  { kind: 'metric', id: 'days', header: 'Days', metric: (w) => metricOf(w.days), format: formatCount },
  {
    kind: 'metric',
    id: 'instruments',
    header: 'Instruments',
    metric: (w) => metricOf(w.instruments),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'frozen-train',
    header: 'Train rows, frozen',
    metric: (w) => metricOf(w.frozenTrainRows),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'retrained-train',
    header: 'Train rows, retrained',
    metric: (w) => metricOf(w.retrainedTrainRows),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'll-frozen',
    header: 'Log loss, frozen',
    metric: (w) => metricOf(w.frozen.logLoss),
    format: fixed4,
  },
  {
    kind: 'metric',
    id: 'll-retrained',
    header: 'Log loss, retrained',
    metric: (w) => metricOf(w.retrained.logLoss),
    format: fixed4,
  },
  {
    kind: 'metric',
    id: 'brier-frozen',
    header: 'Brier, frozen',
    metric: (w) => metricOf(w.frozen.brier),
    format: fixed4,
  },
  {
    kind: 'metric',
    id: 'brier-retrained',
    header: 'Brier, retrained',
    metric: (w) => metricOf(w.retrained.brier),
    format: fixed4,
  },
  {
    kind: 'metric',
    id: 'auc-frozen',
    header: 'AUC, frozen',
    metric: (w) => metricOf(w.frozen.auc),
    format: formatRatio,
  },
  {
    kind: 'metric',
    id: 'auc-retrained',
    header: 'AUC, retrained',
    metric: (w) => metricOf(w.retrained.auc),
    format: formatRatio,
  },
  {
    kind: 'metric',
    id: 'take-frozen',
    header: 'Take-R per trade, frozen',
    metric: (w) => metricOf(w.frozen.takeR),
    format: formatSigned,
  },
  {
    kind: 'metric',
    id: 'take-retrained',
    header: 'Take-R per trade, retrained',
    metric: (w) => metricOf(w.retrained.takeR),
    format: formatSigned,
  },
  {
    kind: 'metric',
    id: 'kept',
    header: 'Kept, retrained',
    metric: (w) => metricOf(w.retrained.kept),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'skipped-mean',
    header: 'Skipped mean R, retrained',
    metric: (w) => metricOf(w.retrained.skippedMeanR),
    format: formatSigned,
  },
  {
    kind: 'metric',
    id: 'gain',
    header: 'Paired gain (log loss)',
    metric: (w) => (w.informative ? metricOf(w.gain) : { kind: 'not_evaluable', reason: 'Not judged' }),
    format: fixed4,
  },
  {
    kind: 'metric',
    id: 'se',
    header: 'Gain standard error (by day)',
    metric: (w) => (w.informative ? metricOf(w.gainStandardError) : { kind: 'not_evaluable', reason: 'Not judged' }),
    format: fixed4,
  },
  { kind: 'metric', id: 'uplift', header: 'Take-R uplift', metric: upliftState, format: formatR },
  {
    kind: 'metric',
    id: 'cumulative',
    header: 'Cumulative uplift',
    metric: (w) => metricOf(w.cumulativeTakeRUplift),
    format: formatR,
  },
];

interface Props {
  report: Counterfactual;
  headline: Headline;
}

/** The windows as stored: a bar of the take-R uplift per window, the cumulative figure, and the table with every number. */
export function WindowsSection({ report, headline }: Props) {
  const last = report.windows.at(-1);
  const tone = headline.colourGain ? 'gain-loss' : 'neutral';
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">
        R of taking trades the model scores at or above its base rate; a convention, not a strategy. Windows are
        quarterly (every {report.knobs.retrainMonths} months), as stored; nothing is interpolated.
      </Typography>
      {report.windows.length > 0 && (
        <BarList
          label="Take-R uplift of retraining by window"
          rows={report.windows.map((w) => ({
            id: String(w.index),
            label: windowName(w),
            state: upliftState(w),
            n: w.rows,
          }))}
          format={formatR}
        />
      )}
      {last && (
        <Typography component="p">
          Cumulative take-R uplift:{' '}
          <MetricValue state={metricOf(last.cumulativeTakeRUplift)} format={formatR} tone={tone} />
        </Typography>
      )}
      <DataTable
        label="Counterfactual windows"
        columns={columns}
        rows={report.windows}
        rowKey={(w) => String(w.index)}
      />
    </Box>
  );
}
