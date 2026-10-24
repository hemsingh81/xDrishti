import { Card, CardActionArea, CardContent, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { formatWeekdayDate } from '@/shared/lib/format';
import type { Plan } from '../api/planQueries';
import { PlanFlags } from './PlanFlags';

interface Props {
  plans: Plan[];
  selectedId: number | undefined;
}

/** Plans by date, newest first; the state and the basis flags are text on every row. */
export function PlanList({ plans, selectedId }: Props) {
  return (
    <Stack component="ul" spacing={1} aria-label="Plans by date" sx={{ m: 0, p: 0, listStyle: 'none' }}>
      {plans.map((p) => (
        <li key={p.id}>
          <Card variant="outlined" sx={p.id === selectedId ? { borderWidth: 2 } : undefined}>
            <CardActionArea
              component={RouterLink}
              to={`?plan=${String(p.id)}`}
              aria-current={p.id === selectedId ? 'true' : undefined}
              aria-label={`Plan for ${formatWeekdayDate(p.forDate)}, version ${String(p.version)}`}
            >
              <CardContent>
                <Stack spacing={0.75}>
                  <Typography sx={{ fontWeight: 600 }}>
                    {formatWeekdayDate(p.forDate)} · v{p.version}
                  </Typography>
                  <PlanFlags plan={p} />
                  <Typography variant="caption" color="text.secondary">
                    {p.primary} primary · {p.reserve} reserve · {p.rejected} rejected
                  </Typography>
                </Stack>
              </CardContent>
            </CardActionArea>
          </Card>
        </li>
      ))}
    </Stack>
  );
}
