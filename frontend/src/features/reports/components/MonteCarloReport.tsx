import { Alert, Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { DataTable, formatCount, formatRatio, NonValue, type MetricState } from '@/shared/analytics';
import { ApiError } from '@/shared/api';
import { formatIstDate } from '@/shared/lib/format';
import { labReportQuery, labRunsQuery, type MonteCarlo } from '../api/reportQueries';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

interface Row {
  id: string;
  label: string;
  value: number;
}

const rows = (m: MonteCarlo): Row[] => [
  { id: 'mean', label: 'Observed mean (R)', value: m.observedMeanR },
  { id: 'dd', label: 'Observed max drawdown (R)', value: m.observedMaxDrawdownR },
  { id: 'm05', label: 'Bootstrap mean, 5th percentile (R)', value: m.meanP05 },
  { id: 'm50', label: 'Bootstrap mean, median (R)', value: m.meanP50 },
  { id: 'm95', label: 'Bootstrap mean, 95th percentile (R)', value: m.meanP95 },
  { id: 'd50', label: 'Bootstrap drawdown, median (R)', value: m.drawdownP50 },
  { id: 'd95', label: 'Bootstrap drawdown, 95th percentile (R)', value: m.drawdownP95 },
  { id: 'np', label: 'Share of draws with a mean R not positive (0 to 1)', value: m.shareNotPositive },
];

/** One run's stored robustness, read when asked for. A run with no report (409) or no bootstrap says so. */
function RunRisk({ id, label, min }: { id: string; label: string; min: number | null }) {
  const result = useQuery(labReportQuery(id));
  if (result.isPending) return <Typography variant="body2">{`Loading the report of ${label}…`}</Typography>;
  if (result.isError) {
    const none = result.error instanceof ApiError && result.error.problem?.code === 'lab.no_report';
    return (
      <Alert severity={none ? 'info' : 'error'}>
        {none ? `${label} has no report (only a succeeded full or robust run has one).` : result.error.message}
      </Alert>
    );
  }
  const robustness = result.data.robustness;
  const mc = robustness?.monteCarlo ?? null;
  if (!mc) {
    return <NonValue state={{ kind: 'unavailable', reason: `${label} stored no Monte Carlo bootstrap.` }} />;
  }
  // Every figure rests on mc.trades: below the minimum sample of the other panels it is insufficient, as everywhere (the minimum is unknown until what-works answers).
  const thin = min === null || mc.trades < min;
  return (
    <Box sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2">
        {`${formatCount(mc.trades)} out-of-sample trades, ${formatCount(mc.draws)} draws, seed ${mc.seed}. Every figure rests on those ${formatCount(mc.trades)} trades (n).`}
      </Typography>
      <DataTable
        label={`Monte Carlo risk, ${label}`}
        rows={rows(mc)}
        rowKey={(r) => r.id}
        columns={[
          { kind: 'text', id: 'figure', header: 'Figure', render: (r) => r.label },
          {
            kind: 'metric',
            id: 'value',
            header: 'Value',
            metric: (r): MetricState =>
              thin ? { kind: 'insufficient', n: mc.trades, min: min ?? 0 } : { kind: 'value', value: r.value },
            format: formatRatio,
          },
        ]}
      />
      {robustness?.limits.map((line) => (
        <Typography key={line} variant="body2" color="text.secondary">
          {line}
        </Typography>
      ))}
    </Box>
  );
}

/** R15: the Lab's stored bootstrap of its out-of-sample trades, as a table (percentiles only: the stored result has no paths, so no fan chart). */
export function MonteCarloReport({ min }: Pick<ReportPanelProps, 'min'>) {
  const runs = useQuery(labRunsQuery);
  const [open, setOpen] = useState<readonly string[]>([]);
  return (
    <QueryPanel title="Monte Carlo risk" query={runs}>
      {(list) =>
        list.length === 0 ? (
          <NonValue state={{ kind: 'empty', reason: 'No Lab run yet.' }} />
        ) : (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            <Typography variant="body2" color="text.secondary">
              A bootstrap of the trades the Lab tested out of sample, assuming they are independent. Percentiles of a
              few years are not forecasts. Choose a run to read its stored result.
            </Typography>
            {list.map((run) => {
              const label = `${run.strategy.id} (${run.state}, ${formatIstDate(run.createdAt)})`;
              const shown = open.includes(run.id);
              return (
                <Box key={run.id} component="section" aria-label={label} sx={{ display: 'grid', gap: 1 }}>
                  <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                    <Typography variant="body1" sx={{ flex: '1 1 16rem' }}>
                      {label}
                    </Typography>
                    <Button
                      size="small"
                      variant="outlined"
                      aria-expanded={shown}
                      onClick={() => {
                        setOpen(shown ? open.filter((id) => id !== run.id) : [...open, run.id]);
                      }}
                      sx={{ minHeight: 44 }}
                    >
                      {shown ? 'Hide' : 'Show Monte Carlo risk'}
                    </Button>
                  </Box>
                  {shown && <RunRisk id={run.id} label={label} min={min} />}
                </Box>
              );
            })}
          </Box>
        )
      }
    </QueryPanel>
  );
}
