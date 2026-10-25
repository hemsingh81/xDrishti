import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { decisionsQuery } from '../api/learningQueries';
import { useRange } from '../lib/useRange';
import { ComparisonList } from './ComparisonList';
import { DecisionCurves } from './DecisionCurves';
import { GradeStatsTable } from './GradeStatsTable';
import { Panel } from './Panel';
import { RangeFilter } from './RangeFilter';

/** Decisions: did the owner's decisions and the ranking add value? Every figure is simulated on plan prices, never a fill. */
export function DecisionsTab() {
  const { range, setRange } = useRange();
  const query = useQuery(decisionsQuery(range));
  const filter = <RangeFilter range={range} onChange={setRange} />;
  if (query.isPending) return <LoadingState label="Loading the decisions report…" />;
  if (query.isError)
    return (
      <Panel title="Decisions" actions={filter}>
        <ErrorState error={query.error} />
      </Panel>
    );
  const report = query.data;
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Panel title="Cumulative R by rank" actions={filter}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Basis: {report.basis}. Plans read: {report.plans}.
        </Typography>
        <DecisionCurves curves={report.curves} />
      </Panel>
      <Panel title="Primary, reserve and rejected">
        <GradeStatsTable label="Groups" firstHeader="Group" rows={report.groups} />
      </Panel>
      <Panel title="The owner's decisions">
        <GradeStatsTable label="Decisions" firstHeader="Decision" rows={report.decisions} />
      </Panel>
      <Panel title="Comparisons">
        <ComparisonList comparisons={report.comparisons} />
      </Panel>
      {report.notes.length > 0 && (
        <Panel title="Notes from the server">
          <Box component="ul" aria-label="Notes" sx={{ m: 0, pl: 2.5 }}>
            {report.notes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </Box>
        </Panel>
      )}
    </Box>
  );
}
