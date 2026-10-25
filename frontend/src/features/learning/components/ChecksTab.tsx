import { Box, Typography } from '@mui/material';
import { LearningCheckPanel } from './LearningCheckPanel';
import { PlanCheckPanel } from './PlanCheckPanel';

/** The two phase checks, each judged by the server from stored records. The screen never merges them into one score. */
export function ChecksTab() {
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">
        Two separate checks. A criterion is MET only where the server says MET; a missing input is NOT EVALUABLE, never
        a pass.
      </Typography>
      <PlanCheckPanel />
      <LearningCheckPanel />
    </Box>
  );
}
