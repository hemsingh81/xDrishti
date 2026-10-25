import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { VerdictChip } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { planCheckQuery } from '../api/learningQueries';
import { verdictOf } from '../lib/metricView';
import { CriteriaList } from './CriteriaList';
import { Panel } from './Panel';
import { SessionsTable } from './SessionsTable';

const lines = (label: string, items: readonly string[]) => (
  <Box component="ul" aria-label={label} sx={{ m: 0, pl: 2.5 }}>
    {items.map((item, index) => (
      <li key={index}>{item}</li>
    ))}
  </Box>
);

/** The P7 (plan) phase check: the exit call, the criteria, the ten sessions, what is still off, blockers, counts and limits. */
export function PlanCheckPanel() {
  const query = useQuery(planCheckQuery);
  if (query.isPending) return <LoadingState label="Loading the P7 check…" />;
  if (query.isError)
    return (
      <Panel title="P7 phase check">
        <ErrorState error={query.error} />
      </Panel>
    );
  const r = query.data.report;
  const off = [
    ...r.services.filter((s) => s.required && s.state !== 'on').map((s) => `Service ${s.id} (${s.role}): ${s.state}`),
    ...r.switches.filter((s) => !s.on).map((s) => `Switch ${s.name}: OFF. ${s.effect}`),
  ];
  return (
    <Panel title="P7 phase check (the nightly plan)">
      <Box sx={{ display: 'grid', gap: 1.5 }}>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <VerdictChip verdict={verdictOf(r.exitVerdict)} />
          <Typography>{r.exitCriterion}</Typography>
        </Box>
        <CriteriaList label="P7 criteria" criteria={r.criteria} />
        <SessionsTable sessions={r.sessions} />
        <Typography variant="subtitle2">Services and switches that are still off</Typography>
        {off.length === 0 ? <Typography variant="body2">None.</Typography> : lines('Still off', off)}
        {r.blockers.length > 0 && (
          <>
            <Typography variant="subtitle2">Blockers</Typography>
            {lines('Blockers', r.blockers)}
          </>
        )}
        <Typography variant="body2">{r.counts.map((c) => `${c.name} ${c.value}`).join(', ')}</Typography>
        <Typography variant="subtitle2">Limits</Typography>
        {lines('P7 limits', r.limits)}
      </Box>
    </Panel>
  );
}
