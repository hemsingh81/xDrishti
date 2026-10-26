import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatR, MetricValue, type Column } from '@/shared/analytics';
import { formatInr } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperDisciplineQuery, type DisciplineLine, type ExecutionRow } from '../api/reviewQueries';
import { toMetric } from '../lib/metric';

const columns: readonly Column<DisciplineLine>[] = [
  { kind: 'text', id: 'title', header: 'Line', render: (l) => l.title },
  { kind: 'text', id: 'n', header: 'n', align: 'right', render: (l) => l.n },
  {
    kind: 'text',
    id: 'sum',
    header: 'Sum R',
    align: 'right',
    render: (l) => (l.kind === 'flag' ? '' : <MetricValue state={toMetric(l.sumR)} format={formatR} />),
  },
  {
    kind: 'text',
    id: 'mean',
    header: 'Mean R',
    align: 'right',
    render: (l) => (l.kind === 'flag' ? '' : <MetricValue state={toMetric(l.rMetric)} format={formatR} />),
  },
  {
    kind: 'text',
    id: 'rupees',
    header: 'Rupees per account',
    align: 'right',
    render: (l) => l.rupees.map((r) => formatInr(r.amount)).join(', '),
  },
  { kind: 'text', id: 'note', header: 'Note', render: (l) => l.note },
];

const executionColumns: readonly Column<ExecutionRow>[] = [
  { kind: 'text', id: 'label', header: 'Step', render: (r) => r.label },
  { kind: 'metric', id: 'value', header: 'Mean R', metric: (r) => toMetric(r.value), format: formatR },
  { kind: 'text', id: 'n', header: 'n', align: 'right', render: (r) => r.n },
];

/** The discipline report of the epoch as tables (the table is the text alternative; there is no chart). Read only. */
export function DisciplineCard() {
  const discipline = useQuery(paperDisciplineQuery);
  if (discipline.isPending) return <LoadingState label="Loading the discipline report…" />;
  if (discipline.isError) return <ErrorState error={discipline.error} />;
  const d = discipline.data;
  return (
    <Box component="section" aria-labelledby="discipline-heading" sx={{ mb: 3 }}>
      <Typography variant="h2" id="discipline-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Discipline, whole epoch to date
      </Typography>
      <Typography variant="body2" sx={{ mb: 1 }}>
        {d.basis}
      </Typography>
      {d.unavailable ? (
        <Typography role="status">Unavailable: {d.unavailable}</Typography>
      ) : (
        <>
          <DataTable label="Discipline lines" columns={columns} rows={d.lines} rowKey={(l) => l.category} />
          <Typography variant="subtitle2" sx={{ mt: 2, mb: 1 }}>
            Execution gap (paper)
          </Typography>
          <DataTable label="Paper execution gap" columns={executionColumns} rows={d.execution} rowKey={(r) => r.id} />
        </>
      )}
      <Box component="ul" aria-label="Limits" sx={{ m: 0, mt: 1, pl: 2.5 }}>
        {d.limits.map((limit) => (
          <li key={limit}>{limit}</li>
        ))}
      </Box>
    </Box>
  );
}
