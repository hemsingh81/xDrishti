import { Box, Typography } from '@mui/material';
import { DataTable, formatCount, formatR, metricOf, NonValue, type Column } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { Journey } from '../api/learningQueries';

type Week = Journey['planPrice']['weeks'][number];

const columns: readonly Column<Week>[] = [
  { kind: 'text', id: 'week', header: 'ISO week', render: (w) => `${w.isoWeek} (from ${formatDate(w.start)})` },
  { kind: 'metric', id: 'graded', header: 'Graded tickets', metric: (w) => metricOf(w.graded), format: formatCount },
  {
    kind: 'metric',
    id: 'mean',
    header: 'Mean R',
    // fewer than five graded tickets: the count is shown and no mean is drawn
    metric: (w) => (w.meanR === null ? { kind: 'insufficient', n: w.graded, min: 5 } : metricOf(w.meanR)),
    format: formatR,
  },
  { kind: 'metric', id: 'total', header: 'Total R', metric: (w) => metricOf(w.totalR), format: formatR },
];

/** The plan-price line (grades by week) and the live line, which says it is not evaluable. */
export function PlanPriceSection({ journey }: { journey: Journey }) {
  const { planPrice, live } = journey;
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        Basis: {planPrice.basis}. A re-simulation of the plan, not an execution.
      </Typography>
      {planPrice.note !== null && <Typography variant="body2">{planPrice.note}</Typography>}
      {planPrice.state === 'empty' ? (
        <NonValue state={{ kind: 'empty', reason: 'No plan has been graded yet.' }} />
      ) : (
        <DataTable
          label="Plan-price grades by week"
          columns={columns}
          rows={planPrice.weeks}
          rowKey={(w) => w.isoWeek}
        />
      )}
      <Box component="p" sx={{ m: 0 }}>
        <Typography component="span" sx={{ fontWeight: 600, mr: 1 }}>
          Live line
        </Typography>
        <NonValue state={{ kind: 'not_evaluable', reason: live.reason }} />
      </Box>
    </Box>
  );
}
