import { Alert, AlertTitle, Box, Button, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { looseningSettingsQuery } from '@/shared/api/looseningRead';
import { dismissLoosening, planKeys, type Plan } from '../api/planQueries';

/**
 * The limits the owner loosened since the last plan, with the old and new value (BK-10b), above the smart-rule banner. Read on request from the stored change rows and never part of the
 * stored plan. Each line has a Dismiss button (insert-only, no password); a failed dismiss says so and the line stays. When the loosenings could not be read the block says so instead of showing nothing.
 * Text only, so markup in a line is shown as written.
 */
export function LooseningBanner({ plan }: { plan: Plan }) {
  const lines = plan.loosenings ?? [];
  const queryClient = useQueryClient();
  const setting = useQuery({ ...looseningSettingsQuery, enabled: lines.length > 0 });
  const dismiss = useMutation({
    mutationFn: dismissLoosening,
    // a dismissal is of the change, so every plan read changes
    onSuccess: () => queryClient.invalidateQueries({ queryKey: planKeys.all }),
  });
  if (plan.looseningsUnavailable === true) return <Alert severity="warning">Loosenings could not be read.</Alert>;
  if (lines.length === 0) return null;
  return (
    <Alert severity="warning" icon={false} aria-label="Limits loosened since the last plan">
      <AlertTitle>Limits loosened since the last plan</AlertTitle>
      {setting.data?.autoDismiss === true && (
        <Typography variant="body2" sx={{ mb: 0.5 }}>
          Automatic dismissal is ON: each line hides after one plan.
        </Typography>
      )}
      <Box component="ul" sx={{ m: 0, pl: 2 }}>
        {lines.map((l) => (
          <li key={l.noticeId}>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1 }}>
              <span>{l.sentence}</span>
              <Button
                size="small"
                variant="outlined"
                disabled={dismiss.isPending}
                aria-label={`Dismiss the loosening of ${l.key}`}
                onClick={() => {
                  dismiss.mutate(l.noticeId);
                }}
              >
                Dismiss
              </Button>
            </Box>
          </li>
        ))}
      </Box>
      {dismiss.isError && (
        <Typography role="alert" variant="body2" sx={{ mt: 0.5 }}>
          The loosening could not be dismissed; it stays shown. Try again.
        </Typography>
      )}
    </Alert>
  );
}
