import { Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { digestsQuery } from '../api/planQueries';

/** The weekly learning digests: proposals as text. Nothing in a digest is applied. */
export function DigestList() {
  const query = useQuery(digestsQuery);
  return (
    <Card component="section" aria-labelledby="plan-digests">
      <CardContent>
        <Typography id="plan-digests" variant="h2" sx={{ mb: 0.5 }}>
          Weekly digests
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Read only: a digest lists proposals and applies nothing.
        </Typography>
        {query.isPending && <LoadingState label="Loading digests…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data?.length === 0 && <Typography>No digest yet.</Typography>}
        {query.data?.map((d) => (
          <details key={d.isoWeek}>
            <summary>
              {d.isoWeek} ({d.weekStart} to {d.weekEnd})
            </summary>
            {d.sections.map((s) => (
              <section key={s.key}>
                <Typography variant="subtitle2" component="h4">
                  {s.title}
                </Typography>
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {s.lines.map((line, i) => (
                    <li key={`${s.key}-${String(i)}`}>{line}</li>
                  ))}
                </ul>
              </section>
            ))}
          </details>
        ))}
      </CardContent>
    </Card>
  );
}
