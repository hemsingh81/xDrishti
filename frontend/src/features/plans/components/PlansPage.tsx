import ChecklistIcon from '@mui/icons-material/ChecklistOutlined';
import { Alert, Box, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { plansQuery } from '../api/planQueries';
import { DigestList } from './DigestList';
import { GradePanel } from './GradePanel';
import { JournalPanel } from './JournalPanel';
import { PlanDetail } from './PlanDetail';
import { PlanList } from './PlanList';
import { Suggestions } from './Suggestions';

/** Plan review (minimal, P7-17): the nightly plan, the owner's decisions, and the after-the-fact panels. */
export function PlansPage() {
  const plans = useQuery(plansQuery);
  const [params] = useSearchParams();
  const asked = Number(params.get('plan'));
  const list = plans.data ?? [];
  const selected = list.find((p) => p.id === asked) ?? list[0];

  return (
    <>
      <PageHeader
        title="Plan review"
        icon={ChecklistIcon}
        hue="review"
        description="Read the evening's plan and decide each ticket before the cutoff."
      />
      <Alert severity="info" sx={{ mb: 2 }}>
        This screen places no order, and none exists in this phase. Plans are for paper accounts only. Approving means
        watching a ticket, nothing more.
      </Alert>
      {plans.isPending && <LoadingState label="Loading plans…" />}
      {plans.isError && <ErrorState error={plans.error} />}
      {plans.data && list.length === 0 && (
        <Typography>No plan yet. The nightly plan appears here once it is published.</Typography>
      )}
      {selected && (
        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '300px 1fr' }, alignItems: 'start' }}>
          <PlanList plans={list} selectedId={selected.id} />
          <Stack spacing={2}>
            <PlanDetail key={selected.id} planId={selected.id} />
            <Suggestions planId={selected.id} />
            <GradePanel planId={selected.id} />
            <JournalPanel planId={selected.id} />
            <DigestList />
          </Stack>
        </Box>
      )}
    </>
  );
}
