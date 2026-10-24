import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { formatIstDayAndTime, formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { plansQuery } from '../api/planQueries';
import { lapseWording, stateLabel } from '../lib/planText';
import { PlanBasisLine } from './PlanBasisLine';
import { PlanFlags } from './PlanFlags';

/** Today tile: the newest plan's state, counts and review cutoff. */
export function PlanTile() {
  const query = useQuery(plansQuery);
  const plan = query.data?.[0];
  return (
    <Card component="section" aria-labelledby="plan-tile">
      <CardContent>
        <Stack spacing={1}>
          <Typography id="plan-tile" variant="h2">
            Plan
          </Typography>
          {query.isPending && <LoadingState label="Loading plan…" />}
          {query.isError && <ErrorState error={query.error} />}
          {query.data && !plan && <Typography color="text.secondary">No plan yet.</Typography>}
          {plan && (
            <>
              <Typography>
                {formatWeekdayDate(plan.forDate)}: {stateLabel(plan.state)}. {plan.primary} primary, {plan.reserve}{' '}
                reserve, {plan.rejected} rejected.
              </Typography>
              <PlanFlags plan={plan} />
              <PlanBasisLine basis={plan.basis} />
              <Typography variant="body2" color="text.secondary">
                Review cutoff {formatIstDayAndTime(plan.reviewCutoff)} IST. {lapseWording} No order is placed.
              </Typography>
            </>
          )}
          <div>
            <Button component={RouterLink} to="/plans" variant="contained" endIcon={<ArrowForwardIcon />}>
              Plan review
            </Button>
          </div>
        </Stack>
      </CardContent>
    </Card>
  );
}
