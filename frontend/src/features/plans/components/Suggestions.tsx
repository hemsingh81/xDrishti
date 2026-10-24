import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { markSeen, planKeys, suggestionsQuery } from '../api/planQueries';
import { decisionError } from '../lib/planText';

/** The suggestions of a plan: advice only. Marking one seen changes nothing else. */
export function Suggestions({ planId }: { planId: number }) {
  const query = useQuery(suggestionsQuery(planId));
  const queryClient = useQueryClient();
  const seen = useMutation({
    mutationFn: (id: number) => markSeen(planId, id),
    onSettled: () => queryClient.invalidateQueries({ queryKey: planKeys.suggestions(planId) }),
  });

  return (
    <Card component="section" aria-labelledby="plan-suggestions">
      <CardContent>
        <Typography id="plan-suggestions" variant="h2" sx={{ mb: 1 }}>
          Suggestions
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Advice only: nothing here changes the plan or places an order.
        </Typography>
        {query.isPending && <LoadingState label="Loading suggestions…" />}
        {query.isError && <ErrorState error={query.error} />}
        {seen.isError && <Alert severity="error">{decisionError(seen.error)}</Alert>}
        {query.data?.length === 0 && <Typography>No suggestions for this plan.</Typography>}
        <Stack component="ul" spacing={1} sx={{ m: 0, p: 0, listStyle: 'none' }}>
          {query.data?.map((s) => (
            <Stack
              component="li"
              key={s.id}
              direction="row"
              sx={{ gap: 1.5, alignItems: 'center', justifyContent: 'space-between' }}
            >
              <div>
                <Typography variant="body2">
                  <strong>{s.subject}</strong> ({s.kind}): {s.text}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {formatIstDayAndTime(s.createdAt)} · {s.seen ? 'Seen' : 'New'}
                </Typography>
              </div>
              {!s.seen && (
                <Button
                  size="small"
                  disabled={seen.isPending}
                  aria-label={`Mark seen: ${s.subject} (${s.kind})`}
                  onClick={() => {
                    seen.mutate(s.id);
                  }}
                >
                  Mark seen
                </Button>
              )}
            </Stack>
          ))}
        </Stack>
      </CardContent>
    </Card>
  );
}
