import { Box, LinearProgress, Typography } from '@mui/material';
import type { RefreshRun } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { phaseLabel, triggerLabel } from '../lib/labels';

/** Live state of a running refresh: what it is doing now and how many rows it has read. */
export function RefreshProgress({ run, id }: { run: RefreshRun; id: string }) {
  return (
    <Box id={id}>
      <Typography role="status" sx={{ fontWeight: 600 }}>
        {triggerLabel(run.trigger)} in progress — {phaseLabel(run.phase)}
      </Typography>
      <LinearProgress aria-label="Refresh progress" sx={{ my: 1, borderRadius: 1 }} />
      <Typography variant="body2" color="text.secondary" sx={numeric}>
        {formatNumber(run.rowsRead)} rows read
      </Typography>
    </Box>
  );
}
