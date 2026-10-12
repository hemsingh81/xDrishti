import { Button, Stack, Typography } from '@mui/material';
import type { GapsOverview } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { gapsSummary } from '../lib/gaps';

interface GapsSummaryProps {
  gaps: GapsOverview;
  onView: () => void;
}

/** In the Dhan popover: how many sessions have missing bars now ("12 open · 3 unfillable") and a button to the Gaps tab. */
export function GapsSummary({ gaps, onView }: GapsSummaryProps) {
  const any = gaps.open + gaps.unfillable + gaps.filled > 0;
  return (
    <Stack spacing={0.25} sx={{ alignItems: 'flex-start' }}>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        Gaps: {gapsSummary(gaps)}
      </Typography>
      {gaps.lastDetectedAt && (
        <Typography variant="caption" color="text.secondary">
          Last found <time dateTime={gaps.lastDetectedAt}>{formatIstDateTime(gaps.lastDetectedAt)}</time>
        </Typography>
      )}
      {any && (
        <Button size="small" onClick={onView} sx={{ px: 0.5 }}>
          View gaps
        </Button>
      )}
    </Stack>
  );
}
