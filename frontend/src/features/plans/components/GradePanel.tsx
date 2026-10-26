import { Card, CardContent, Link, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { ErrorState, LoadingState } from '@/shared/ui';
import { touchTarget } from '@/theme/tokens';
import { gradeQuery } from '../api/planQueries';

const r = (v: number | null) => (v === null ? '-' : `${v.toFixed(2)} R`);

/** The grade of every ticket, read only, always labelled with what it rests on. */
export function GradePanel({ planId }: { planId: number }) {
  const query = useQuery(gradeQuery(planId));
  return (
    <Card component="section" aria-labelledby="plan-grade">
      <CardContent>
        <Typography id="plan-grade" variant="h2" sx={{ mb: 0.5 }}>
          Grade
        </Typography>
        {query.isPending && <LoadingState label="Loading grade…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data && (
          <>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              {query.data.basis}. {query.data.pending > 0 ? `${query.data.pending} ticket(s) not graded yet.` : ''}
            </Typography>
            {query.data.tickets.length === 0 ? (
              <Typography>No tickets to grade.</Typography>
            ) : (
              <table aria-label="Ticket grades" style={{ width: '100%', textAlign: 'left' }}>
                <thead>
                  <tr>
                    <th scope="col">Ticket</th>
                    <th scope="col">Group</th>
                    <th scope="col">Decision</th>
                    <th scope="col">Status</th>
                    <th scope="col">Result</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.tickets.map((t) => (
                    <tr key={t.itemId}>
                      <td>
                        {t.symbol} {t.side}
                      </td>
                      <td>{t.group}</td>
                      <td>{t.decision || '-'}</td>
                      <td>{t.status}</td>
                      <td>{r(t.r)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <Link
              component={RouterLink}
              to="/learning?tab=plan"
              // A link on its own line is a touch target: 44 px high on a phone, padded not stretched.
              sx={{ display: 'inline-flex', alignItems: 'center', mt: 1, minHeight: { xs: touchTarget.phone, sm: 24 } }}
            >
              Plan vs actual in Learning
            </Link>
          </>
        )}
      </CardContent>
    </Card>
  );
}
